// BDArmory Overdrive: multi-spawn (OD1) layout and clear-space planning. Pure logic, no Unity dependencies, so it can be unit tested outside KSP.
using System;
using System.Collections.Generic;

namespace BDArmory.VesselSpawning
{
    /// <summary>
    /// Line abreast (perpendicular to the heading, rows stack behind) or in trail (along the heading behind the leader, rows side by side).
    /// </summary>
    public enum MultiSpawnLineDirection
    {
        Abreast = 0,
        Trail = 1,
    }

    public enum MultiSpawnStatus
    {
        /// <summary>Every slot was free where the layout put it.</summary>
        Placed,
        /// <summary>Some slots were shifted outward along the line to find clear space.</summary>
        Shifted,
        /// <summary>The whole block was moved to a different centre (see MultiSpawnPlan.centre). MultiSpawnPlan.wrapped tells whether its rows also wrapped early.</summary>
        Moved,
        /// <summary>No clear space was found within the search radius (see MultiSpawnPlan.bestCentre and checkBudgetExhausted).</summary>
        NoClearSpace,
        /// <summary>The count was zero or negative.</summary>
        InvalidCount,
        /// <summary>At the requested point, clear space along a line ran out, so the remaining slots wrapped into the next row (more or shorter rows than requested).</summary>
        Wrapped,
    }

    /// <summary>
    /// A horizontal offset in metres in the spawn frame: right of the heading and forward along the heading.
    /// </summary>
    public readonly struct SpawnOffset(double right, double forward)
    {
        public readonly double right = right;
        public readonly double forward = forward;
        public double Length => Math.Sqrt(right * right + forward * forward);
        public static SpawnOffset operator +(SpawnOffset a, SpawnOffset b) => new(a.right + b.right, a.forward + b.forward);
        public static SpawnOffset operator -(SpawnOffset a, SpawnOffset b) => new(a.right - b.right, a.forward - b.forward);
        public static SpawnOffset operator *(SpawnOffset a, double s) => new(a.right * s, a.forward * s);
        public override string ToString() => $"({right:0.0}, {forward:0.0})";
    }

    /// <summary>
    /// The result of MultiSpawnLayout.Plan.
    /// </summary>
    public sealed class MultiSpawnPlan
    {
        public MultiSpawnStatus status;
        /// <summary>Final slot positions relative to the requested point, in layout order (slot 0 is the leader). Empty unless the status is Placed, Shifted, Wrapped or Moved.</summary>
        public readonly List<SpawnOffset> slots = [];
        /// <summary>Row of each slot in `slots`.</summary>
        public readonly List<int> slotRows = [];
        public int perRow;
        /// <summary>Rows of the layout (the rows actually used when `wrapped`).</summary>
        public int rows;
        /// <summary>Number of slots shifted outward along the line.</summary>
        public int shiftedSlots;
        /// <summary>Offset of the block's centre from the requested point (non-zero when Moved).</summary>
        public SpawnOffset centre;
        /// <summary>When NoClearSpace: the candidate centre with the most free slots, and how many.</summary>
        public SpawnOffset bestCentre;
        public int bestFreeSlots;
        /// <summary>Number of clear-space checks made (calls of isFree; a position that was already checked is answered from a cache).</summary>
        public int checks;
        /// <summary>The rows wrapped early because clear space along a line ran out (status Wrapped, or Moved with wrapped rows).</summary>
        public bool wrapped;
        /// <summary>How far from the requested point candidate centres were searched (m).</summary>
        public double searchReach;
        /// <summary>The search stopped because the clear-space check budget ran out (only with NoClearSpace).</summary>
        public bool checkBudgetExhausted;
        public double MovedBy => centre.Length;
    }

