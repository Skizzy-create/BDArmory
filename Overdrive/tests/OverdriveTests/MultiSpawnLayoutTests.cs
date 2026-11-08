using System;
using System.Collections.Generic;
using System.Diagnostics;
using BDArmory.VesselSpawning;

namespace OverdriveTests
{
    public static class MultiSpawnLayoutTests
    {
        const MultiSpawnLineDirection Abreast = MultiSpawnLineDirection.Abreast;
        const MultiSpawnLineDirection Trail = MultiSpawnLineDirection.Trail;

        static List<SpawnOffset> Slots(int count, MultiSpawnLineDirection dir, double spacing, double rowSpacing, int perRow, List<int> rowsOut = null)
        {
            var l = new List<SpawnOffset>();
            MultiSpawnLayout.ComputeSlots(count, dir, spacing, rowSpacing, perRow, l, rowsOut);
            return l;
        }

        static void AssertSlot(SpawnOffset s, double right, double forward, string tag = "")
        {
            Assert.Near(right, s.right, 1e-9, $"{tag} right");
            Assert.Near(forward, s.forward, 1e-9, $"{tag} forward");
        }

        static double MinPairDistance(IList<SpawnOffset> slots)
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < slots.Count; ++i)
                for (int j = i + 1; j < slots.Count; ++j)
                    min = Math.Min(min, (slots[i] - slots[j]).Length);
            return min;
        }

        // ---- AutoMargin / AutoSpacing / RowSpacing ----
        [Test] public static void AutoMargin_SurfaceSmallUsesFloor() => Assert.Near(10, MultiSpawnLayout.AutoMargin(8, false));
        [Test] public static void AutoMargin_SurfaceLargeScales() => Assert.Near(25, MultiSpawnLayout.AutoMargin(100, false));
        [Test] public static void AutoMargin_AirSmallUsesFloor() => Assert.Near(30, MultiSpawnLayout.AutoMargin(20, true));
        [Test] public static void AutoMargin_AirLargeScales() => Assert.Near(50, MultiSpawnLayout.AutoMargin(100, true));
        [Test] public static void AutoMargin_NegativeSizeTreatedAsZero() { Assert.Near(10, MultiSpawnLayout.AutoMargin(-50, false)); Assert.Near(30, MultiSpawnLayout.AutoMargin(-50, true)); }
        [Test] public static void AutoSpacing_SurfaceIsSizePlusMargin() => Assert.Near(18, MultiSpawnLayout.AutoSpacing(8, false));
        [Test] public static void AutoSpacing_AirIsSizePlusMargin() => Assert.Near(150, MultiSpawnLayout.AutoSpacing(100, true));
        [Test] public static void AutoSpacing_NegativeSizeIsJustMargin() => Assert.Near(10, MultiSpawnLayout.AutoSpacing(-5, false));
        [Test] public static void RowSpacing_IsSum() => Assert.Near(70, MultiSpawnLayout.RowSpacing(50, 20));
        [Test] public static void RowSpacing_NegativesClamped() { Assert.Near(20, MultiSpawnLayout.RowSpacing(-10, 20)); Assert.Near(50, MultiSpawnLayout.RowSpacing(50, -20)); Assert.Near(0, MultiSpawnLayout.RowSpacing(-1, -1)); }

        // ---- PerRow / Rows ----
        [Test] public static void PerRow_NonPositiveCountIsZero() { Assert.Equal(0, MultiSpawnLayout.PerRow(0, 10)); Assert.Equal(0, MultiSpawnLayout.PerRow(-4, 10)); }
        [Test] public static void PerRow_AutoAllFitsIsCount() => Assert.Equal(5, MultiSpawnLayout.PerRow(5, 10));
        [Test] public static void PerRow_MaxRowLengthCap() => Assert.Equal(21, MultiSpawnLayout.PerRow(60, 100, 0, 2000));
        [Test] public static void PerRow_DefaultMaxRowLengthCap() => Assert.Equal(21, MultiSpawnLayout.PerRow(60, 100));
        [Test] public static void PerRow_RequestedWithRoom() => Assert.Equal(6, MultiSpawnLayout.PerRow(20, 10, 6));
        [Test] public static void PerRow_RequestedLargerThanCountIsCount() => Assert.Equal(5, MultiSpawnLayout.PerRow(5, 10, 50));
        [Test] public static void PerRow_RequestedStillCappedByRowLength() => Assert.Equal(21, MultiSpawnLayout.PerRow(60, 100, 40));
        [Test] public static void PerRow_AtLeastOneWithHugeSpacing() { Assert.Equal(1, MultiSpawnLayout.PerRow(10, 1e6)); Assert.Equal(1, MultiSpawnLayout.PerRow(10, 5000)); Assert.Equal(1, MultiSpawnLayout.PerRow(1, 1e9)); }
        [Test] public static void PerRow_ZeroSpacingNoCap() => Assert.Equal(30, MultiSpawnLayout.PerRow(30, 0));
        [Test] public static void Rows_SixtyOverTwentyOne() => Assert.Equal(3, MultiSpawnLayout.Rows(60, 21));
        [Test] public static void Rows_Exact() => Assert.Equal(1, MultiSpawnLayout.Rows(6, 6));
        [Test] public static void Rows_Remainder() => Assert.Equal(2, MultiSpawnLayout.Rows(7, 6));
        [Test] public static void Rows_ZeroCount() => Assert.Equal(0, MultiSpawnLayout.Rows(0, 6));
        [Test] public static void Rows_ZeroPerRow() => Assert.Equal(0, MultiSpawnLayout.Rows(5, 0));

        // ---- ComputeSlots: abreast ----
        [Test] public static void Slots_AbreastSingle() { var s = Slots(1, Abreast, 20, 30, 1); Assert.Equal(1, s.Count); AssertSlot(s[0], 0, 0); }

        [Test]
        public static void Slots_AbreastThreeOneRow()
        {
            var s = Slots(3, Abreast, 10, 50, 3);
            Assert.Equal(3, s.Count);
            AssertSlot(s[0], -10, 0, "0"); AssertSlot(s[1], 0, 0, "1"); AssertSlot(s[2], 10, 0, "2");
        }

        [Test]
        public static void Slots_AbreastFourOneRow()
        {
            var s = Slots(4, Abreast, 10, 50, 4);
            double[] r = [-15, -5, 5, 15];
            for (int i = 0; i < 4; ++i) AssertSlot(s[i], r[i], 0, i.ToString());
        }

        [Test]
        public static void Slots_AbreastWrapsIntoRows()
        {
            var rows = new List<int>();
            var s = Slots(7, Abreast, 10, 50, 3, rows);
            Assert.Equal(7, s.Count);
            int[] expRows = [0, 0, 0, 1, 1, 1, 2];
            double[] fwd = [0, -50, -100];
            for (int i = 0; i < 7; ++i)
            {
                Assert.Equal(expRows[i], rows[i], $"row of slot {i}");
                Assert.Near(fwd[expRows[i]], s[i].forward, 1e-9, $"forward of slot {i}");
            }
            AssertSlot(s[6], 0, -100, "last row centred");
            AssertSlot(s[3], -10, -50, "row1 col0");
        }

        [Test]
        public static void Slots_ResultClearedBeforeFilling()
        {
            var result = new List<SpawnOffset> { new(1, 1), new(2, 2), new(3, 3) };
            var rows = new List<int> { 9, 9, 9, 9 };
            MultiSpawnLayout.ComputeSlots(2, Abreast, 10, 10, 2, result, rows);
            Assert.Equal(2, result.Count, "slots");
            Assert.Equal(2, rows.Count, "rows");
            Assert.Equal(0, rows[0]);
        }

        [Test]
        public static void Slots_NonPositiveCountClearsAndReturnsEmpty()
        {
            var result = new List<SpawnOffset> { new(1, 1) };
            var rows = new List<int> { 1 };
            MultiSpawnLayout.ComputeSlots(0, Abreast, 10, 10, 1, result, rows);
            Assert.Equal(0, result.Count); Assert.Equal(0, rows.Count);
        }

        [Test]
        public static void Slots_ZeroPerRowMeansOneRow()
        {
            var s = Slots(4, Abreast, 10, 50, 0);
            for (int i = 0; i < 4; ++i) Assert.Near(0, s[i].forward, 1e-9);
        }

        // ---- ComputeSlots: trail ----
        [Test]
        public static void Slots_TrailThreeOneRow()
        {
            var s = Slots(3, Trail, 20, 30, 3);
            AssertSlot(s[0], 0, 0, "0"); AssertSlot(s[1], 0, -20, "1"); AssertSlot(s[2], 0, -40, "2");
        }

        [Test]
        public static void Slots_TrailTwoRowsSideBySide()
        {
            var rows = new List<int>();
            var s = Slots(6, Trail, 20, 30, 3, rows);
            for (int i = 0; i < 6; ++i)
            {
                int row = i / 3, col = i % 3;
                Assert.Equal(row, rows[i], $"row {i}");
                AssertSlot(s[i], row == 0 ? -15 : 15, -20.0 * col, $"slot {i}");
            }
        }

        [Test]
        public static void Slots_PairwiseSeparationProperty()
        {
            int[] counts = [1, 2, 3, 5, 6, 7, 10, 17, 24, 50, 99, 100];
            double[][] spacings = [[20, 30], [30, 20], [10, 10], [45, 120]];
            foreach (var dir in new[] { Abreast, Trail })
                foreach (var count in counts)
                    foreach (var sp in spacings)
                        foreach (int requested in new[] { 0, 3, 8 })
                        {
                            int perRow = MultiSpawnLayout.PerRow(count, sp[0], requested);
                            var s = Slots(count, dir, sp[0], sp[1], perRow);
                            Assert.Equal(count, s.Count);
                            if (count > 1)
                                Assert.True(MinPairDistance(s) >= Math.Min(sp[0], sp[1]) - 1e-9,
                                    $"{dir} count={count} spacing={sp[0]} rowSpacing={sp[1]} perRow={perRow}: min distance {MinPairDistance(s)}");
                        }
        }

        // ---- OutwardDirection ----
        [Test] public static void Outward_TrailIsBackward() { var d = MultiSpawnLayout.OutwardDirection(Trail, new SpawnOffset(30, 0)); AssertSlot(d, 0, -1); }
        [Test] public static void Outward_AbreastLeftGoesLeft() { var d = MultiSpawnLayout.OutwardDirection(Abreast, new SpawnOffset(-5, -10)); AssertSlot(d, -1, 0); }
        [Test] public static void Outward_AbreastRightGoesRight() { var d = MultiSpawnLayout.OutwardDirection(Abreast, new SpawnOffset(5, 0)); AssertSlot(d, 1, 0); }
        [Test] public static void Outward_AbreastCentreGoesRight() { var d = MultiSpawnLayout.OutwardDirection(Abreast, new SpawnOffset(0, 0)); AssertSlot(d, 1, 0); }

        // ---- EstimatePhysicsMs / FramesBetweenSpawns ----
        [Test] public static void Physics_DefaultRate() => Assert.Near(10, MultiSpawnLayout.EstimatePhysicsMs(2500));
        [Test] public static void Physics_NegativePartsIsZero() => Assert.Near(0, MultiSpawnLayout.EstimatePhysicsMs(-100));
        [Test] public static void Physics_CustomRate() => Assert.Near(5, MultiSpawnLayout.EstimatePhysicsMs(500, 100));
        [Test] public static void Physics_NonPositiveRateIsZero() => Assert.Near(0, MultiSpawnLayout.EstimatePhysicsMs(500, 0));
        [Test] public static void Frames_SmallVesselUsesMinFrames() => Assert.Equal(2, MultiSpawnLayout.FramesBetweenSpawns(40));
        [Test] public static void Frames_LargeVesselCeil() => Assert.Equal(3, MultiSpawnLayout.FramesBetweenSpawns(1000));
        [Test] public static void Frames_ZeroPartsIsMinFrames() => Assert.Equal(2, MultiSpawnLayout.FramesBetweenSpawns(0));
        [Test] public static void Frames_ExactMultiple() => Assert.Equal(5, MultiSpawnLayout.FramesBetweenSpawns(2000));
        [Test] public static void Frames_NonPositiveBudgetIsMinFrames() => Assert.Equal(2, MultiSpawnLayout.FramesBetweenSpawns(1000, 0));

        // ---- Plan ----
        static void AssertValidPlan(MultiSpawnPlan plan, Func<SpawnOffset, bool> isFree, double minSep)
        {
            foreach (var s in plan.slots) Assert.True(isFree(s), $"slot {s} not free");
            if (plan.slots.Count > 1)
                Assert.True(MinPairDistance(plan.slots) >= minSep - 1e-6, $"min pair distance {MinPairDistance(plan.slots)} < {minSep}");
        }

        [Test]
        public static void Plan_AllFreePlaced()
        {
            int calls = 0;
            Func<SpawnOffset, bool> free = _ => { ++calls; return true; };
            var plan = MultiSpawnLayout.Plan(6, Abreast, 20, 30, 6, 20, free);
            Assert.Equal(MultiSpawnStatus.Placed, plan.status);
            Assert.Equal(0, plan.shiftedSlots);
            AssertSlot(plan.centre, 0, 0, "centre");
            var expected = Slots(6, Abreast, 20, 30, 6);
            Assert.Equal(6, plan.slots.Count);
            for (int i = 0; i < 6; ++i) AssertSlot(plan.slots[i], expected[i].right, expected[i].forward, $"slot {i}");
            Assert.Equal(6, calls, "isFree calls");
            Assert.Equal(6, plan.checks, "plan.checks");
            Assert.Equal(6, plan.perRow); Assert.Equal(1, plan.rows);
        }

        [Test]
        public static void Plan_ReportsRowsPerSlot()
        {
            var plan = MultiSpawnLayout.Plan(7, Abreast, 10, 50, 3, 10, _ => true);
            Assert.Equal(MultiSpawnStatus.Placed, plan.status);
            Assert.Equal(3, plan.rows);
            int[] exp = [0, 0, 0, 1, 1, 1, 2];
            Assert.Equal(7, plan.slotRows.Count);
            for (int i = 0; i < 7; ++i) Assert.Equal(exp[i], plan.slotRows[i], $"row {i}");
        }

        [Test]
        public static void Plan_TrailAllFreeMatchesLayout()
        {
            var plan = MultiSpawnLayout.Plan(6, Trail, 20, 30, 3, 20, _ => true);
            Assert.Equal(MultiSpawnStatus.Placed, plan.status);
            var expected = Slots(6, Trail, 20, 30, 3);
            for (int i = 0; i < 6; ++i) AssertSlot(plan.slots[i], expected[i].right, expected[i].forward, $"slot {i}");
        }

        [Test]
        public static void Plan_NonPositiveCountIsInvalid()
        {
            foreach (int c in new[] { 0, -1, -100 })
            {
                var plan = MultiSpawnLayout.Plan(c, Abreast, 20, 30, 1, 10, _ => true);
                Assert.Equal(MultiSpawnStatus.InvalidCount, plan.status, $"count={c}");
                Assert.Equal(0, plan.slots.Count, $"count={c}");
            }
        }

        [Test] public static void Plan_NullIsFreeThrows() => Assert.Throws<ArgumentNullException>(() => MultiSpawnLayout.Plan(3, Abreast, 20, 30, 3, 10, null));

        [Test]
        public static void Plan_NullIsFreeThrowsEvenForInvalidCount() => Assert.Throws<ArgumentNullException>(() => MultiSpawnLayout.Plan(0, Abreast, 20, 30, 3, 10, null));

        [Test]
        public static void Plan_BlockedCentreSlotIsShifted()
        {
            Func<SpawnOffset, bool> free = p => !(Math.Abs(p.right) < 1 && Math.Abs(p.forward) < 1);
            var plan = MultiSpawnLayout.Plan(3, Abreast, 20, 30, 3, 10, free);
            Assert.Equal(MultiSpawnStatus.Shifted, plan.status);
            Assert.True(plan.shiftedSlots >= 1, "shiftedSlots");
            Assert.Equal(3, plan.slots.Count);
            foreach (var s in plan.slots) Assert.True(s.Length >= 1, $"slot {s} within 1 m of origin");
            AssertValidPlan(plan, free, 10);
            AssertSlot(plan.centre, 0, 0, "centre not moved");
            Assert.Near(0, plan.MovedBy);
        }

        [Test]
        public static void Plan_BlockedCentreKeepsLayoutOrderForOuterSlots()
        {
            Func<SpawnOffset, bool> free = p => !(Math.Abs(p.right) < 1 && Math.Abs(p.forward) < 1);
            var plan = MultiSpawnLayout.Plan(3, Abreast, 20, 30, 3, 10, free);
            Assert.Near(-20, plan.slots[0].right, 1e-9, "slot 0 unchanged");
            Assert.Near(20, plan.slots[2].right, 1e-9, "slot 2 unchanged");
            Assert.True(plan.slots[1].right > 0, "slot 1 shifted outward (right)");
        }

        [Test]
        public static void Plan_BlockedTrailLeaderIsShiftedBack()
        {
            Func<SpawnOffset, bool> free = p => !(Math.Abs(p.right) < 1 && Math.Abs(p.forward) < 1);
            var plan = MultiSpawnLayout.Plan(3, Trail, 20, 30, 3, 10, free);
            Assert.Equal(3, plan.slots.Count);
            AssertValidPlan(plan, free, 10);
            Assert.True(plan.status == MultiSpawnStatus.Shifted || plan.status == MultiSpawnStatus.Moved, $"status {plan.status}");
        }

        [Test]
        public static void Plan_BlockedHalfPlaneMovesOrShifts()
        {
            Func<SpawnOffset, bool> free = p => p.right >= 50;
            var plan = MultiSpawnLayout.Plan(4, Abreast, 20, 30, 4, 10, free);
            Assert.True(plan.status == MultiSpawnStatus.Moved || plan.status == MultiSpawnStatus.Shifted, $"status {plan.status}");
            Assert.Equal(4, plan.slots.Count);
            AssertValidPlan(plan, free, 10);
            if (plan.status == MultiSpawnStatus.Moved) Assert.True(plan.MovedBy > 0, "MovedBy");
        }

        [Test]
        public static void Plan_EverythingBlockedIsNoClearSpace()
        {
            int calls = 0;
            var plan = MultiSpawnLayout.Plan(5, Abreast, 20, 30, 5, 10, _ => { ++calls; return false; });
            Assert.Equal(MultiSpawnStatus.NoClearSpace, plan.status);
            Assert.Equal(0, plan.slots.Count, "slots");
            Assert.Equal(0, plan.bestFreeSlots, "bestFreeSlots");
            Assert.True(calls > 0 && calls < 10_000, $"isFree calls {calls}");
            Assert.Equal(calls, plan.checks, "plan.checks equals isFree calls");
        }

        [Test]
        public static void Plan_NoClearSpaceReportsBestCandidate()
        {
            // Only a thin strip is ever free, so some partial candidate should be reported.
            Func<SpawnOffset, bool> free = p => p.right < -35 && p.right > -45;
            var plan = MultiSpawnLayout.Plan(5, Abreast, 20, 30, 5, 10, free);
            Assert.Equal(MultiSpawnStatus.NoClearSpace, plan.status);
            Assert.True(plan.bestFreeSlots >= 1, $"bestFreeSlots {plan.bestFreeSlots}");
        }

        [Test]
        public static void Plan_IslandMovesBlockIntoDisc()
        {
            Func<SpawnOffset, bool> free = p => (p - new SpawnOffset(0, 2000)).Length <= 300;
            var plan = MultiSpawnLayout.Plan(3, Abreast, 20, 30, 3, 10, free, 100, 5000);
            Assert.Equal(MultiSpawnStatus.Moved, plan.status);
            Assert.True((plan.centre - new SpawnOffset(0, 2000)).Length <= 360, $"centre {plan.centre}");
            Assert.Equal(3, plan.slots.Count);
            foreach (var s in plan.slots) Assert.True((s - new SpawnOffset(0, 2000)).Length <= 300, $"slot {s} outside disc");
            AssertValidPlan(plan, free, 10);
            Assert.True(plan.MovedBy > 1500, $"MovedBy {plan.MovedBy}");
        }

        [Test]
        public static void Plan_IslandBeyondSearchRadiusIsNoClearSpace()
        {
            // Small block: 8 diagonals would only reach ~506 m, the minimum reach makes it 2 km; an island 6 km away is out of reach (max 5 km).
            Func<SpawnOffset, bool> free = p => (p - new SpawnOffset(0, 6000)).Length <= 300;
            var plan = MultiSpawnLayout.Plan(3, Abreast, 20, 30, 3, 10, free);
            Assert.Equal(MultiSpawnStatus.NoClearSpace, plan.status);
            Assert.Near(MultiSpawnLayout.DefaultMinSearchReach, plan.searchReach, 1e-9, "searchReach");
        }

        [Test]
        public static void Plan_LargeCountSixtyThreeRows()
        {
            int perRow = MultiSpawnLayout.PerRow(60, 100);
            Assert.Equal(21, perRow);
            var plan = MultiSpawnLayout.Plan(60, Abreast, 100, 150, perRow, 100, _ => true);
            Assert.Equal(MultiSpawnStatus.Placed, plan.status);
            Assert.Equal(60, plan.slots.Count);
            Assert.Equal(3, plan.rows);
            AssertValidPlan(plan, _ => true, 100);
        }

        [Test]
        public static void Plan_ThousandAllFreeIsFast()
        {
            int perRow = MultiSpawnLayout.PerRow(1000, 20);
            var sw = Stopwatch.StartNew();
            var plan = MultiSpawnLayout.Plan(1000, Abreast, 20, 30, perRow, 20, _ => true);
            sw.Stop();
            Assert.Equal(MultiSpawnStatus.Placed, plan.status);
            Assert.Equal(1000, plan.slots.Count);
            Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
        }

        [Test]
        public static void Plan_AllSuccessCasesSatisfyIsFreeAndSeparation()
        {
            double minSep = 15;
            var cases = new List<Func<SpawnOffset, bool>>
            {
                _ => true,
                p => !(Math.Abs(p.right) < 1 && Math.Abs(p.forward) < 1),
                p => p.right >= 50,
                p => p.forward >= 80 || p.forward <= -300,
                p => (p - new SpawnOffset(100, 100)).Length <= 250,
            };
            foreach (var dir in new[] { Abreast, Trail })
                foreach (int count in new[] { 1, 2, 5, 9 })
                    for (int c = 0; c < cases.Count; ++c)
                    {
                        var free = cases[c];
                        var plan = MultiSpawnLayout.Plan(count, dir, 20, 30, MultiSpawnLayout.PerRow(count, 20, 4), minSep, free, 20, 5000);
                        string tag = $"{dir} count={count} case={c} status={plan.status}";
                        if (plan.status == MultiSpawnStatus.NoClearSpace) { Assert.Equal(0, plan.slots.Count, tag); continue; }
                        Assert.Equal(count, plan.slots.Count, tag);
                        AssertValidPlan(plan, free, minSep);
                    }
        }

        [Test]
        public static void Plan_ZeroPerRowTreatedAsSingleRow()
        {
            var plan = MultiSpawnLayout.Plan(4, Abreast, 20, 30, 0, 10, _ => true);
            Assert.Equal(4, plan.perRow); Assert.Equal(1, plan.rows);
        }
    }
}
