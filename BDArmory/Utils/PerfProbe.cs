using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BDArmory.Utils
{
    /// <summary>
    /// [Overdrive perf] The BDArmory code sections timed by the perf probe.
    /// Top-level sections are entry points called by Unity/KSP (FixedUpdate, Update, OnGUI, fly-by-wire, TimingManager callbacks); their time is summed (without double counting nested entry points) into the BDArmory total.
    /// Nested sections are hot functions called from the entry points; they are reported separately and are already included in their callers' time.
    /// Keep the order of this enum and PerfSections.Names/TopLevel in sync (append new sections at the end so CSV columns stay stable).
    /// </summary>
    public enum PerfSection
    {
        // Top-level (entry points).
        WeaponManagerFixed,     // MissileFire.OnFixedUpdate (guard mode, view scan, target selection, firing).
        WeaponManagerUpdate,    // MissileFire.OnUpdate (targeting audio, aimers, manual firing).
        WeaponManagerPD,        // MissileFire.PointDefence (TimingManager Earlyish).
        WeaponManagerGUI,       // MissileFire.OnGUI.
        MissileFixed,           // MissileLauncher.OnFixedUpdate (thrust, guidance, fuzes) for every missile part, fired or not.
        MissileGUI,             // MissileLauncher.OnGUI (body only; Unity's per-call OnGUI overhead isn't included).
        ModularMissileFixed,    // BDModularGuidance.OnFixedUpdate.
        PilotAIFlyByWire,       // BDModulePilotAI.AutoPilot (vessel.OnFlyByWire).
        PilotAIFixed,           // BDModulePilotAI.FixedUpdate.
        RadarFixed,             // ModuleRadar.FixedUpdate.
        RadarUpdate,            // ModuleRadar.Update + LateUpdate.
        VesselRadarData,        // VesselRadarData.Update + LateUpdate + FixedUpdate.
        RWRFixed,               // RadarWarningReceiver.OnFixedUpdate.
        IRSTFixed,              // ModuleIRST.FixedUpdate.
        WeaponFixed,            // ModuleWeapon.FixedUpdate.
        WeaponUpdate,           // ModuleWeapon.Update.
        Bullets,                // PooledBulletManager.FixedUpdate (all bullets' movement and collision checks).
        Rockets,                // PooledRocket.FixedUpdate.
        Explosions,             // ExplosionFX.Update + FixedUpdate.
        HitpointsFixed,         // HitpointTracker.FixedUpdate (every part).
        Competition,            // BDACompetitionMode.FixedUpdate.
        SetupUpdate,            // BDArmorySetup.Update.
        SetupGUI,               // BDArmorySetup.OnGUI (the BDArmory windows).
        TargetManager,          // BDATargetManager.Update.

        // Nested (included in the sections above).
        GuardScan,              // RadarUtils.GuardScanInDirection (per weapon manager per physics frame).
        TargetSelection,        // MissileFire.SmartFindTarget.
        HeatTarget,             // BDATargetManager.GetHeatTarget (heat seekers, guard mode heat locks).
        RadarMissileLock,       // RadarUtils.RadarUpdateMissileLock (active radar missile seekers).
        RadarScanLock,          // RadarUtils.RadarUpdateScanLock (radar scans and lock attempts).
        CanSeeTarget,           // MissileFire.CanSeeTarget.
    }

    /// <summary>
    /// [Overdrive perf] Metadata for PerfSection.
    /// </summary>
    public static class PerfSections
    {
        public static readonly int Count = Enum.GetValues(typeof(PerfSection)).Length;

        /// <summary>CSV column stems (snake case).</summary>
        public static readonly string[] Names =
        [
            "wm_fixed", "wm_update", "wm_pd", "wm_gui",
            "missile_fixed", "missile_gui", "mmg_fixed",
            "ai_fbw", "ai_fixed",
            "radar_fixed", "radar_update", "vrd", "rwr_fixed", "irst_fixed",
            "weapon_fixed", "weapon_update", "bullets", "rockets", "explosions", "hp_fixed",
            "competition", "setup_update", "setup_gui", "target_manager",
            "guard_scan", "target_selection", "heat_target", "radar_missile_lock", "radar_scan_lock", "can_see_target",
        ];

        /// <summary>Whether each section is a top-level entry point (counted in the BDArmory total).</summary>
        public static readonly bool[] TopLevel = BuildTopLevel();

        static bool[] BuildTopLevel()
        {
            var topLevel = new bool[Count];
            for (int i = 0; i < topLevel.Length; ++i) topLevel[i] = i <= (int)PerfSection.TargetManager;
            return topLevel;
        }
    }

    /// <summary>
    /// [Overdrive perf] Per-frame accumulator of section times and call counts (Stopwatch ticks). Main thread only.
    /// Inclusive time is accumulated per section. Top-level sections also add to the BDArmory total, but only for the outermost top-level section,
    /// so that an entry point called from inside another (e.g., a callback fired from BDArmory code) isn't counted twice.
    /// </summary>
    public sealed class PerfAccumulator
    {
        readonly long[] ticks = new long[PerfSections.Count];
        readonly int[] calls = new int[PerfSections.Count];
        int topDepth = 0;
        long topStart = 0;
        long bdaTicks = 0;

        public long BdaTicks => bdaTicks;
        public long SectionTicks(PerfSection section) => ticks[(int)section];
        public int SectionCalls(PerfSection section) => calls[(int)section];
        public int TopDepth => topDepth;

        /// <summary>Start timing a section at the given timestamp. Returns the timestamp to pass to End.</summary>
        public long Begin(PerfSection section, long now)
        {
            if (PerfSections.TopLevel[(int)section] && topDepth++ == 0) topStart = now;
            return now;
        }

        /// <summary>Stop timing a section started at 'start'.</summary>
        public void End(PerfSection section, long start, long now)
        {
            int index = (int)section;
            ticks[index] += now - start;
            ++calls[index];
            if (PerfSections.TopLevel[index] && topDepth > 0 && --topDepth == 0) bdaTicks += now - topStart;
        }

        /// <summary>
        /// Clear the accumulated values for the next frame. A top-level section that is still open (the frame boundary is inside it, or an exception skipped its End)
        /// is restarted at 'now' so that its remaining time goes to the next frame.
        /// </summary>
        public void Reset(long now)
        {
            Array.Clear(ticks, 0, ticks.Length);
            Array.Clear(calls, 0, calls.Length);
            bdaTicks = 0;
            if (topDepth > 0) topStart = now;
        }

        /// <summary>Forget any open sections (e.g., when recording stops).</summary>
        public void ResetDepth() => topDepth = 0;
    }

    /// <summary>
    /// [Overdrive perf] Timing scope for 'using var _ = PerfProbe.Scope(section);'. Does nothing when the probe is off.
    /// </summary>
    public readonly struct PerfScope : IDisposable
    {
        readonly long start;
        readonly PerfSection section;

        internal PerfScope(PerfSection section, long start)
        {
            this.section = section;
            this.start = start;
        }

        public void Dispose()
        {
            if (start != 0) PerfProbe.End(section, start);
        }
    }

    /// <summary>
    /// [Overdrive perf] Static entry points for timing BDArmory code. Off by default (BDArmorySettings.PERF_PROBE); when off, Scope is a static bool check.
    /// PerfProbeRecorder turns it on in the flight scene, closes a row per frame and writes the CSV.
    /// </summary>
    public static class PerfProbe
    {
        /// <summary>Whether timing is active. Only PerfProbeRecorder (and tests) should set this.</summary>
        public static bool Enabled = false;
        public static readonly PerfAccumulator Accumulator = new();

        public static long Now() => Stopwatch.GetTimestamp();
        public static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        public static PerfScope Scope(PerfSection section)
        {
            if (!Enabled) return default;
            long now = Stopwatch.GetTimestamp();
            if (now == 0) now = 1; // 0 means "not timing" in PerfScope.
            return new PerfScope(section, Accumulator.Begin(section, now));
        }

        public static void End(PerfSection section, long start)
        {
            if (!Enabled) return; // Recording stopped inside the section.
            Accumulator.End(section, start, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>
    /// [Overdrive perf] One frame of perf probe data (everything except the per-section values, which come from the accumulator).
    /// "Frame" is the interval between two consecutive early Update callbacks, so it contains one frame's Update/LateUpdate/rendering and the next frame's physics steps.
    /// </summary>
    public struct PerfFrameStats
    {
        public long frameIndex;
        public double gameTime;          // Time.time at the end of the frame.
        public double realTime;          // Seconds since recording started.
        public double frameMs;           // Measured between early Update callbacks.
        public double unscaledDeltaMs;   // Time.unscaledDeltaTime (Unity's own frame time) for comparison.
        public int fixedSteps;           // Physics steps (FixedUpdate passes) in the frame.
        public double fixedScriptsMs;    // Earliest to latest FixedUpdate callback, summed over the steps (FixedUpdate scripts, KSP and mods).
        public double physicsMs;         // Latest FixedUpdate callback to WaitForFixedUpdate, summed over the steps (PhysX simulation, collision callbacks, some coroutines).
        public double updateMs;          // Earliest Update callback to latest LateUpdate callback (Update/LateUpdate scripts and coroutines).
        public int gcCount;              // Garbage collections during the frame.
        public double heapMb;            // Managed heap in use.
        public int vesselsLoaded;
        public int partsTotal;           // Parts of all loaded vessels.
        public int partsMaxVessel;       // Parts of the largest loaded vessel.
        public int missilesInFlight;
        public int bulletsActive;
        public int guardModeWMs;
        public bool competitionActive;
        public double probeMs;           // Time the probe spent collecting and writing the previous row.
    }

    /// <summary>
    /// [Overdrive perf] CSV formatting for the perf probe (invariant culture, fixed column order).
    /// </summary>
    public static class PerfCsv
    {
        public static readonly string[] FrameColumns =
        [
            "frame", "game_time", "real_time", "frame_ms", "unscaled_dt_ms", "fixed_steps", "fixed_scripts_ms", "physics_ms", "update_ms", "other_ms",
            "bda_ms", "non_bda_ms", "gc_count", "heap_mb", "vessels_loaded", "parts_total", "parts_max_vessel", "missiles_in_flight", "bullets_active", "guard_wms", "competition_active", "probe_ms",
        ];

        public static string Header()
        {
            var sb = new StringBuilder(1024);
            sb.Append(string.Join(",", FrameColumns));
            for (int i = 0; i < PerfSections.Count; ++i) sb.Append(',').Append(PerfSections.Names[i]).Append("_ms,").Append(PerfSections.Names[i]).Append("_n");
            return sb.ToString();
        }

        /// <summary>
        /// The time not accounted for by the measured phases: rendering, OnGUI, Unity overhead, GC pauses, waiting for vsync. Never negative.
        /// </summary>
        public static double OtherMs(in PerfFrameStats stats) => Math.Max(0.0, stats.frameMs - stats.fixedScriptsMs - stats.physicsMs - stats.updateMs);

        public static double NonBdaMs(in PerfFrameStats stats, double bdaMs) => Math.Max(0.0, stats.frameMs - bdaMs);

        /// <summary>Append one row (without a newline) for the frame stats and the accumulated section values.</summary>
        public static void AppendRow(StringBuilder sb, in PerfFrameStats stats, PerfAccumulator accumulator, double ticksToMs)
        {
            var c = CultureInfo.InvariantCulture;
            double bdaMs = accumulator.BdaTicks * ticksToMs;
            sb.Append(stats.frameIndex.ToString(c)).Append(',');
            sb.Append(stats.gameTime.ToString("0.000", c)).Append(',');
            sb.Append(stats.realTime.ToString("0.000", c)).Append(',');
            sb.Append(stats.frameMs.ToString("0.000", c)).Append(',');
            sb.Append(stats.unscaledDeltaMs.ToString("0.000", c)).Append(',');
            sb.Append(stats.fixedSteps.ToString(c)).Append(',');
            sb.Append(stats.fixedScriptsMs.ToString("0.000", c)).Append(',');
            sb.Append(stats.physicsMs.ToString("0.000", c)).Append(',');
            sb.Append(stats.updateMs.ToString("0.000", c)).Append(',');
            sb.Append(OtherMs(stats).ToString("0.000", c)).Append(',');
            sb.Append(bdaMs.ToString("0.000", c)).Append(',');
            sb.Append(NonBdaMs(stats, bdaMs).ToString("0.000", c)).Append(',');
            sb.Append(stats.gcCount.ToString(c)).Append(',');
            sb.Append(stats.heapMb.ToString("0.0", c)).Append(',');
            sb.Append(stats.vesselsLoaded.ToString(c)).Append(',');
            sb.Append(stats.partsTotal.ToString(c)).Append(',');
            sb.Append(stats.partsMaxVessel.ToString(c)).Append(',');
            sb.Append(stats.missilesInFlight.ToString(c)).Append(',');
            sb.Append(stats.bulletsActive.ToString(c)).Append(',');
            sb.Append(stats.guardModeWMs.ToString(c)).Append(',');
            sb.Append(stats.competitionActive ? '1' : '0').Append(',');
            sb.Append(stats.probeMs.ToString("0.000", c));
            for (int i = 0; i < PerfSections.Count; ++i)
            {
                var section = (PerfSection)i;
                sb.Append(',').Append((accumulator.SectionTicks(section) * ticksToMs).ToString("0.000", c));
                sb.Append(',').Append(accumulator.SectionCalls(section).ToString(c));
            }
        }
    }
}
