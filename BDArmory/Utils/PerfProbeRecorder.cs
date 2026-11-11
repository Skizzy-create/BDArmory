using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Utils
{
    /// <summary>
    /// [Overdrive perf] Records one CSV row per frame in the flight scene when BDArmorySettings.PERF_PROBE is enabled (off by default):
    /// frame time, physics steps, the time of the FixedUpdate scripts, the physics simulation and the Update/LateUpdate scripts, the time spent in BDArmory code
    /// (total and per section, see PerfSection), GC counts, vessel/part counts, missiles in flight and active bullets.
    /// Output: GameData/BDArmory/PluginData/PerfProbe/perf_yyyyMMdd_HHmmss.csv (see Overdrive/PERF.md for the columns).
    /// When the setting is off, this component disables itself in Start and nothing is timed.
    ///
    /// Phase timing uses KSP's TimingManager stages: ObscenelyEarly runs before and BetterLateThanNever after the regular FixedUpdate/Update/LateUpdate scripts.
    /// The physics simulation runs after the last FixedUpdate script and before WaitForFixedUpdate coroutines resume.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PerfProbeRecorder : MonoBehaviour
    {
        const int MaxRows = 500000; // ~2.5 hours at 60 fps; stop writing after that.
        const float FlushInterval = 2f; // Seconds (real time).
        const int FlushChars = 1 << 16;

        StreamWriter writer;
        readonly StringBuilder rows = new(FlushChars + 4096);
        string path;
        int rowCount = 0;
        float lastFlushRealtime = 0f;
        float startRealtime = 0f;
        bool registered = false;

        // Phase timing (Stopwatch ticks).
        long lastFrameStart = 0;
        long updateStart = 0;
        long updateTicks = 0;
        long fixedStart = 0;
        long fixedLate = 0;
        long fixedScriptsTicks = 0;
        long physicsTicks = 0;
        bool awaitingPhysics = false;
        int fixedSteps = 0;
        int lastGCCount = 0;
        double lastProbeMs = 0;

        void Start()
        {
            if (!BDArmorySettings.PERF_PROBE)
            {
                enabled = false;
                return;
            }
            try
            {
                var directory = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/BDArmory/PluginData/PerfProbe"));
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, $"perf_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                writer = new StreamWriter(path, false, new UTF8Encoding(false), FlushChars);
                writer.WriteLine(PerfCsv.Header());
            }
            catch (Exception e)
            {
                Debug.LogError($"[BDArmory.PerfProbe]: Failed to open the perf probe CSV: {e.Message}");
                writer = null;
                enabled = false;
                return;
            }

            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.ObscenelyEarly, OnFixedEarly);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, OnFixedLate);
            TimingManager.UpdateAdd(TimingManager.TimingStage.ObscenelyEarly, OnUpdateEarly);
            TimingManager.LateUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, OnLateUpdateLate);
            registered = true;
            StartCoroutine(AfterPhysicsRoutine());

            lastGCCount = GC.CollectionCount(0);
            startRealtime = Time.realtimeSinceStartup;
            lastFlushRealtime = startRealtime;
            PerfProbe.Accumulator.ResetDepth();
            PerfProbe.Accumulator.Reset(PerfProbe.Now());
            PerfProbe.Enabled = true;
            Debug.Log($"[BDArmory.PerfProbe]: Recording to {path}. BDArmory {BDArmorySetup.Version}, Unity {Application.unityVersion}, {SystemInfo.processorCount} logical processors ({SystemInfo.processorType}), GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {Screen.width}x{Screen.height}, vSync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}, fixedDeltaTime {Time.fixedDeltaTime}, maximumDeltaTime {Time.maximumDeltaTime}, shadows {QualitySettings.shadows} distance {QualitySettings.shadowDistance} cascades {QualitySettings.shadowCascades}, pixel lights {QualitySettings.pixelLightCount}, GUARD_SCAN_INTERVAL {BDArmorySettings.GUARD_SCAN_INTERVAL}.");
        }

        void OnDestroy()
        {
            PerfProbe.Enabled = false;
            PerfProbe.Accumulator.ResetDepth();
            if (registered)
            {
                TimingManager.FixedUpdateRemove(TimingManager.TimingStage.ObscenelyEarly, OnFixedEarly);
                TimingManager.FixedUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, OnFixedLate);
                TimingManager.UpdateRemove(TimingManager.TimingStage.ObscenelyEarly, OnUpdateEarly);
                TimingManager.LateUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, OnLateUpdateLate);
                registered = false;
            }
            if (writer != null)
            {
                try
                {
                    writer.Write(rows.ToString());
                    rows.Length = 0;
                    writer.Flush();
                    writer.Dispose();
                    Debug.Log($"[BDArmory.PerfProbe]: Wrote {rowCount} rows to {path}.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.PerfProbe]: Failed to write the perf probe CSV: {e.Message}");
                }
                writer = null;
            }
        }

        void OnFixedEarly()
        {
            fixedStart = PerfProbe.Now();
            ++fixedSteps;
        }

        void OnFixedLate()
        {
            long now = PerfProbe.Now();
            if (fixedStart != 0) fixedScriptsTicks += now - fixedStart;
            fixedLate = now;
            awaitingPhysics = true;
        }

        IEnumerator AfterPhysicsRoutine()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                if (awaitingPhysics)
                {
                    physicsTicks += PerfProbe.Now() - fixedLate;
                    awaitingPhysics = false;
                }
            }
        }

        void OnLateUpdateLate()
        {
            if (updateStart != 0) updateTicks = PerfProbe.Now() - updateStart;
        }

        /// <summary>
        /// The frame boundary: close the previous frame's row and start the next one.
        /// </summary>
        void OnUpdateEarly()
        {
            long now = PerfProbe.Now();
            if (lastFrameStart != 0 && writer != null && rowCount < MaxRows)
            {
                try
                {
                    WriteRow(now);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.PerfProbe]: Failed to record a frame, stopping: {e.Message}");
                    rowCount = MaxRows;
                }
                if (rowCount >= MaxRows) Debug.LogWarning($"[BDArmory.PerfProbe]: Stopped recording after {rowCount} rows.");
            }
            lastFrameStart = now;
            fixedSteps = 0;
            fixedScriptsTicks = 0;
            physicsTicks = 0;
            updateTicks = 0;
            PerfProbe.Accumulator.Reset(now);
            updateStart = PerfProbe.Now();
        }

        void WriteRow(long now)
        {
            double ticksToMs = PerfProbe.TicksToMs;
            int gcCount = GC.CollectionCount(0);
            var stats = new PerfFrameStats
            {
                frameIndex = Time.frameCount,
                gameTime = Time.time,
                realTime = Time.realtimeSinceStartup - startRealtime,
                frameMs = (now - lastFrameStart) * ticksToMs,
                unscaledDeltaMs = Time.unscaledDeltaTime * 1000.0,
                fixedSteps = fixedSteps,
                fixedScriptsMs = fixedScriptsTicks * ticksToMs,
                physicsMs = physicsTicks * ticksToMs,
                updateMs = updateTicks * ticksToMs,
                gcCount = gcCount - lastGCCount,
                heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
                bulletsActive = Bullets.PooledBulletManager.ActiveCount,
                competitionActive = BDACompetitionMode.Instance != null && BDACompetitionMode.Instance.competitionIsActive,
                probeMs = lastProbeMs,
            };
            lastGCCount = gcCount;

            var loaded = FlightGlobals.VesselsLoaded;
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Count; ++i)
                {
                    var v = loaded[i];
                    if (v == null) continue;
                    ++stats.vesselsLoaded;
                    int parts = v.parts.Count;
                    stats.partsTotal += parts;
                    if (parts > stats.partsMaxVessel) stats.partsMaxVessel = parts;
                    var wm = v.ActiveController().WM;
                    if (wm != null && wm.guardMode) ++stats.guardModeWMs;
                }
            }
            var fired = BDATargetManager.FiredMissiles;
            if (fired != null)
            {
                for (int i = 0; i < fired.Count; ++i)
                    if (fired[i] is MissileBase missile && missile != null && missile.HasFired && !missile.HasExploded) ++stats.missilesInFlight;
            }

            PerfCsv.AppendRow(rows, stats, PerfProbe.Accumulator, ticksToMs);
            rows.Append('\n');
            ++rowCount;
            float realtime = Time.realtimeSinceStartup;
            if (rows.Length >= FlushChars || realtime - lastFlushRealtime > FlushInterval)
            {
                writer.Write(rows.ToString());
                rows.Length = 0;
                writer.Flush();
                lastFlushRealtime = realtime;
            }
            lastProbeMs = (PerfProbe.Now() - now) * ticksToMs;
        }
    }
}