    /// <summary>
    /// Multi-spawn layout: N copies in a line abreast or in trail, wrapping into rows, with clear-space finding.
    /// Algorithm: skizzy-protocol design 06-flight-control.md §9.1.
    /// </summary>
    public static class MultiSpawnLayout
    {
        public const int LargeSpawnWarningThreshold = 50;
        public const double DefaultMaxRowLength = 2000;
        public const int MaxShiftSteps = 3;
        public const int DefaultMaxRings = 8;
        public const double DefaultMaxSearchRadius = 5000;
        /// <summary>The ring search reaches at least this far (m), even for a small block whose 8 × diagonal would only reach a few hundred metres.</summary>
        public const double DefaultMinSearchReach = 2000;
        /// <summary>Most rings used to cover the minimum reach (the step between rings grows instead).</summary>
        public const int MaxRingsForMinReach = 16;
        /// <summary>
        /// Clear-space check budget of a plan: at least this many checks, or CheckBudgetPerSlot per slot, whichever is more.
        /// Enough for the default search over a fully blocked area; most failing checks in-game are cheap (one or two terrain queries).
        /// </summary>
        public const int MinCheckBudget = 20000;
        public const int CheckBudgetPerSlot = 50;
        public const int DefaultMaxPartsPerFrame = 400;
        public const int DefaultMinFramesBetweenSpawns = 2;
        public const double DefaultPartsPerPhysicsMs = 250; // Rough default: 1 ms of physics per 250 parts.

        /// <summary>Margin between craft: max(10 m, 0.25 × size) on the surface, max(30 m, 0.5 × size) in the air.</summary>
        public static double AutoMargin(double size, bool air)
        {
            size = Math.Max(0, size);
            return air ? Math.Max(30, 0.5 * size) : Math.Max(10, 0.25 * size);
        }

        /// <summary>Spacing between neighbouring slots: the craft's size along the line plus the margin.</summary>
        public static double AutoSpacing(double size, bool air) => Math.Max(0, size) + AutoMargin(size, air);

        /// <summary>Distance between rows: one spacing plus the craft's extent across the line (its length for abreast, its width for trail).</summary>
        public static double RowSpacing(double spacing, double extentAcrossLine) => Math.Max(0, spacing) + Math.Max(0, extentAcrossLine);

        /// <summary>
        /// Slots per row: the requested number (0 or less for auto, meaning all in one row), capped by what fits within maxRowLength.
        /// </summary>
        public static int PerRow(int count, double spacing, int requestedPerRow = 0, double maxRowLength = DefaultMaxRowLength)
        {
            if (count <= 0) return 0;
            int perRow = requestedPerRow > 0 ? requestedPerRow : count;
            if (spacing > 0 && maxRowLength > 0)
            {
                double fitD = Math.Floor(maxRowLength / spacing) + 1;
                if (fitD < perRow) perRow = (int)fitD;
            }
            return Math.Max(1, Math.Min(perRow, count));
        }

        public static int Rows(int count, int perRow) => count <= 0 || perRow <= 0 ? 0 : (count + perRow - 1) / perRow;

        /// <summary>Most rows a wrapped layout may use: twice the rows of the requested layout, and at least 3 more, so the block stays compact.</summary>
        public static int MaxWrappedRows(int count, int perRow)
        {
            int rows = Rows(count, perRow <= 0 ? count : perRow);
            return Math.Max(2 * rows, rows + 3);
        }

        /// <summary>The clear-space check budget of a plan for count slots.</summary>
        public static int DefaultCheckBudget(int count) => (int)Math.Min(int.MaxValue, Math.Max(MinCheckBudget, (long)CheckBudgetPerSlot * Math.Max(0, count)));

        /// <summary>
        /// Ring search geometry. The reach is maxRings × the natural step (the block diagonal, so it scales with craft size and count), but at least minSearchReach
        /// and at most maxSearchRadius. The rings are a natural step apart, except that covering the minimum reach uses at most max(maxRings, MaxRingsForMinReach)
        /// rings (the step grows instead). Returns the number of rings (0: no ring search).
        /// </summary>
        public static int SearchRings(double naturalStep, int maxRings, double minSearchReach, double maxSearchRadius, out double step, out double reach)
        {
            naturalStep = Math.Max(1, naturalStep);
            reach = Math.Min(Math.Max(0, maxSearchRadius), Math.Max(Math.Max(0, maxRings) * naturalStep, Math.Max(0, minSearchReach)));
            step = naturalStep;
            if (reach <= 0) { reach = 0; return 0; }
            int rings = (int)Math.Ceiling(reach / naturalStep - 1e-9);
            rings = Math.Max(1, Math.Min(rings, Math.Max(maxRings, MaxRingsForMinReach)));
            step = reach / rings;
            return rings;
        }

