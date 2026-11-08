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
        /// <summary>The whole block was moved to a different centre (see MultiSpawnPlan.centre).</summary>
        Moved,
        /// <summary>No clear space was found within the search radius (see MultiSpawnPlan.bestCentre).</summary>
        NoClearSpace,
        /// <summary>The count was zero or negative.</summary>
        InvalidCount,
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
        /// <summary>Final slot positions relative to the requested point, in layout order (slot 0 is the leader). Empty unless the status is Placed, Shifted or Moved.</summary>
        public readonly List<SpawnOffset> slots = [];
        /// <summary>Row of each slot in `slots`.</summary>
        public readonly List<int> slotRows = [];
        public int perRow;
        public int rows;
        /// <summary>Number of slots shifted outward along the line.</summary>
        public int shiftedSlots;
        /// <summary>Offset of the block's centre from the requested point (non-zero when Moved).</summary>
        public SpawnOffset centre;
        /// <summary>When NoClearSpace: the candidate centre with the most free slots, and how many.</summary>
        public SpawnOffset bestCentre;
        public int bestFreeSlots;
        /// <summary>Number of clear-space checks made (for diagnostics).</summary>
        public int checks;
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
        /// Plan the slots: lay them out, check each for clear space, shift blocked slots outward along the line (by spacing/2, up to 3 times, keeping the order),
        /// and if any slot is still blocked move the whole block, searching candidate centres in rings around the point
        /// (step = block diagonal, up to maxRings rings and maxSearchRadius) and taking the nearest centre where every slot is free.
        /// </summary>
        /// <param name="count">Number of vessels (any positive number).</param>
        /// <param name="direction">Abreast or trail.</param>
        /// <param name="spacing">Spacing between neighbours in a row (m).</param>
        /// <param name="rowSpacing">Spacing between rows (m).</param>
        /// <param name="perRow">Slots per row (from PerRow).</param>
        /// <param name="minSeparation">Minimum distance between accepted slots (m), normally the craft's size along the line.</param>
        /// <param name="isFree">Clear-space check for a slot at an offset from the requested point. Called many times; must not be null.</param>
        public static MultiSpawnPlan Plan(int count, MultiSpawnLineDirection direction, double spacing, double rowSpacing, int perRow, double minSeparation, Func<SpawnOffset, bool> isFree, int maxRings = DefaultMaxRings, double maxSearchRadius = DefaultMaxSearchRadius)
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
            var layout = new List<SpawnOffset>(count);
            var rowsOfSlots = new List<int>(count);
            ComputeSlots(count, direction, spacing, rowSpacing, perRow, layout, rowsOfSlots);

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
            // 1. In place, with shifting.
            if (TryPlace(direction, layout, order, new SpawnOffset(0, 0), spacing, minSeparation, isFree, true, placed, out int shifted, out int free, ref plan.checks))
            {
                Fill(plan, placed, rowsOfSlots);
                plan.shiftedSlots = shifted;
                plan.status = shifted > 0 ? MultiSpawnStatus.Shifted : MultiSpawnStatus.Placed;
                return plan;
            }
            plan.bestFreeSlots = free;
            plan.bestCentre = new SpawnOffset(0, 0);

            // 2. Move the whole block: rings of candidate centres around the point.
            double minR = 0, maxR = 0, minF = 0, maxF = 0;
            for (int i = 0; i < count; ++i)
            {
                minR = Math.Min(minR, layout[i].right); maxR = Math.Max(maxR, layout[i].right);
                minF = Math.Min(minF, layout[i].forward); maxF = Math.Max(maxF, layout[i].forward);
            }
            double width = maxR - minR + spacing, depth = maxF - minF + spacing;
            double step = Math.Max(Math.Sqrt(width * width + depth * depth), Math.Max(spacing, 1));
            for (int ring = 1; ring <= maxRings; ++ring)
            {
                double radius = ring * step;
                if (radius > maxSearchRadius) break;
                int candidates = Math.Max(8, 8 * ring);
                for (int k = 0; k < candidates; ++k)
                {
                    double angle = 2 * Math.PI * k / candidates;
                    var centre = new SpawnOffset(radius * Math.Sin(angle), radius * Math.Cos(angle));
                    if (TryPlace(direction, layout, order, centre, spacing, minSeparation, isFree, true, placed, out shifted, out free, ref plan.checks))
                    {
                        Fill(plan, placed, rowsOfSlots);
                        plan.shiftedSlots = shifted;
                        plan.centre = centre;
                        plan.status = MultiSpawnStatus.Moved;
                        return plan;
                    }
                    if (free > plan.bestFreeSlots)
                    {
                        plan.bestFreeSlots = free;
                        plan.bestCentre = centre;
                    }
                }
            }
            plan.status = MultiSpawnStatus.NoClearSpace;
            return plan;
        }

        static void Fill(MultiSpawnPlan plan, SpawnOffset[] placed, List<int> rowsOfSlots)
        {
            plan.slots.Clear();
            plan.slotRows.Clear();
            plan.slots.AddRange(placed);
            plan.slotRows.AddRange(rowsOfSlots);
        }

        static bool TryPlace(MultiSpawnLineDirection direction, List<SpawnOffset> layout, int[] order, SpawnOffset centre, double spacing, double minSeparation, Func<SpawnOffset, bool> isFree, bool allowShift, SpawnOffset[] placed, out int shifted, out int free, ref int checks)
        {
            shifted = 0;
            free = 0;
            double minSepSqr = Math.Max(0, minSeparation) * Math.Max(0, minSeparation);
            for (int n = 0; n < order.Length; ++n)
            {
                int i = order[n];
                var basePos = centre + layout[i];
                var outward = OutwardDirection(direction, layout[i]);
                bool ok = false;
                int maxSteps = allowShift ? MaxShiftSteps : 0;
                for (int s = 0; s <= maxSteps; ++s)
                {
                    var candidate = basePos + outward * (s * spacing * 0.5);
                    if (!ClearOfPlaced(candidate, placed, order, n, minSepSqr)) continue;
                    ++checks;
                    if (!isFree(candidate)) continue;
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
    }
}
