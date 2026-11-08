using BDArmory.Control;

namespace OverdriveTests
{
    public static class FireAuthorityTests
    {
        static readonly bool[] Bools = [false, true];
        static readonly FireAuthority[] Modes = [FireAuthority.Free, FireAuthority.Tight, FireAuthority.Hold];

        // ---- enums ----
        [Test] public static void Enum_DefaultFireAuthorityIsFree() => Assert.Equal(FireAuthority.Free, default(FireAuthority));
        [Test] public static void Enum_DefaultOverrideLostReasonIsNone() => Assert.Equal(OverrideLostReason.None, default(OverrideLostReason));

        // ---- PermitsFire ----
        [Test]
        public static void PermitsFire_FreeAlwaysTrue()
        {
            foreach (var m in Bools) foreach (var a in Bools) foreach (var h in Bools) foreach (var p in Bools)
                Assert.True(FireAuthorityRules.PermitsFire(FireAuthority.Free, m, a, h, p), $"Free m={m} a={a} h={h} p={p}");
        }

        [Test]
        public static void PermitsFire_HoldAlwaysFalse()
        {
            foreach (var m in Bools) foreach (var a in Bools) foreach (var h in Bools) foreach (var p in Bools)
                Assert.False(FireAuthorityRules.PermitsFire(FireAuthority.Hold, m, a, h, p), $"Hold m={m} a={a} h={h} p={p}");
        }

        [Test] public static void PermitsFire_HoldBlocksMissiles() => Assert.False(FireAuthorityRules.PermitsFire(FireAuthority.Hold, true, true, true, true));

        [Test]
        public static void PermitsFire_TightTruthTable()
        {
            int combos = 0;
            foreach (var m in Bools) foreach (var a in Bools) foreach (var h in Bools) foreach (var p in Bools)
            {
                bool expected = m || (h ? p : a);
                Assert.Equal(expected, FireAuthorityRules.PermitsFire(FireAuthority.Tight, m, a, h, p), $"Tight missile={m} assigned={a} hasFilter={h} passes={p}");
                ++combos;
            }
            Assert.Equal(16, combos);
        }

        [Test]
        public static void PermitsFire_TightMissileIgnoresFilterRejection()
            => Assert.True(FireAuthorityRules.PermitsFire(FireAuthority.Tight, true, false, true, false));

        [Test]
        public static void PermitsFire_TightFilterRejectionBeatsAssignment()
            => Assert.False(FireAuthorityRules.PermitsFire(FireAuthority.Tight, false, true, true, false));

        [Test]
        public static void PermitsFire_TightFilterAcceptWithoutAssignment()
            => Assert.True(FireAuthorityRules.PermitsFire(FireAuthority.Tight, false, false, true, true));

        [Test]
        public static void PermitsFire_TightNoFilterNeedsAssignment()
        {
            Assert.True(FireAuthorityRules.PermitsFire(FireAuthority.Tight, false, true, false, false));
            Assert.False(FireAuthorityRules.PermitsFire(FireAuthority.Tight, false, false, false, true));
        }

        // ---- ExcludedFromSelection ----
        [Test]
        public static void Excluded_MissilesNeverExcluded()
        {
            foreach (var mode in Modes) foreach (var a in Bools) foreach (var h in Bools) foreach (var p in Bools)
                Assert.False(FireAuthorityRules.ExcludedFromSelection(mode, true, a, h, p), $"missile mode={mode} a={a} h={h} p={p}");
        }

        [Test]
        public static void Excluded_FilterSetExcludedIffRejected_AllModes()
        {
            foreach (var mode in Modes) foreach (var a in Bools) foreach (var p in Bools)
                Assert.Equal(!p, FireAuthorityRules.ExcludedFromSelection(mode, false, a, true, p), $"mode={mode} assigned={a} passes={p}");
        }

        [Test] public static void Excluded_FreeFilterRejects() => Assert.True(FireAuthorityRules.ExcludedFromSelection(FireAuthority.Free, false, false, true, false));
        [Test] public static void Excluded_HoldFilterRejects() => Assert.True(FireAuthorityRules.ExcludedFromSelection(FireAuthority.Hold, false, true, true, false));
        [Test] public static void Excluded_FilterRejectsBeatsAssignment() => Assert.True(FireAuthorityRules.ExcludedFromSelection(FireAuthority.Tight, false, true, true, false));
        [Test] public static void Excluded_FilterAcceptsTightUnassignedNotExcluded() => Assert.False(FireAuthorityRules.ExcludedFromSelection(FireAuthority.Tight, false, false, true, true));

        [Test]
        public static void Excluded_NoFilterTightExcludesOnlyUnassigned()
        {
            foreach (var p in Bools)
            {
                Assert.True(FireAuthorityRules.ExcludedFromSelection(FireAuthority.Tight, false, false, false, p), "tight unassigned");
                Assert.False(FireAuthorityRules.ExcludedFromSelection(FireAuthority.Tight, false, true, false, p), "tight assigned");
            }
        }

