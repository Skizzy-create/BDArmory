using System;
using System.Collections.Generic;
using BDArmory.Control;

namespace OverdriveTests
{
    /// <summary>
    /// Layered defence for guard mode point defence (GUARD_LAYERED_DEFENCE): most urgent threat first, longest-range interceptor whose envelope fits first,
    /// fall-through to the next type, shoot-look-shoot re-engagement and optional shoot-shoot.
    /// The point defence scan is modelled with the same pure functions MissileFire.PointDefenseTurretFiring uses.
    /// </summary>
    public static class LayeredDefenceTests
    {
        sealed class Interceptor(string type, float min, float max, bool lockOk = true)
        {
            public readonly string Type = type;
            public readonly float Min = min, Max = max;
            public bool LockOk = lockOk;
            public bool Fired;
            public int Key;
        }

        sealed class Threat(float distance, float closing)
        {
            public float Distance = distance, Closing = closing;
            public int Away; // Interceptors in flight at it.
            public float TimeToImpact => LayeredDefence.TimeToImpact(Distance, Closing);
        }

        sealed class PointDefence
        {
            public readonly List<Interceptor> Interceptors = []; // Sorted shortest to longest range, as pointDefenseMissileArray.
            public readonly List<Threat> Threats = [];
            public readonly InterceptorAttemptMemory Failures = new();
            public readonly InterceptorAttemptMemory TargetSkips = new();
            public bool Layered = true;
            public int MissilesPerTarget = 1;
            public float ShootShootTime = 0f;
            public int RotationIndex = 0;
            public float Now = 0f;

            public void Add(string type, float min, float max, int rounds, bool lockOk = true)
            {
                for (int i = 0; i < rounds; ++i) Interceptors.Add(new Interceptor(type, min, max, lockOk) { Key = Interceptors.Count + 1 });
                Interceptors.Sort((a, b) => a.Max.CompareTo(b.Max));
            }

            int Quota(Threat t) => Layered ? LayeredDefence.PerTargetQuota(MissilesPerTarget, t.TimeToImpact, ShootShootTime) : MissilesPerTarget;

            // The reach of the interceptors that can still fire (MissileFire.pdReachMin/pdReachMax, computed each scan).
            float MinReach { get { float m = float.MaxValue; foreach (var i in Interceptors) if (!i.Fired) m = Math.Min(m, i.Min); return m; } }
            float MaxReach { get { float m = -1f; foreach (var i in Interceptors) if (!i.Fired) m = Math.Max(m, i.Max); return m; } }

            bool Excluded(int i) => GuardFireRules.TargetSaturated(Threats[i].Away, Quota(Threats[i])) || TargetSkips.IsBlocked(0, i, Now) || !LayeredDefence.InReach(Threats[i].Distance, MinReach, MaxReach);

            /// <summary>Whether some threat that still needs an interceptor (and isn't skipped) has an unfired interceptor that can engage it now.</summary>
            public bool EngageableExists()
            {
                for (int t = 0; t < Threats.Count; ++t)
                {
                    if (GuardFireRules.TargetSaturated(Threats[t].Away, Quota(Threats[t])) || TargetSkips.IsBlocked(0, t, Now)) continue;
                    foreach (var i in Interceptors)
                        if (!i.Fired && i.LockOk && Threats[t].Distance >= i.Min && Threats[t].Distance <= i.Max && !Failures.IsBlocked(i.Key, t, Now)) return true;
                }
                return false;
            }

            public void Advance(float dt) { foreach (var t in Threats) t.Distance = Math.Max(0f, t.Distance - t.Closing * dt); }

            /// <summary>One point defence scan: returns (interceptor type, threat index) or (null, -1).</summary>
            public (string type, int target) Scan()
            {
                int target = Layered
                    ? LayeredDefence.MostUrgentIndex(Threats.Count, i => Threats[i].TimeToImpact, Excluded)
                    : GuardFireRules.NextUnsaturatedIndex(Threats.Count, RotationIndex, i => GuardFireRules.TargetSaturated(Threats[i].Away, MissilesPerTarget));
                if (target < 0) return (null, -1);
                var threat = Threats[target];
                string fired = null;
                for (int k = 0; k < Interceptors.Count && fired == null; ++k)
                {
                    var interceptor = Interceptors[LayeredDefence.InterceptorIndex(k, Interceptors.Count, Layered)];
                    if (interceptor.Fired) continue;
                    if (GuardFireRules.CheckInterceptor(Failures.IsBlocked(interceptor.Key, target, Now), threat.Distance, interceptor.Min) != InterceptorPrecheck.Usable) continue;
                    if (threat.Distance > interceptor.Max) continue; // Envelope (DLZ).
                    if (!interceptor.LockOk) continue; // No lock: next type, same scan.
                    interceptor.Fired = true;
                    ++threat.Away;
                    fired = interceptor.Type;
                }
                if (Layered) { if (fired == null) TargetSkips.RecordFailure(0, target, Now, LayeredDefence.TargetSkipTime); }
                else RotationIndex = target + 1;
                Now += 0.1f;
                return (fired, target);
            }