        /// <summary>
        /// The layout before clear-space checks, relative to the requested point.
        /// Abreast: each row is centred on the point's right axis, rows stack behind (negative forward) by rowSpacing.
        /// Trail: the leader is at the point, each row runs behind it by spacing, rows sit side by side (centred) by rowSpacing.
        /// </summary>
        public static void ComputeSlots(int count, MultiSpawnLineDirection direction, double spacing, double rowSpacing, int perRow, List<SpawnOffset> result, List<int> rowsOut = null)
        {
            result.Clear();
            rowsOut?.Clear();
            if (count <= 0) return;
            if (perRow <= 0) perRow = count;
            int rows = Rows(count, perRow);
            for (int i = 0; i < count; ++i)
            {
                int row = i / perRow;
                int col = i % perRow;
                int inRow = row == rows - 1 ? count - row * perRow : perRow;
                SpawnOffset offset = direction == MultiSpawnLineDirection.Abreast
                    ? new SpawnOffset((col - (inRow - 1) * 0.5) * spacing, -row * rowSpacing)
                    : new SpawnOffset((row - (rows - 1) * 0.5) * rowSpacing, -col * spacing);
                result.Add(offset);
                rowsOut?.Add(row);
            }
        }

        /// <summary>
        /// The unit direction a slot is shifted in when it's blocked: outward along its line (away from the line's centre for abreast, further back for trail).
        /// </summary>
        public static SpawnOffset OutwardDirection(MultiSpawnLineDirection direction, SpawnOffset layoutSlot)
        {
            if (direction == MultiSpawnLineDirection.Trail) return new SpawnOffset(0, -1);
            return new SpawnOffset(layoutSlot.right < 0 ? -1 : 1, 0);
        }

        /// <summary>Estimated physics cost of the parts in milliseconds per frame.</summary>
        public static double EstimatePhysicsMs(long totalParts, double partsPerMs = DefaultPartsPerPhysicsMs) => partsPerMs > 0 ? Math.Max(0, totalParts) / partsPerMs : 0;

        /// <summary>Frames to wait between spawning vessels so that at most maxPartsPerFrame parts are built per frame.</summary>
        public static int FramesBetweenSpawns(int partsPerVessel, int maxPartsPerFrame = DefaultMaxPartsPerFrame, int minFrames = DefaultMinFramesBetweenSpawns)
        {
            if (maxPartsPerFrame <= 0) return Math.Max(1, minFrames);
            int frames = (Math.Max(0, partsPerVessel) + maxPartsPerFrame - 1) / maxPartsPerFrame;
            return Math.Max(Math.Max(1, minFrames), frames);
        }

