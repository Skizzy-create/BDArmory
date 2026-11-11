using System.Collections.Generic;
using BDArmory.Utils;

namespace OverdriveTests
{
    public static class ScanStaggerTests
    {
        [Test]
        public static void Interval1AlwaysRuns()
        {
            var stagger = new ScanStagger();
            for (int i = 0; i < 100; ++i) Assert.True(stagger.Tick(12345u, 1), $"call {i}");
        }

        [Test]
        public static void IntervalZeroOrNegativeAlwaysRuns()
        {
            var stagger = new ScanStagger();
            for (int i = 0; i < 10; ++i) Assert.True(stagger.Tick(7u, 0));
            for (int i = 0; i < 10; ++i) Assert.True(stagger.Tick(7u, -3));
        }

        [Test]
        public static void DefaultStructRunsOnFirstCallAtInterval1()
        {
            ScanStagger stagger = default; // MissileFire's field starts as default.
            Assert.True(stagger.Tick(0u, 1));
        }

        [Test]
        public static void IntervalNRunsOncePerNCalls()
        {
            foreach (int n in new[] { 2, 3, 4, 5, 8 })
                foreach (uint id in new uint[] { 0, 1, 2, 3, 7, 1000003, uint.MaxValue })
                {
                    var stagger = new ScanStagger();
                    var dueCalls = new List<int>();
                    for (int call = 0; call < 10 * n; ++call)
                        if (stagger.Tick(id, n)) dueCalls.Add(call);
                    Assert.Equal(10, dueCalls.Count, $"n={n} id={id}");
                    for (int k = 1; k < dueCalls.Count; ++k) Assert.Equal(n, dueCalls[k] - dueCalls[k - 1], $"n={n} id={id} spacing");
                    Assert.True(dueCalls[0] < n, $"n={n} id={id}: first run within one interval");
                }
        }

        [Test]
        public static void CallersAreSpreadOverTheInterval()
        {
            const int n = 4;
            var staggers = new ScanStagger[n];
            for (int frame = 0; frame < 40; ++frame)
            {
                int due = 0;
                for (uint id = 0; id < n; ++id)
                    if (staggers[id].Tick(id, n)) ++due;
                Assert.Equal(1, due, $"frame {frame}: exactly one of {n} consecutive ids is due");
            }
        }

        [Test]
        public static void ManyCallersBalanced()
        {
            const int n = 3, callers = 300;
            var staggers = new ScanStagger[callers];
            var ids = new uint[callers];
            for (int i = 0; i < callers; ++i) ids[i] = (uint)(i * 2654435761u); // Scattered ids.
            for (int frame = 0; frame < 30; ++frame)
            {
                int due = 0;
                for (int i = 0; i < callers; ++i)
                    if (staggers[i].Tick(ids[i], n)) ++due;
                Assert.True(due > callers / n / 2 && due < 2 * callers / n, $"frame {frame}: {due} due");
            }
        }

        [Test]
        public static void ChangingIntervalRephasesAndStaysInRange()
        {
            var stagger = new ScanStagger();
            for (int i = 0; i < 7; ++i) stagger.Tick(5u, 3);
            int due = 0;
            for (int i = 0; i < 20; ++i) if (stagger.Tick(5u, 5)) ++due;
            Assert.Equal(4, due, "5 interval over 20 calls");
            for (int i = 0; i < 5; ++i) Assert.True(stagger.Tick(5u, 1), "back to every frame");
            due = 0;
            for (int i = 0; i < 6; ++i) if (stagger.Tick(5u, 2)) ++due;
            Assert.Equal(3, due);
        }
    }

    public static class GuardScanGatesTests
    {
        static readonly bool[] Bools = [false, true];