            public int Left(string type) { int n = 0; foreach (var i in Interceptors) if (i.Type == type && !i.Fired) ++n; return n; }
        }

        static PointDefence Ship(bool layered = true)
        {
            var pd = new PointDefence { Layered = layered };
            pd.Add("RAM", 500, 9000, 10);
            pd.Add("ESSM", 1500, 30000, 8);
            pd.Add("SM-2", 3000, 90000, 4);
            return pd;
        }

        [Test]
        public static void ThreatAtLongRange_LongRangeFires()
        {
            var pd = Ship();
            pd.Threats.Add(new Threat(60000, 300));
            Assert.Equal("SM-2", pd.Scan().type);
        }

        [Test]
        public static void ThreatInsideEveryEnvelope_LongRangeFiresFirst_StockFiresShortest()
        {
            // The user's report: with threats already close, stock fired only the shortest-range missiles and never used the long-range ones.
            var layered = Ship();
            layered.Threats.Add(new Threat(8000, 300));
            Assert.Equal("SM-2", layered.Scan().type);
            var stock = Ship(layered: false);
            stock.Threats.Add(new Threat(8000, 300));
            Assert.Equal("RAM", stock.Scan().type, "setting off: old behaviour");
        }

        [Test]
        public static void LongRangeEmpty_MediumFiresAtOnce()
        {
            var pd = Ship();
            foreach (var i in pd.Interceptors) if (i.Type == "SM-2") i.Fired = true;
            pd.Threats.Add(new Threat(20000, 300));
            Assert.Equal("ESSM", pd.Scan().type, "same scan");
        }

        [Test]
        public static void LongRangeNoLock_FallsThroughInTheSameScan()
        {
            var pd = Ship();
            foreach (var i in pd.Interceptors) if (i.Type == "SM-2") i.LockOk = false;
            pd.Threats.Add(new Threat(20000, 300));
            Assert.Equal("ESSM", pd.Scan().type);
        }

        [Test]
        public static void LongRangeFailsToLaunch_NextScanUsesAnotherType()
        {
            var pd = Ship();
            pd.Threats.Add(new Threat(20000, 300));
            var first = pd.Interceptors[pd.Interceptors.Count - 1]; // The SM-2 that will be picked.
            pd.Failures.RecordFailure(first.Key, 0, pd.Now, GuardFireRules.InterceptorInstanceRetryCooldown);
            foreach (var i in pd.Interceptors) if (i.Type == "SM-2") pd.Failures.RecordFailure(i.Key, 0, pd.Now, GuardFireRules.InterceptorRetryCooldown); // Type failure (separate memory in game).
            Assert.Equal("ESSM", pd.Scan().type);
        }

        [Test]
        public static void Miss_RefiresOnTheNextScan()
        {
            var pd = Ship();
            var threat = new Threat(20000, 300);
            pd.Threats.Add(threat);
            Assert.Equal("SM-2", pd.Scan().type);
            Assert.Equal(null, pd.Scan().type, "shoot-look-shoot: one in flight (Missiles/Tgt 1)");
            pd.Now += LayeredDefence.TargetSkipTime; // (The skipped target comes back.)
            Assert.Equal(null, pd.Scan().type, "still in flight");
            threat.Away -= GuardFireRules.CountsAsMissileAway(true, true, true, false, true, false) ? 0 : 1; // It misses (or loses its seeker, or explodes).
            pd.Now += LayeredDefence.TargetSkipTime;
            Assert.Equal("SM-2", pd.Scan().type, "re-engaged at once");
        }

        [Test]
        public static void MostUrgentThreatFirst_NotNearestFirst()
        {
            var pd = Ship();
            pd.Threats.Add(new Threat(12000, 200)); // 60s
            pd.Threats.Add(new Threat(20000, 900)); // 22s: farther but faster.
            pd.Threats.Add(new Threat(15000, -50)); // Opening.
            Assert.Equal(1, pd.Scan().target);
            Assert.Equal(0, pd.Scan().target);
            Assert.Equal(2, pd.Scan().target, "missiles that aren't closing go last");
        }

        [Test]
        public static void ShootShoot_TwoAtOnceInsideTheThreshold()
        {
            var pd = Ship();
            pd.ShootShootTime = 15f;
            var near = new Threat(9000, 900); // 10s.
            pd.Threats.Add(near);
            Assert.True(pd.Scan().type != null);
            Assert.True(pd.Scan().type != null, "second interceptor at once");
            Assert.Equal(null, pd.Scan().type, "two is enough");
            var off = Ship();
            off.Threats.Add(new Threat(9000, 900));
            Assert.True(off.Scan().type != null);
            Assert.Equal(null, off.Scan().type, "shoot-shoot off by default");
        }

        [Test]
        public static void ThreatOutOfReach_SkippedInTheSameScan()
        {
            var pd = Ship();
            pd.Threats.Add(new Threat(300, 900)); // Most urgent, but inside every minimum range.
            pd.Threats.Add(new Threat(20000, 300));
            Assert.Equal("SM-2", pd.Scan().type, "the next most urgent is served at once");
            foreach (var i in pd.Interceptors) if (i.Type == "SM-2") i.Fired = true;
            var far = Ship();
            foreach (var i in far.Interceptors) if (i.Type == "SM-2") i.Fired = true;
            far.Threats.Add(new Threat(50000, 1000)); // Most urgent, beyond the interceptors left (ESSM 30 km).
            far.Threats.Add(new Threat(20000, 300));
            Assert.Equal(1, far.Scan().target);
        }

