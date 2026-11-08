using System;
using System.Collections.Generic;
using BDArmory.Control;
using BDArmory.VesselSpawning;

namespace OverdriveTests
{
    /// <summary>Review additions: the wrap rule, the scaled search reach, the check budget and the check cache of MultiSpawnLayout.Plan.</summary>
    public static class MultiSpawnWrapTests
    {
        const MultiSpawnLineDirection Abreast = MultiSpawnLineDirection.Abreast;
        const MultiSpawnLineDirection Trail = MultiSpawnLineDirection.Trail;

        static void AssertValid(MultiSpawnPlan plan, int count, Func<SpawnOffset, bool> free, double minSep, string tag = "")
        {
            Assert.Equal(count, plan.slots.Count, $"{tag} slots");
            Assert.Equal(count, plan.slotRows.Count, $"{tag} slotRows");
            foreach (var s in plan.slots) Assert.True(free(s), $"{tag} slot {s} not free");
            for (int i = 0; i < plan.slots.Count; ++i)
                for (int j = i + 1; j < plan.slots.Count; ++j)
                    Assert.True((plan.slots[i] - plan.slots[j]).Length >= minSep - 1e-6, $"{tag} slots {i} and {j} closer than {minSep}");
        }

        // ---- SearchRings ----
        [Test]
        public static void SearchRings_SmallBlockUsesMinimumReach()
        {
            int rings = MultiSpawnLayout.SearchRings(63.2, 8, 2000, 5000, out double step, out double reach);
            Assert.Near(2000, reach, 1e-9, "reach");
            Assert.Equal(MultiSpawnLayout.MaxRingsForMinReach, rings, "rings capped");
            Assert.Near(2000.0 / rings, step, 1e-9, "step");
        }

        [Test]
        public static void SearchRings_NoMinimumIsEightDiagonals()
        {
            int rings = MultiSpawnLayout.SearchRings(100, 8, 0, 5000, out double step, out double reach);
            Assert.Equal(8, rings); Assert.Near(100, step, 1e-9); Assert.Near(800, reach, 1e-9);
        }

        [Test]
        public static void SearchRings_LargeBlockScalesWithDiagonalUpToMax()
        {
            int rings = MultiSpawnLayout.SearchRings(400, 8, 2000, 5000, out double step, out double reach);
            Assert.Near(3200, reach, 1e-9, "8 diagonals beat the minimum"); Assert.Equal(8, rings); Assert.Near(400, step, 1e-9);
            rings = MultiSpawnLayout.SearchRings(3000, 8, 2000, 5000, out step, out reach);
            Assert.Near(5000, reach, 1e-9, "capped at max"); Assert.Equal(2, rings); Assert.Near(2500, step, 1e-9);
        }

        [Test]
        public static void SearchRings_ZeroReachMeansNoRings()
        {
            Assert.Equal(0, MultiSpawnLayout.SearchRings(50, 0, 0, 5000, out _, out double reach));
            Assert.Near(0, reach);
            Assert.Equal(0, MultiSpawnLayout.SearchRings(50, 8, 2000, 0, out _, out reach));
        }

        // ---- budget helpers ----
        [Test] public static void DefaultCheckBudget_Floor() => Assert.Equal(MultiSpawnLayout.MinCheckBudget, MultiSpawnLayout.DefaultCheckBudget(5));
        [Test] public static void DefaultCheckBudget_PerSlot() => Assert.Equal(1000 * MultiSpawnLayout.CheckBudgetPerSlot, MultiSpawnLayout.DefaultCheckBudget(1000));
        [Test] public static void DefaultCheckBudget_NonPositiveCount() => Assert.Equal(MultiSpawnLayout.MinCheckBudget, MultiSpawnLayout.DefaultCheckBudget(-3));
        [Test] public static void MaxWrappedRows_OneRowAllowsFour() => Assert.Equal(4, MultiSpawnLayout.MaxWrappedRows(6, 6));
        [Test] public static void MaxWrappedRows_ThreeRowsAllowsSix() => Assert.Equal(6, MultiSpawnLayout.MaxWrappedRows(60, 21));
        [Test] public static void MaxWrappedRows_TenRowsAllowsTwenty() => Assert.Equal(20, MultiSpawnLayout.MaxWrappedRows(100, 10));

        // ---- wrap when clear space along the line ends ----
        [Test]
        public static void Wrap_AbreastCorridorWrapsIntoSecondRow()
        {
            // A 90 m wide corridor: a 6-wide row at 20 m spacing (100 m) doesn't fit, so the line ends at the corridor walls and wraps.
            Func<SpawnOffset, bool> free = p => Math.Abs(p.right) <= 45;
            var plan = MultiSpawnLayout.Plan(6, Abreast, 20, 30, 6, 20, free);
            Assert.Equal(MultiSpawnStatus.Wrapped, plan.status);
            Assert.True(plan.wrapped, "wrapped");
            Assert.Equal(2, plan.rows, "rows used");
            AssertValid(plan, 6, free, 20);
            Assert.Near(0, plan.slots[0].right, 1e-9, "leader at the centre"); Assert.Near(0, plan.slots[0].forward, 1e-9);
            int[] expectedRows = [0, 0, 0, 0, 0, 1];
            for (int i = 0; i < 6; ++i) Assert.Equal(expectedRows[i], plan.slotRows[i], $"row of slot {i}");
            Assert.Near(-30, plan.slots[5].forward, 1e-9, "second row one row spacing behind");
            Assert.Near(0, plan.MovedBy, 1e-9, "not moved");
        }