        /// <summary>
        /// The missile branch of RadarUtils.GuardScanInDirection before the change, transcribed: returns whether the missile reaches the threat evaluation,
        /// and counts the MWS display side effect (PerformMWSCheck with addTarget) and terrain raycasts.
        /// </summary>
        static bool Original(bool terrainBlocked, bool ours, bool mwsEnabled, bool mwsDetects, bool withinSight, float angle, float halfFov,
            bool rwrPresentEnabled, bool radarGuided, bool active, bool loal, bool beyond, bool rwrLock, bool hasVrd, ref int mwsSideEffects, ref int raycasts)
        {
            ++raycasts;
            if (terrainBlocked) return false;
            if (ours) return false;
            bool mwsDetected = false;
            if (mwsEnabled)
            {
                mwsDetected = mwsDetects;
                if (mwsDetected) ++mwsSideEffects;
            }
            if (!mwsDetected)
            {
                bool detected = false;
                if (withinSight)
                {
                    if (angle < halfFov) detected = withinSight;
                }
                if (!detected)
                {
                    if (!rwrPresentEnabled) return false;
                    if (!radarGuided || ((active || loal) && (beyond || !rwrLock)) || (!active && !hasVrd)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The changed flow: the pure gates (as composed in RadarUtils.GuardScanMissileCouldBeDetected) before the raycast, then the original code.
        /// </summary>
        static bool Changed(bool terrainBlocked, bool ours, bool mwsEnabled, bool mwsDetects, bool withinSight, float angle, float halfFov,
            bool rwrPresentEnabled, bool radarGuided, bool active, bool loal, bool beyond, bool rwrLock, bool hasVrd, ref int mwsSideEffects, ref int raycasts)
        {
            bool gateMws = mwsEnabled && mwsDetects;
            bool gateSight = GuardScanGates.SightDetects(withinSight, withinSight ? angle : 0f, halfFov);
            bool rwrRejects = false;
            if (rwrPresentEnabled && !gateMws && !gateSight)
            {
                bool activeOrLoal = active || loal;
                rwrRejects = GuardScanGates.RwrRejects(radarGuided, activeOrLoal, beyond, activeOrLoal && !beyond && rwrLock, active, hasVrd);
            }
            if (ours || !GuardScanGates.MissileCouldBeDetected(gateMws, gateSight, rwrPresentEnabled, rwrRejects)) return false;
            return Original(terrainBlocked, ours, mwsEnabled, mwsDetects, withinSight, angle, halfFov, rwrPresentEnabled, radarGuided, active, loal, beyond, rwrLock, hasVrd, ref mwsSideEffects, ref raycasts);
        }

        [Test]
        public static void ChangedFlowMatchesOriginalForAllGateCombinations()
        {
            int combos = 0, raycastsOriginal = 0, raycastsChanged = 0;
            float[] angles = [10f, 50f];
            foreach (var terrain in Bools) foreach (var ours in Bools) foreach (var mwsEnabled in Bools) foreach (var mws in Bools)
            foreach (var within in Bools) foreach (var angle in angles) foreach (var rwr in Bools) foreach (var radar in Bools)
            foreach (var active in Bools) foreach (var loal in Bools) foreach (var beyond in Bools) foreach (var rwrLock in Bools) foreach (var vrd in Bools)
            {
                int sideA = 0, sideB = 0;
                bool a = Original(terrain, ours, mwsEnabled, mws, within, angle, 30f, rwr, radar, active, loal, beyond, rwrLock, vrd, ref sideA, ref raycastsOriginal);
                bool b = Changed(terrain, ours, mwsEnabled, mws, within, angle, 30f, rwr, radar, active, loal, beyond, rwrLock, vrd, ref sideB, ref raycastsChanged);
                string ctx = $"terrain={terrain} ours={ours} mwsOn={mwsEnabled} mws={mws} within={within} angle={angle} rwr={rwr} radar={radar} active={active} loal={loal} beyond={beyond} lock={rwrLock} vrd={vrd}";
                Assert.Equal(a, b, ctx);
                Assert.Equal(sideA, sideB, "MWS display side effects: " + ctx);
                ++combos;
            }
            Assert.Equal(1 << 13, combos);
            Assert.True(raycastsChanged < raycastsOriginal / 2, $"raycasts {raycastsChanged} vs {raycastsOriginal}");
        }

        [Test]
        public static void SightNeedsDistanceAndCone()
        {
            Assert.True(GuardScanGates.SightDetects(true, 10f, 30f));
            Assert.False(GuardScanGates.SightDetects(true, 30f, 30f), "boundary is exclusive, as in the scan");
            Assert.False(GuardScanGates.SightDetects(false, 0f, 180f));
        }

        [Test]
        public static void RwrOnlyDetectsRadarMissiles()
        {
            foreach (var active in Bools) foreach (var loal in Bools) foreach (var beyond in Bools) foreach (var rwrLock in Bools) foreach (var vrd in Bools)
                Assert.True(GuardScanGates.RwrRejects(false, active || loal, beyond, rwrLock, active, vrd));
        }

        [Test]
        public static void RwrActiveRadarNeedsRangeAndLockWarning()
        {
            Assert.False(GuardScanGates.RwrRejects(true, true, false, true, true, false), "active, in range, lock warning");
            Assert.True(GuardScanGates.RwrRejects(true, true, true, true, true, true), "beyond twice the active range");
            Assert.True(GuardScanGates.RwrRejects(true, true, false, false, true, true), "no lock warning");
        }

        [Test]
        public static void RwrSemiActiveNeedsRadarData()
        {
            Assert.False(GuardScanGates.RwrRejects(true, false, true, false, false, true), "SARH guided by a radar: range and lock warning don't matter");
            Assert.True(GuardScanGates.RwrRejects(true, false, false, true, false, false), "SARH without a radar");
        }

        [Test]
        public static void CouldBeDetectedTruthTable()
        {
            foreach (var mws in Bools) foreach (var sight in Bools) foreach (var rwr in Bools) foreach (var rejects in Bools)
                Assert.Equal(mws || sight || (rwr && !rejects), GuardScanGates.MissileCouldBeDetected(mws, sight, rwr, rejects), $"mws={mws} sight={sight} rwr={rwr} rejects={rejects}");
        }
    }
}