        /// <summary>
        /// Plan the slots (06-flight-control.md §9.1):
        /// 1. lay them out at the requested point, check each for clear space, and shift blocked slots outward along the line (by spacing/2, up to 3 times, keeping the order);
        /// 2. if a slot is still blocked, lay the rows out again so that a line wraps into the next row where its clear space ends (as well as at perRow and maxRowLength);
        /// 3. if that fails too, move the whole block: candidate centres in rings around the point, nearest first (reach and step from SearchRings), first with the
        ///    requested layout and then with wrapping rows, taking the first centre where every slot is free.
        /// The search stops at maxChecks clear-space checks (0: DefaultCheckBudget(count)), so a blocked area can't stall the game.
        /// </summary>
        /// <param name="count">Number of vessels (any positive number).</param>
        /// <param name="direction">Abreast or trail.</param>
        /// <param name="spacing">Spacing between neighbours in a row (m).</param>
        /// <param name="rowSpacing">Spacing between rows (m).</param>
        /// <param name="perRow">Slots per row (from PerRow).</param>
        /// <param name="minSeparation">Minimum distance between accepted slots (m), normally the craft's size along the line.</param>
        /// <param name="isFree">Clear-space check for a slot at an offset from the requested point. Called many times; must not be null.</param>
        /// <param name="maxRings">Rings of the move search at the natural step (the block diagonal).</param>
        /// <param name="maxSearchRadius">Largest move (m).</param>
        /// <param name="minSearchReach">The move search reaches at least this far (m), within maxSearchRadius.</param>
        /// <param name="maxRowLength">Longest row (m) when rows wrap (step 2); 0 or less: unlimited.</param>
        /// <param name="maxChecks">Budget of isFree calls; 0 or less: DefaultCheckBudget(count).</param>
        public static MultiSpawnPlan Plan(int count, MultiSpawnLineDirection direction, double spacing, double rowSpacing, int perRow, double minSeparation, Func<SpawnOffset, bool> isFree,
            int maxRings = DefaultMaxRings, double maxSearchRadius = DefaultMaxSearchRadius, double minSearchReach = DefaultMinSearchReach, double maxRowLength = DefaultMaxRowLength, int maxChecks = 0)
        {
            if (isFree == null) throw new ArgumentNullException(nameof(isFree));
            var plan = new MultiSpawnPlan();
            if (count <= 0)
            {
                plan.status = MultiSpawnStatus.InvalidCount;
                return plan;
            }
            if (perRow <= 0) perRow = count;
            plan.perRow = perRow;
            plan.rows = Rows(count, perRow);
            var checker = new ClearSpaceChecker(isFree, maxChecks > 0 ? maxChecks : DefaultCheckBudget(count));
            double minSepSqr = Math.Max(0, minSeparation) * Math.Max(0, minSeparation);
            var layout = new List<SpawnOffset>(count);
            var rowsOfSlots = new List<int>(count);
            ComputeSlots(count, direction, spacing, rowSpacing, perRow, layout, rowsOfSlots);

            // Move search geometry: the natural step is the block diagonal.
            double minR = 0, maxR = 0, minF = 0, maxF = 0;
            for (int i = 0; i < count; ++i)
            {
                minR = Math.Min(minR, layout[i].right); maxR = Math.Max(maxR, layout[i].right);
                minF = Math.Min(minF, layout[i].forward); maxF = Math.Max(maxF, layout[i].forward);
            }
            double width = maxR - minR + spacing, depth = maxF - minF + spacing;
            int rings = SearchRings(Math.Max(Math.Sqrt(width * width + depth * depth), Math.Max(spacing, 1)), maxRings, minSearchReach, maxSearchRadius, out double step, out double reach);
            plan.searchReach = reach;

            // Process slots from the centre of each line outward, so that shifting a slot outward only moves it towards slots that haven't been placed yet.
            var order = new int[count];
            var along = new double[count];
            for (int i = 0; i < count; ++i)
            {
                order[i] = i;
                along[i] = direction == MultiSpawnLineDirection.Abreast ? Math.Abs(layout[i].right) : -layout[i].forward;
            }
            Array.Sort(order, (a, b) =>
            {
                int c = along[a].CompareTo(along[b]);
                return c != 0 ? c : a.CompareTo(b);
            });

            var placed = new SpawnOffset[count];
            var wrappedSlots = new List<SpawnOffset>(count);
            var wrappedRows = new List<int>(count);
            var origin = new SpawnOffset(0, 0);
            plan.bestCentre = origin;

            // 1. In place, with shifting.
            if (TryPlace(direction, layout, order, origin, spacing, minSepSqr, checker, placed, out int shifted, out int free))
            {
                Fill(plan, placed, rowsOfSlots);
                plan.shiftedSlots = shifted;
                plan.status = shifted > 0 ? MultiSpawnStatus.Shifted : MultiSpawnStatus.Placed;
                return Finish(plan, checker);
            }
            plan.bestFreeSlots = free;

            // 2. In place, wrapping rows where clear space along a line ends.
            if (TryPlaceWrapped(count, direction, origin, spacing, rowSpacing, perRow, minSepSqr, maxRowLength, checker, wrappedSlots, wrappedRows, out shifted, out int rowsUsed))
            {
                FillWrapped(plan, wrappedSlots, wrappedRows, rowsUsed, shifted);
                plan.status = MultiSpawnStatus.Wrapped;
                return Finish(plan, checker);
            }
            if (wrappedSlots.Count > plan.bestFreeSlots) plan.bestFreeSlots = wrappedSlots.Count;

            // 3. Move the whole block: rings of candidate centres around the point, first with the requested layout, then with wrapping rows.
            for (int pass = 0; pass < 2 && !checker.exhausted; ++pass)
            {
                for (int ring = 1; ring <= rings && !checker.exhausted; ++ring)
                {
                    double radius = ring * step;
                    int candidates = Math.Max(8, 8 * ring);
                    for (int k = 0; k < candidates && !checker.exhausted; ++k)
                    {
                        double angle = 2 * Math.PI * k / candidates;
                        var centre = new SpawnOffset(radius * Math.Sin(angle), radius * Math.Cos(angle));
                        bool ok;
                        if (pass == 0)
                        {
                            ok = TryPlace(direction, layout, order, centre, spacing, minSepSqr, checker, placed, out shifted, out free);
                            if (ok) Fill(plan, placed, rowsOfSlots);
                        }
                        else
                        {
                            ok = TryPlaceWrapped(count, direction, centre, spacing, rowSpacing, perRow, minSepSqr, maxRowLength, checker, wrappedSlots, wrappedRows, out shifted, out rowsUsed);
                            free = wrappedSlots.Count;
                            if (ok) FillWrapped(plan, wrappedSlots, wrappedRows, rowsUsed, shifted);
                        }
                        if (ok)
                        {
                            plan.shiftedSlots = shifted;
                            plan.centre = centre;
                            plan.status = MultiSpawnStatus.Moved;
                            return Finish(plan, checker);
                        }
                        if (free > plan.bestFreeSlots)
                        {
                            plan.bestFreeSlots = free;
                            plan.bestCentre = centre;
                        }
                    }
                }
            }
            plan.status = MultiSpawnStatus.NoClearSpace;
            plan.checkBudgetExhausted = checker.exhausted;
            return Finish(plan, checker);
        }