        [Test]
        public static void Wrap_TrailWallWrapsIntoSideRow()
        {
            // A wall 55 m behind the point: the trail line ends after three craft and wraps into a row beside it.
            Func<SpawnOffset, bool> free = p => p.forward >= -55;
            var plan = MultiSpawnLayout.Plan(6, Trail, 20, 30, 6, 20, free);
            Assert.Equal(MultiSpawnStatus.Wrapped, plan.status);
            Assert.Equal(2, plan.rows);
            AssertValid(plan, 6, free, 20);
            for (int i = 0; i < 3; ++i) { Assert.Near(0, plan.slots[i].right, 1e-9, $"slot {i} right"); Assert.Near(-20 * i, plan.slots[i].forward, 1e-9, $"slot {i} forward"); }
            for (int i = 3; i < 6; ++i) { Assert.Near(30, plan.slots[i].right, 1e-9, $"slot {i} right"); Assert.Equal(1, plan.slotRows[i]); }
        }

        [Test]
        public static void Wrap_RowsNeverExceedMaxRowLength()
        {
            Func<SpawnOffset, bool> free = p => Math.Abs(p.right) <= 45;
            var plan = MultiSpawnLayout.Plan(6, Abreast, 20, 30, 6, 20, free, maxRowLength: 40);
            Assert.Equal(MultiSpawnStatus.Wrapped, plan.status);
            AssertValid(plan, 6, free, 20);
            foreach (var s in plan.slots) Assert.True(Math.Abs(s.right) <= 20 + 1e-6, $"slot {s} beyond the 40 m row");
            Assert.Equal(2, plan.rows, "3 + 3");
        }

        [Test]
        public static void Wrap_BlockedFirstRowMovesInstead()
        {
            // Nothing is free near the point, so wrapping can't help there (an empty first row is not a compact block): the block moves.
            Func<SpawnOffset, bool> free = p => p.forward <= -400;
            var plan = MultiSpawnLayout.Plan(4, Abreast, 20, 30, 4, 20, free);
            Assert.Equal(MultiSpawnStatus.Moved, plan.status);
            AssertValid(plan, 4, free, 20);
        }

        [Test]
        public static void Wrap_RingSearchFallsBackToWrappedRows()
        {
            // A 90 m corridor that starts 500 m behind the point: the 100 m wide row never fits anywhere, but wrapped rows fit inside the corridor.
            Func<SpawnOffset, bool> free = p => Math.Abs(p.right) <= 45 && p.forward <= -500;
            var plan = MultiSpawnLayout.Plan(6, Abreast, 20, 30, 6, 20, free);
            Assert.Equal(MultiSpawnStatus.Moved, plan.status);
            Assert.True(plan.wrapped, "wrapped");
            AssertValid(plan, 6, free, 20);
            Assert.True(plan.MovedBy >= 500 - 1e-6, $"moved {plan.MovedBy}");
        }

        [Test]
        public static void Wrap_TooManyRowsNeededIsNoClearSpace()
        {
            // A 10 m strip holds one craft per row: 5 craft would need 5 rows, more than MaxWrappedRows(5, 5) = 4.
            Func<SpawnOffset, bool> free = p => p.right < -35 && p.right > -45;
            var plan = MultiSpawnLayout.Plan(5, Abreast, 20, 30, 5, 10, free);
            Assert.Equal(MultiSpawnStatus.NoClearSpace, plan.status);
            Assert.Equal(0, plan.slots.Count);
            Assert.True(plan.bestFreeSlots >= 1, $"bestFreeSlots {plan.bestFreeSlots}");
        }

        [Test]
        public static void Wrap_AllFreeStillUsesTheRequestedLayout()
        {
            var plan = MultiSpawnLayout.Plan(7, Abreast, 10, 50, 3, 10, _ => true);
            Assert.Equal(MultiSpawnStatus.Placed, plan.status);
            Assert.False(plan.wrapped, "not wrapped");
            Assert.Equal(3, plan.rows);
        }