        [Test]
        public static void Excluded_NoFilterFreeAndHoldNeverExcluded()
        {
            foreach (var mode in new[] { FireAuthority.Free, FireAuthority.Hold }) foreach (var a in Bools) foreach (var p in Bools)
                Assert.False(FireAuthorityRules.ExcludedFromSelection(mode, false, a, false, p), $"mode={mode} a={a} p={p}");
        }

        // ---- EvaluateOverrideScan ----
        static OverrideScanOutcome Scan(bool external, bool sticky, bool filteredOut, bool engageable, out OverrideLostReason reason)
            => FireAuthorityRules.EvaluateOverrideScan(external, sticky, filteredOut, engageable, out reason);

        [Test]
        public static void Scan_ExternalFilteredDropsEvenIfEngageable()
        {
            foreach (var sticky in Bools) foreach (var eng in Bools)
            {
                var o = Scan(true, sticky, true, eng, out var r);
                Assert.Equal(OverrideScanOutcome.Drop, o, $"sticky={sticky} eng={eng}");
                Assert.Equal(OverrideLostReason.Filtered, r, $"sticky={sticky} eng={eng}");
            }
        }

        [Test]
        public static void Scan_EngageableNotFilteredEngages()
        {
            foreach (var external in Bools) foreach (var sticky in Bools)
            {
                var o = Scan(external, sticky, false, true, out var r);
                Assert.Equal(OverrideScanOutcome.Engage, o, $"external={external} sticky={sticky}");
                Assert.Equal(OverrideLostReason.None, r, $"external={external} sticky={sticky}");
            }
        }

        [Test]
        public static void Scan_ExternalStickyNotEngageableHolds()
        {
            var o = Scan(true, true, false, false, out var r);
            Assert.Equal(OverrideScanOutcome.HoldNotEngageable, o);
            Assert.Equal(OverrideLostReason.None, r);
        }

        [Test]
        public static void Scan_ExternalNonStickyNotEngageableDropsNotEngageable()
        {
            var o = Scan(true, false, false, false, out var r);
            Assert.Equal(OverrideScanOutcome.Drop, o);
            Assert.Equal(OverrideLostReason.NotEngageable, r);
        }

        [Test]
        public static void Scan_NonExternalNotEngageableDropsWithNoReason()
        {
            foreach (var sticky in Bools)
            {
                var o = Scan(false, sticky, false, false, out var r);
                Assert.Equal(OverrideScanOutcome.Drop, o, $"sticky={sticky}");
                Assert.Equal(OverrideLostReason.None, r, $"sticky={sticky}");
            }
        }

        [Test]
        public static void Scan_NonExternalIgnoresFilteredOut()
        {
            foreach (var sticky in Bools)
            {
                var o = Scan(false, sticky, true, true, out var r);
                Assert.Equal(OverrideScanOutcome.Engage, o);
                Assert.Equal(OverrideLostReason.None, r);
                o = Scan(false, sticky, true, false, out r);
                Assert.Equal(OverrideScanOutcome.Drop, o);
                Assert.Equal(OverrideLostReason.None, r);
            }
        }

        [Test]
        public static void Scan_FullTruthTable()
        {
            foreach (var ext in Bools) foreach (var st in Bools) foreach (var fo in Bools) foreach (var eng in Bools)
            {
                OverrideScanOutcome eo; OverrideLostReason er = OverrideLostReason.None;
                if (ext && fo) { eo = OverrideScanOutcome.Drop; er = OverrideLostReason.Filtered; }
                else if (eng) eo = OverrideScanOutcome.Engage;
                else if (ext && st) eo = OverrideScanOutcome.HoldNotEngageable;
                else if (ext) { eo = OverrideScanOutcome.Drop; er = OverrideLostReason.NotEngageable; }
                else eo = OverrideScanOutcome.Drop;
                var o = Scan(ext, st, fo, eng, out var r);
                string tag = $"ext={ext} sticky={st} filtered={fo} eng={eng}";
                Assert.Equal(eo, o, tag);
                Assert.Equal(er, r, tag);
            }
        }

        // ---- ExternalOverrideTimer ----
        [Test] public static void Timer_PositiveUnchanged() { Assert.Equal(5f, FireAuthorityRules.ExternalOverrideTimer(5f)); Assert.Equal(0.001f, FireAuthorityRules.ExternalOverrideTimer(0.001f)); }
        [Test] public static void Timer_ZeroIsInfinity() => Assert.True(float.IsPositiveInfinity(FireAuthorityRules.ExternalOverrideTimer(0f)));
        [Test] public static void Timer_NegativeIsInfinity() => Assert.True(float.IsPositiveInfinity(FireAuthorityRules.ExternalOverrideTimer(-3f)));
        [Test] public static void Timer_NaNIsInfinity() => Assert.True(float.IsPositiveInfinity(FireAuthorityRules.ExternalOverrideTimer(float.NaN)));
        [Test] public static void Timer_NegativeInfinityIsInfinity() => Assert.True(float.IsPositiveInfinity(FireAuthorityRules.ExternalOverrideTimer(float.NegativeInfinity)));
        [Test] public static void Timer_PositiveInfinityStaysInfinity() => Assert.True(float.IsPositiveInfinity(FireAuthorityRules.ExternalOverrideTimer(float.PositiveInfinity)));
    }
}