        [Test]
        public static void UnengageableThreatInReach_CostsOneScanAtMost()
        {
            var pd = Ship();
            pd.Threats.Add(new Threat(8000, 900)); // Most urgent, in reach, but every interceptor just failed to launch at it.
            pd.Threats.Add(new Threat(20000, 300));
            foreach (var i in pd.Interceptors) pd.Failures.RecordFailure(i.Key, 0, pd.Now, 5f);
            Assert.Equal((string)null, pd.Scan().type);
            Assert.Equal("SM-2", pd.Scan().type, "skipped for a second: the next most urgent gets the next scan");
        }

        [Test]
        public static void HundredIncomingMixedInventory_NoStall()
        {
            var pd = new PointDefence();
            pd.Add("RAM", 500, 9000, 40);
            pd.Add("ESSM", 1500, 30000, 40);
            pd.Add("SM-2", 3000, 90000, 30);
            var rng = new Random(1234);
            for (int i = 0; i < 100; ++i) pd.Threats.Add(new Threat(2000 + (float)rng.NextDouble() * 58000, 250 + (float)rng.NextDouble() * 650));
            int fired = 0, idle = 0;
            for (int scan = 0; scan < 2000 && fired < 100; ++scan)
            {
                bool engageable = pd.EngageableExists();
                var (type, _) = pd.Scan();
                if (type != null) { ++fired; idle = 0; }
                else if (engageable) Assert.True(++idle <= 1, $"stalled at scan {scan} with {fired} fired"); // At most one scan lost to a threat nothing could engage.
                else idle = 0; // Nothing in reach yet: the threats are still closing.
                pd.Advance(0.1f);
            }
            Assert.Equal(100, fired, "every threat engaged");
            Assert.True(pd.Left("SM-2") == 0, "long-range rounds used first");
            int used = 30 + (40 - pd.Left("ESSM")) + (40 - pd.Left("RAM"));
            Assert.Equal(100, used);
        }

        [Test]
        public static void SettingOff_StockOrderAndRoundRobin()
        {
            var pd = Ship(layered: false);
            pd.Threats.Add(new Threat(12000, 200));
            pd.Threats.Add(new Threat(20000, 900));
            var (type, target) = pd.Scan();
            Assert.Equal(0, target, "list order (round-robin), not urgency");
            Assert.Equal("ESSM", type, "shortest range whose envelope fits (RAM can't reach 12 km)");
            Assert.Equal(1, pd.Scan().target);
        }

        [Test]
        public static void TimeToImpact_Edges()
        {
            Assert.Near(10f, LayeredDefence.TimeToImpact(5000f, 500f), 1e-4);
            Assert.True(LayeredDefence.TimeToImpact(5000f, 0f) >= LayeredDefence.NoClosingTime);
            Assert.True(LayeredDefence.TimeToImpact(5000f, -100f) > LayeredDefence.TimeToImpact(4000f, -100f), "opening: nearest first");
            Assert.True(LayeredDefence.TimeToImpact(float.NaN, 100f) > LayeredDefence.NoClosingTime);
            Assert.Near(0f, LayeredDefence.TimeToImpact(-5f, 100f), 1e-6);
        }

        [Test]
        public static void PerTargetQuota_Edges()
        {
            Assert.Equal(1, LayeredDefence.PerTargetQuota(1, 5f, 0f));
            Assert.Equal(2, LayeredDefence.PerTargetQuota(1, 5f, 10f));
            Assert.Equal(1, LayeredDefence.PerTargetQuota(1, 15f, 10f));
            Assert.Equal(4, LayeredDefence.PerTargetQuota(4, 5f, 10f));
            Assert.Equal(1, LayeredDefence.PerTargetQuota(0, 50f, 10f));
        }

        [Test]
        public static void MostUrgentIndex_Edges()
        {
            Assert.Equal(-1, LayeredDefence.MostUrgentIndex(0, _ => 1f, null));
            Assert.Equal(-1, LayeredDefence.MostUrgentIndex(3, null, null));
            Assert.Equal(-1, LayeredDefence.MostUrgentIndex(3, _ => 1f, _ => true));
            Assert.Equal(0, LayeredDefence.MostUrgentIndex(3, _ => 1f, null), "ties go to the lower index");
            Assert.Equal(2, LayeredDefence.MostUrgentIndex(3, i => i == 1 ? float.NaN : 3 - i, null));
        }

        [Test]
        public static void InterceptorIndex_Order()
        {
            Assert.Equal(4, LayeredDefence.InterceptorIndex(0, 5, true));
            Assert.Equal(0, LayeredDefence.InterceptorIndex(4, 5, true));
            Assert.Equal(0, LayeredDefence.InterceptorIndex(0, 5, false));
        }
    }
}