        [Test]
        public static void Wrap_PropertyEverySuccessIsValidAndCompact()
        {
            double minSep = 15;
            var cases = new List<Func<SpawnOffset, bool>>
            {
                p => Math.Abs(p.right) <= 45,
                p => p.forward >= -55,
                p => Math.Abs(p.right) <= 70 && p.forward >= -100,
                p => !(p.right > 15 && p.right < 60) && p.forward > -200,
                p => (p - new SpawnOffset(0, -300)).Length <= 120,
                p => Math.Abs(p.right) <= 45 && p.forward <= -500,
            };
            foreach (var dir in new[] { Abreast, Trail })
                foreach (int count in new[] { 2, 5, 9, 20 })
                    for (int c = 0; c < cases.Count; ++c)
                    {
                        var free = cases[c];
                        int perRow = MultiSpawnLayout.PerRow(count, 20, 0, 200);
                        var plan = MultiSpawnLayout.Plan(count, dir, 20, 30, perRow, minSep, free, maxRowLength: 200);
                        string tag = $"{dir} count={count} case={c} status={plan.status}";
                        if (plan.status == MultiSpawnStatus.NoClearSpace) { Assert.Equal(0, plan.slots.Count, tag); continue; }
                        AssertValid(plan, count, free, minSep, tag);
                        if (plan.wrapped) Assert.True(plan.rows <= MultiSpawnLayout.MaxWrappedRows(count, perRow), $"{tag} rows {plan.rows}");
                        for (int i = 0; i < count; ++i) Assert.True(plan.slotRows[i] >= 0 && plan.slotRows[i] < Math.Max(plan.rows, 1), $"{tag} slot {i} row {plan.slotRows[i]}");
                    }
        }

        // ---- search reach ----
        [Test]
        public static void Reach_SmallBlockFindsIslandTwoKilometresAway()
        {
            // Before the review the default search of a 3-craft block reached ~506 m and missed this island.
            Func<SpawnOffset, bool> free = p => (p - new SpawnOffset(0, 2000)).Length <= 300;
            var plan = MultiSpawnLayout.Plan(3, Abreast, 20, 30, 3, 10, free);
            Assert.Equal(MultiSpawnStatus.Moved, plan.status);
            AssertValid(plan, 3, free, 10);
            Assert.True(plan.MovedBy > 1500, $"MovedBy {plan.MovedBy}");
        }

        [Test]
        public static void Reach_ZeroMinimumKeepsEightDiagonals()
        {
            Func<SpawnOffset, bool> free = p => (p - new SpawnOffset(0, 2000)).Length <= 300;
            var plan = MultiSpawnLayout.Plan(3, Abreast, 20, 30, 3, 10, free, minSearchReach: 0);
            Assert.Equal(MultiSpawnStatus.NoClearSpace, plan.status);
            Assert.True(plan.searchReach < 600, $"searchReach {plan.searchReach}");
        }

        [Test]
        public static void Reach_LargeBlockSearchesToMaxRadius()
        {
            int perRow = MultiSpawnLayout.PerRow(60, 100);
            var plan = MultiSpawnLayout.Plan(60, Abreast, 100, 150, perRow, 100, _ => true);
            Assert.Near(MultiSpawnLayout.DefaultMaxSearchRadius, plan.searchReach, 1e-9, "8 diagonals of a 2 km block exceed 5 km");
        }

        // ---- budget and cache ----
        [Test]
        public static void Budget_StopsTheSearch()
        {
            int calls = 0;
            var plan = MultiSpawnLayout.Plan(5, Abreast, 20, 30, 5, 10, _ => { ++calls; return false; }, maxChecks: 100);
            Assert.Equal(MultiSpawnStatus.NoClearSpace, plan.status);
            Assert.True(plan.checkBudgetExhausted, "checkBudgetExhausted");
            Assert.Equal(100, calls, "isFree calls");
            Assert.Equal(100, plan.checks, "checks");
        }

        [Test]
        public static void Budget_DefaultCoversAFullyBlockedSearch()
        {
            int calls = 0;
            var plan = MultiSpawnLayout.Plan(5, Abreast, 20, 30, 5, 10, _ => { ++calls; return false; });
            Assert.Equal(MultiSpawnStatus.NoClearSpace, plan.status);
            Assert.False(plan.checkBudgetExhausted, $"budget exhausted after {calls} calls");
            Assert.True(calls <= MultiSpawnLayout.DefaultCheckBudget(5), $"calls {calls}");
        }

        [Test]
        public static void Cache_NoPositionIsCheckedTwice()
        {
            var seen = new HashSet<(long, long)>();
            int repeats = 0;
            Func<SpawnOffset, bool> free = p =>
            {
                if (!seen.Add(((long)Math.Round(p.right * 100), (long)Math.Round(p.forward * 100)))) ++repeats;
                return Math.Abs(p.right) <= 45 && p.forward <= -500;
            };
            var plan = MultiSpawnLayout.Plan(6, Abreast, 20, 30, 6, 20, free);
            Assert.Equal(MultiSpawnStatus.Moved, plan.status);
            Assert.Equal(0, repeats, "repeated isFree calls");
        }

        // ---- F1 enum ----
        [Test]
        public static void OverrideLostReason_NotHostileAppended()
        {
            // New values go at the end so the existing ones keep their numbers.
            Assert.Equal(6, (int)OverrideLostReason.Filtered);
            Assert.Equal(7, (int)OverrideLostReason.NotHostile);
        }

        [Test]
        public static void MultiSpawnStatus_WrappedAppended()
        {
            Assert.Equal(4, (int)MultiSpawnStatus.InvalidCount);
            Assert.Equal(5, (int)MultiSpawnStatus.Wrapped);
        }
    }
}