        static MultiSpawnPlan Finish(MultiSpawnPlan plan, ClearSpaceChecker checker)
        {
            plan.checks = checker.checks;
            return plan;
        }

        static void Fill(MultiSpawnPlan plan, SpawnOffset[] placed, List<int> rowsOfSlots)
        {
            plan.slots.Clear();
            plan.slotRows.Clear();
            plan.slots.AddRange(placed);
            plan.slotRows.AddRange(rowsOfSlots);
        }

        static void FillWrapped(MultiSpawnPlan plan, List<SpawnOffset> slots, List<int> rows, int rowsUsed, int shifted)
        {
            plan.slots.Clear();
            plan.slotRows.Clear();
            plan.slots.AddRange(slots);
            plan.slotRows.AddRange(rows);
            plan.rows = rowsUsed;
            plan.wrapped = true;
            plan.shiftedSlots = shifted;
        }

        /// <summary>
        /// isFree with a budget and a cache: a position checked before (to the centimetre) is answered from the cache and doesn't count as a check.
        /// </summary>
        sealed class ClearSpaceChecker(Func<SpawnOffset, bool> isFree, int budget)
        {
            readonly Dictionary<long, bool> cache = [];
            public int checks;
            public bool exhausted;

            /// <summary>False when the budget has run out and the position isn't cached (it wasn't checked).</summary>
            public bool TryCheck(SpawnOffset position, out bool free)
            {
                long key = ((long)Math.Round(position.right * 100) << 32) ^ ((long)Math.Round(position.forward * 100) & 0xFFFFFFFFL);
                if (cache.TryGetValue(key, out free)) return true;
                if (checks >= budget) { exhausted = true; return false; }
                ++checks;
                free = isFree(position);
                cache[key] = free;
                return true;
            }
        }

        static bool TryPlace(MultiSpawnLineDirection direction, List<SpawnOffset> layout, int[] order, SpawnOffset centre, double spacing, double minSepSqr, ClearSpaceChecker checker, SpawnOffset[] placed, out int shifted, out int free)
        {
            shifted = 0;
            free = 0;
            for (int n = 0; n < order.Length; ++n)
            {
                int i = order[n];
                var basePos = centre + layout[i];
                var outward = OutwardDirection(direction, layout[i]);
                bool ok = false;
                for (int s = 0; s <= MaxShiftSteps; ++s)
                {
                    var candidate = basePos + outward * (s * spacing * 0.5);
                    if (!ClearOfPlaced(candidate, placed, order, n, minSepSqr)) continue;
                    if (!checker.TryCheck(candidate, out bool isFree)) return false; // Out of budget.
                    if (!isFree) continue;
                    placed[i] = candidate;
                    if (s > 0) ++shifted;
                    ok = true;
                    break;
                }
                if (!ok) return false;
                ++free;
            }
            return true;
        }

        /// <summary>
        /// Lay the slots out row by row, wrapping into the next row when a row holds perRow slots, reaches maxRowLength, or its clear space ends.
        /// Abreast: rows stack behind the point (0, -rowSpacing, ...); each line is filled from its centre outward on both sides, alternately, and a side ends at the
        /// first position that is still blocked after shifting (clear space along the line ended). Trail: rows sit side by side at 0, +1, -1, +2, ... row spacings;
        /// each line runs backward from the row's head. Fails if a row gets no slot at all (no compact block here) or more than MaxWrappedRows rows are needed.
        /// Slots are returned in placement order (slot 0 is the leader at the centre of the first row).
        /// </summary>
        static bool TryPlaceWrapped(int count, MultiSpawnLineDirection direction, SpawnOffset centre, double spacing, double rowSpacing, int perRow, double minSepSqr, double maxRowLength,
            ClearSpaceChecker checker, List<SpawnOffset> placed, List<int> placedRows, out int shifted, out int rowsUsed)
        {
            placed.Clear();
            placedRows.Clear();
            shifted = 0;
            rowsUsed = 0;
            bool abreast = direction == MultiSpawnLineDirection.Abreast;
            int maxRows = MaxWrappedRows(count, perRow);
            double limit = maxRowLength > 0 ? (abreast ? 0.5 * maxRowLength : maxRowLength) : double.PositiveInfinity; // |along| ≤ limit
            for (int row = 0; row < maxRows && placed.Count < count; ++row)
            {
                double rowCoord = abreast ? -row * rowSpacing : ((row + 1) / 2) * (row % 2 == 1 ? 1 : -1) * rowSpacing;
                double nextPos = 0, nextNeg = -spacing; // Along-line coordinate of the next slot on each side (trail: measured backward, one side only).
                bool posOpen = true, negOpen = abreast;
                int posCount = 0, negCount = 0, inRow = 0;
                while (inRow < perRow && placed.Count < count && (posOpen || negOpen))
                {
                    bool pos = posOpen && (!negOpen || posCount <= negCount);
                    double basePos = pos ? nextPos : nextNeg;
                    double dir = pos ? 1 : -1;
                    bool found = false;
                    for (int s = 0; s <= MaxShiftSteps; ++s)
                    {
                        double alongLine = basePos + dir * s * spacing * 0.5;
                        if (Math.Abs(alongLine) > limit + 1e-6) break; // Past the end of the row.
                        var candidate = centre + (abreast ? new SpawnOffset(alongLine, rowCoord) : new SpawnOffset(rowCoord, -alongLine));
                        if (!ClearOf(candidate, placed, minSepSqr)) continue;
                        if (!checker.TryCheck(candidate, out bool isFree)) return false; // Out of budget.
                        if (!isFree) continue;
                        placed.Add(candidate);
                        placedRows.Add(row);
                        if (s > 0) ++shifted;
                        if (pos) { nextPos = alongLine + spacing; ++posCount; }
                        else { nextNeg = alongLine - spacing; ++negCount; }
                        ++inRow;
                        found = true;
                        break;
                    }
                    if (!found) // Clear space along this side of the line ended.
                    {
                        if (pos) posOpen = false;
                        else negOpen = false;
                    }
                }
                if (inRow == 0) return false;
                rowsUsed = row + 1;
            }
            return placed.Count == count;
        }

        static bool ClearOfPlaced(SpawnOffset candidate, SpawnOffset[] placed, int[] order, int placedCount, double minSepSqr)
        {
            if (minSepSqr <= 0) return true;
            for (int m = 0; m < placedCount; ++m)
            {
                var d = candidate - placed[order[m]];
                if (d.right * d.right + d.forward * d.forward < minSepSqr - 1e-9) return false;
            }
            return true;
        }

        static bool ClearOf(SpawnOffset candidate, List<SpawnOffset> placed, double minSepSqr)
        {
            if (minSepSqr <= 0) return true;
            for (int m = 0; m < placed.Count; ++m)
            {
                var d = candidate - placed[m];
                if (d.right * d.right + d.forward * d.forward < minSepSqr - 1e-9) return false;
            }
            return true;
        }
    }
}
