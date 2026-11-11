# BDArmory Overdrive: big-battle performance

Branch `overdrive-perf`. This covers the report that a battle of about 20 jets and 100 missiles runs below 10 fps on the test PC (Ryzen 9 9950X3D, RTX 5080, 1440p at 143 Hz), while CPU use stays around 15% and GPU use around 22%. It contains:

- the likely costs, ranked;
- the changes made;
- a per-frame perf probe;
- settings advice;
- a multithreading evaluation;
- the answer on frame generation.

The in-game test plan for the PC is `PERF-PC-TEST.md`. The CSV summary script is `perf_summary.py`.

Everything except the code changes is a static analysis. The costs are estimated from the code and from how Unity 2019.4 and KSP 1.12 work. The probe exists to replace these estimates with measurements, and section 9 lists what it needs to answer.

## 1. What the symptoms say

- **Main-thread bound.** On a 16-core, 32-thread CPU, 15% total load is about five threads' worth of work:
  - one thread at 100%: Unity's main thread, which runs every KSP and mod script, steps the physics, and prepares most of the rendering;
  - the render thread, audio, and PhysX/job helpers make up the rest.

  At 22%, the GPU is waiting for the CPU, so lowering graphics settings or adding frame generation won't change how fast the simulation runs. Task Manager's per-logical-processor view should show one processor pegged.
- **The editor's 72 fps comes from v-sync, not from a slow editor.** At 143 Hz, a frame that takes slightly more than 7 ms waits for the second refresh, which gives 143 / 2 = 71.5 fps. Turn v-sync off, or use G-Sync with a frame limiter, to see the real number.
- **Below 10 fps the game also runs in slow motion.** KSP's "Max Physics Delta-Time per Frame" (`PHYSICS_FRAME_DT_LIMIT` in `settings.cfg`, default 0.04 s) caps how much game time one frame may simulate.
  - At 0.04, each slow frame runs 2 physics steps of 0.02 s, so at 10 fps the game advances 0.4 s per real second.
  - At higher values (up to 0.12), each slow frame runs up to 6 steps. Most of the frame is spent on those steps, so the frame gets longer and the fps falls further, although game time keeps up better.

  The probe's `fixed_steps` column shows which case a battle is in.
- **The 9950X3D's extra cache is on only one of its two CCDs.** That is normally CCD0, which is logical processors 0 to 15. KSP's main thread walks a large, scattered heap of thousands of parts, modules and Transforms, which is the kind of work that extra cache helps. If Windows doesn't treat KSP as a game, the main thread may run on the other CCD. The PC test checks which processor is pegged and tries a run with the affinity set to CCD0. The gain is unknown until measured.

## 2. Where the time goes at 20 jets and 100 missiles (static ranking)

These symbols are used below:

| Symbol | Meaning | Example value |
|---|---|---|
| W | weapon managers in guard mode | 20 |
| V | loaded vessels: jets + missiles in flight + debris | about 150 |
| M | missiles in flight | 100 |
| P | physical parts: 20 jets × 50-150 parts + 100 missiles × 1-10 parts | 1,500-3,500 |
| S | physics steps per frame | 2 when slow at the default setting, up to 6 |

### 2.1 BDArmory

Ranked by estimated cost per frame. The estimates come from operation counts multiplied by typical Unity costs, so treat them as orders of magnitude.

1. **Guard view scan: terrain linecasts for every missile.**
   - Where: `RadarUtils.GuardScanInDirection`, called from `MissileFire.UpdateGuardViewScan`, which runs every physics step for every weapon manager in guard mode. Weapon managers outside guard mode run it too when non-guard-mode countermeasures are set.
   - What it did: walked every loaded vessel, calling `GetComponent<TargetInfo>()` on each (W × V = 3,000 calls per step). For every missile, it ran a `Physics.Linecast` against terrain **before** checking whether the missile could be detected at all. That is W × M = 2,000 linecasts per step, or 4,000 per frame at S = 2. These lines are often kilometres long across terrain colliders, so they cost microseconds each: an estimated 10-40 ms per frame for this item alone.
   - Other costs: each call allocates a `ViewScanResults` with a new `List`. While a missile is incoming, `StartCoroutine(UnderAttackRoutine())` runs every step for every weapon manager, which allocates too.
   - Status: **fixed** (identical results, see 3.1). There is also an opt-in fidelity setting, `GUARD_SCAN_INTERVAL` (see 3.2).
2. **Active radar missile seekers.**
   - Where: `RadarUtils.RadarUpdateMissileLock`, called from `MissileLauncher`/`MissileBase` every frame for each active radar missile.
   - What it does, per missile: looks up the weapon manager of all V vessels. For each vessel in the seeker's field of view, it also runs `RadarTerrainNotchingCheck` (raycasts), `GetVesselRadarSignature`, and `GetStandoffJammingModifier`, which loops over all V vessels again with a `GetComponent<VesselECMJInfo>()` each. That makes the cost roughly M_active × V, plus M_active × in-view targets × V.
   - Status: **partly fixed.** The standoff jammer loop now skips vessels that have no jammer before the lookup and `GetComponent` (see 3.3), and the weapon manager lookup is cheaper (see 3.5). The probe column is `radar_missile_lock`.
3. **OnGUI on hundreds of components.**
   - Where: `OnGUI` methods on `MissileLauncher`/`MissileBase` (every missile part, including the unfired missiles on the jets' rails), `ModuleWeapon`, `TargetInfo` (every reported vessel), `ModuleRadar`, `RadarWarningReceiver`, `ModuleIRST`, `VesselRadarData`, the AIs, `MissileFire`, `ExplosionFx` and `PooledRocket`. That is 500 or more components.
   - Why it costs: Unity calls each one at least twice per frame (Layout and Repaint, and more on input events), even when the body returns at once. With `useGUILayout` left on, each call also sets up GUILayout state. The estimate is several ms per frame of call overhead.
   - What the probe sees: the bodies only (`wm_gui`, `missile_gui`, `setup_gui`; the `_n` columns count the calls). The per-call overhead lands in `other_ms`.
   - KSPCommunityFixes' `IMGUIOptimization` patch reduces this overhead. It is on in the laptop install; check the PC.
   - Status: **not changed.** Setting `useGUILayout = false` on components that never use GUILayout would halve the calls, but each `OnGUI` path has to be checked for GUILayout use and tested in game first.
4. **`HitpointTracker.FixedUpdate` on every part.**
   - What it did: P × S calls per frame. Each call searched the armour list by name (`ArmorInfo.armors.FindIndex(t => t.name == "None")`), plus Unity's call overhead: about 2-7 ms per frame at 3,000 parts.
   - Status: **fixed.** The index is now cached (see 3.4). The Unity call overhead remains.
5. **Bullets.** `PooledBulletManager.FixedUpdate` runs two short raycasts per bullet per step (`RaycastNonAlloc` forward and reverse) plus hit handling, and fills impact FX and decal pools. The cost scales with the number of guns firing. Status: not changed. Probe: `bullets` and `bullets_active`.
6. **Explosions.** `ExplosionFX`: an overlap sphere, then a line-of-sight raycast per part in the blast radius, a `Light` per explosion (`LIGHTFX`), and particles. These come in bursts when salvos hit. Status: not changed; see the settings advice. Probe: `explosions`.
7. **`MissileFire.CanSeeTarget`.**
   - Cost: a terrain linecast and a `GetComponent<VesselCloakInfo>()` per check. It also built debug strings with `$"..."` on every stale-target result, even though only the debug displays read them.
   - Status: **the strings are now built only when a debug display that shows them is on** (see 3.6). Probe: `can_see_target`.
8. **Error logging during battles.** `BDATargetManager.ReportVessel` called `Debug.LogError("DEBUG ... detected at ...")` unconditionally whenever a reporter re-detected a vessel it had marked stale. This is a leftover debug line from upstream. `ReportVessel` runs from the radar and guard scans every physics step, so this can fire every step for each reporter while a target stays stale. Unity's `LogError` captures a stack trace and writes it to disk, which costs a millisecond or so per call.
   - Status: **now only with Debug AI or Debug Radar on** (see 3.6). With the old DLL, count these lines in `KSP.log` to see how often it fired.
9. **Radar scans and locks.** `RadarUtils.RadarUpdateScanLock`. With `RADAR_LOCKS_FOLLOW_MULTITARGET` (an Overdrive setting, default on), a radar can hold as many locks as the weapon manager's multi-target settings allow. Each lock is tracked every frame. Probe: `radar_scan_lock`.
10. **Pilot AI.** `BDModulePilotAI.AutoPilot` (fly-by-wire) and `FixedUpdate`: evasion, terrain avoidance raycasts, and steering, per jet per step. Probe: `ai_fbw` and `ai_fixed`.
11. **Coroutines.** `GuardMissileRoutine`, `GuardTurretRoutine`, the salvo routines and others aren't timed. Their time lands in `update_ms`, `physics_ms` or `other_ms`, depending on what they yield on.

### 2.2 KSP core and other mods

These costs scale with part count and vessel count, not with BDArmory. With 2,000 to 3,500 physical parts, expect the following.

1. **PhysX and joints.**
   - Every part is a rigidbody, and every attachment is a `ConfigurableJoint`. Kerbal Joint Reinforcement adds more joints per vessel.
   - Unity's PhysX step can use worker threads for some stages, but the main thread blocks until the step finishes. Each vessel's joint chain is one island that is solved on one thread. Collision callbacks (`OnCollisionEnter`/`Stay` on parts, BDArmory ramming and impact handling) run on the main thread.
   - Probe: `physics_ms`.
2. **FlightIntegrator.** Aerodynamics and thermal run per part per step: drag cubes, lift and body lift, conduction, convection and radiation. With ModularFlightIntegrator installed, mods hook into it as well. 20 supersonic jets plus 100 missiles is the worst case: every part is aerodynamically active, and thermal convection matters at speed. KSPCommunityFixes' `FlightIntegratorPerf` helps.
3. **PartModule `FixedUpdate`/`Update` on every part.**
   - Stock: `ModuleLiftingSurface` and `ModuleControlSurface` on every wing and control surface, `ModuleEngines` with its exhaust-damage raycasts per thruster, gimbals, intakes, resource flow, `ModuleWheel*` and `ModuleResourceIntake`.
   - Mods: Waterfall effect controllers, RocketSoundEnhancement, TweakScale, B9PartSwitch, HullBreach (check whether it patches every part), and the BDArmory modules themselves.
   - This is the "KSP + other mods' FixedUpdate" figure in the probe summary.
4. **Per-vessel work.** Every missile is its own vessel:
   - `Vessel.FixedUpdate`, `VesselPrecalculate`, orbit updates, `CollisionEnhancer` raycasts for fast parts, and FloatingOrigin/Krakensbane shifts that move every vessel;
   - PhysicsRangeExtender keeping every vessel within its range loaded and unpacked, with its terrain extender forcing terrain to load under far vessels;
   - **events on vessel creation and destruction.** Every missile launch, decouple, part death and debris split fires `onVesselCreate`, `onVesselWasModified`, `onPartDie` and similar events. Mods that rebuild per vessel on these events add spikes on every launch: KJR re-jointing, Kerbal Engineer's simulation, BDArmory's own module registry.
5. **Rendering preparation on the main thread.**
   - Culling and draw-call submission scale with renderers × lights × shadow cascades × cameras. That means every part's meshes; four shadow cascades; forward rendering's extra pass per pixel light (explosion lights; the Deferred mod changes this); and the extra cameras and passes from Scatterer (ocean), EVE (clouds) and TUFX (post-processing).
   - The GPU is idle at 22%, but submitting the work is CPU time on the main thread and render thread. Probe: `other_ms`.
6. **Garbage collection.** KSP's Unity 2019.4 Mono runtime uses the Boehm collector, which stops everything for each collection. KSP 1.12 isn't known to use Unity's incremental mode. A heap of several GB, typical for a modded install, makes each pause long (tens to hundreds of ms). BDArmory's per-step allocations make collections more frequent. Probe: `gc_count` and `heap_mb`.

**Decision rule for a Harmony-based performance mod for KSP's own code:** build it only if the probe shows that the non-BDArmory FixedUpdate time is a large share of the frame, meaning FlightIntegrator and part modules. `perf_summary.py` estimates that as `fixed_scripts_ms` minus BDArmory's fixed sections.

| If the probe shows | What it means |
|---|---|
| `fixed_scripts_ms` minus BDA's fixed sections is large | A Harmony mod has a target: FlightIntegrator and part modules. KSPCommunityFixes already patches several of these hot paths, so check it is installed with its perf patches on first. |
| `physics_ms` dominates | PhysX can't be patched usefully. Only fewer parts, joints and colliders help (part count, KJR). |
| `other_ms` dominates | It is rendering, OnGUI and GC. Graphics settings and the OnGUI work help, not a physics mod. |
| `bda_ms` is large | Keep optimising BDArmory. |

## 3. Changes in this branch

All changes are marked `[Overdrive perf]` in the code. With the default settings, none of them changes game behaviour.

### 3.1 Guard scan: cheap checks before the terrain linecast

In `RadarUtils.GuardScanInDirection`, a missile's terrain linecast now runs only after these checks:

- the missile isn't one this weapon manager fired;
- the missile could be detected by the MWS, by sight (distance and view cone), or by the RWR.

The original code skipped such missiles after the linecast anyway. The checks have no side effects: the MWS check runs without adding RWR display contacts, and the original code still adds them afterwards, as before. So the results are identical and only the wasted linecasts are removed.

- **New code:** a helper `GuardScanMissileCouldBeDetected` and the pure gates in `Utils/PerfLogic.cs` (`GuardScanGates`).
- **Test:** `GuardScanGatesTests.ChangedFlowMatchesOriginalForAllGateCombinations` transcribes the original flow and checks the changed flow against it for all 8,192 combinations of the 13 inputs:
  - same outcome;
  - same MWS display side effects;
  - fewer than half the raycasts across the combination space.

  Truth-table tests cover each gate.
- **How much it saves** depends on the craft. The fewest linecasts are left with a narrow guard view angle, a short guard range, or a short MWS range. With a 360° view and a guard range covering the whole battle, every missile within range is still "in sight" and still needs a linecast. Probe: `guard_scan`.

### 3.2 New setting: `GUARD_SCAN_INTERVAL` (fidelity trade-off, default 1 = stock)

`MissileFire.UpdateGuardViewScan` runs the guard view scan only every N physics steps (1 to 10). It is staggered between craft by part id, so the load spreads evenly. Between scans, the weapon manager keeps the last results.

- **Trade-off:** reactions to incoming missiles and gunfire run on results up to N−1 steps old, which is 20 ms per step. Countermeasures, evasion triggers and the RWR switch-on happen up to that much later. The "speed up the target scan when laser-guided missiles are seen" adjustment happens N times less often.
- **Suggested values:** 2 to 3 for large battles; the extra delay is 20 to 40 ms. Leave it at 1 for competitions where reaction timing matters.
- **Tests:** `ScanStaggerTests`: interval 1, 0 or negative runs every frame; N runs exactly once per N calls; callers are spread across the interval and balanced; changing N re-phases correctly.

### 3.3 Standoff jammer loop skips vessels without jammers

`VesselECMJInfo` now keeps a registry of the GameObjects it is attached to. It registers in `Awake`, and again explicitly after `AddComponent` because `Awake` is delayed on inactive objects. It unregisters in `OnDestroy`.

`RadarUtils.GetStandoffJammingModifier` checks the registry before the weapon manager lookup and `GetComponent<VesselECMJInfo>()`. If a vessel isn't in the registry, `GetComponent` would have returned null and the original code would have skipped it anyway. A stale registry entry only falls through to the original path, so results are the same.

One side benefit: the loop no longer throws a `NullReferenceException` for loaded vessels without an `ActiveController`, such as asteroids.

### 3.4 Cached armour index

`ArmorInfo.NoneIndex` caches `armors.FindIndex(t => t.name == "None")`. It recomputes the index if the list is replaced or resized. `HitpointTracker.FixedUpdate` uses it on every part every step. The result is identical.

### 3.5 One dictionary lookup for the active controller

`ActiveController.GetActiveController` used `ContainsKey` followed by the indexer. It now does one `TryGetValue`. This is the `vessel.ActiveController()` behind every `.WM`/`.AI` lookup, which runs thousands of times per frame. The result is identical.

### 3.6 Debug-only work only when debugging

- `MissileFire.CanSeeTarget` builds the three `staleTargetDebugString` entries only when `DEBUG_TELEMETRY`, `DEBUG_AI` or `DEBUG_WEAPONS` is on, since only those displays read them.
- `BDATargetManager.ReportVessel` logs its "DEBUG ... detected at" error only with `DEBUG_AI` or `DEBUG_RADAR`.

Gameplay is unchanged. Only debug output is affected.

### 3.7 The perf probe

See section 5. When the probe is off, each timed method pays one static bool check, and the recorder component disables itself.

### Tests

There are 34 new tests in `Overdrive/tests/OverdriveTests/PerfProbeTests.cs` and `PerfLogicTests.cs`. They link `BDArmory/Utils/PerfProbe.cs` and `PerfLogic.cs`, which are pure C#. Run them with:

```
dotnet run --project Overdrive/tests/OverdriveTests
```

The run gives 156 passed, 0 failed.

## 4. New settings

Both settings are in `GameData/BDArmory/PluginData/settings.cfg`, in the `BDASettings` node. There is no UI for them yet: edit the file while KSP is closed.

| Setting | Default | Meaning |
|---|---|---|
| `PERF_PROBE` | `False` | Record per-frame timings to a CSV in the flight scene (section 5). Read when the flight scene starts. |
| `GUARD_SCAN_INTERVAL` | `1` | Guard view scan every N physics steps, staggered between craft (1 to 10; values outside that range are clamped). 1 is stock. See 3.2. |

## 5. The perf probe

**Turning it on:** set `PERF_PROBE = True` in `settings.cfg`, then enter the flight scene.

**Output:** `GameData/BDArmory/PluginData/PerfProbe/perf_yyyyMMdd_HHmmss.csv`, one file per flight scene, one row per frame.

**Writing:** rows are buffered and flushed every 2 seconds. Recording stops after 500,000 rows. If KSP crashes, the last line may be cut off; `perf_summary.py` skips it.

**Log line:** `KSP.log` gets one `[BDArmory.PerfProbe]` line with:

- the file path;
- the BDArmory and Unity versions;
- CPU and GPU;
- resolution, v-sync and target fps;
- `fixedDeltaTime` and `maximumDeltaTime` (the max physics delta per frame);
- the shadow settings;
- `GUARD_SCAN_INTERVAL`.

When the flight scene ends, it logs the number of rows written.

**How the frame is split:**

- **Timing points.** KSP's `TimingManager` stages provide them: `ObscenelyEarly` runs before, and `BetterLateThanNever` after, all regular `FixedUpdate`, `Update` and `LateUpdate` scripts. A `WaitForFixedUpdate` coroutine resumes after the physics simulation.
- **Frame boundary.** A row covers the time from one frame's early `Update` callback to the next frame's. That interval contains:
  - this frame's `Update` and `LateUpdate`;
  - rendering;
  - `OnGUI`;
  - the physics steps that run just before the next `Update`.
- **BDArmory time.** Stopwatch scopes at the entry points Unity or KSP call: `FixedUpdate`, `Update`, `OnGUI`, fly-by-wire and the TimingManager callbacks. When one timed entry point calls another, the time is counted once in `bda_ms`. Nested sections (`guard_scan` and so on) are shown separately and are already included in their callers.

**Columns:**

| Column | Meaning |
|---|---|
| `frame` | Unity frame number |
| `game_time`, `real_time` | Game time (`Time.time`) and real seconds since recording started. If game time advances slower than real time, the game is in slow motion. |
| `frame_ms` | The whole frame, as measured by the probe |
| `unscaled_dt_ms` | Unity's own frame delta, for cross-checking |
| `fixed_steps` | Physics steps in this frame |
| `fixed_scripts_ms` | All `FixedUpdate` scripts over all steps: KSP (FlightIntegrator, vessels, part modules), mods, BDArmory |
| `physics_ms` | From the end of the `FixedUpdate` scripts to the resume of the `WaitForFixedUpdate` coroutine, summed over steps. This is the PhysX step plus collision and trigger callbacks, plus any other mods' `WaitForFixedUpdate` coroutines that Unity happens to run before the probe's. |
| `update_ms` | All `Update` scripts, `yield null` coroutines, and `LateUpdate` scripts |
| `other_ms` | `frame_ms` minus the three phases above: rendering preparation and submission, `OnGUI`, GC pauses, waiting for v-sync or a frame limiter, and Unity overhead |
| `bda_ms` | Time inside the BDArmory entry points (all phases) |
| `non_bda_ms` | `frame_ms` minus `bda_ms`: KSP core, PhysX, rendering, other mods, and BDArmory coroutines |
| `gc_count` | Garbage collections during the frame (generation 0 count; Boehm only has one) |
| `heap_mb` | Managed heap size |
| `vessels_loaded`, `parts_total`, `parts_max_vessel` | Loaded vessels, the total part count across them, and the part count of the largest vessel |
| `missiles_in_flight` | Fired missiles that haven't exploded (`BDATargetManager.FiredMissiles`) |
| `bullets_active` | Bullets in flight |
| `guard_wms` | Weapon managers in guard mode |
| `competition_active` | 1 while a BDA competition is running |
| `probe_ms` | The probe's own cost for the previous row |
| `<section>_ms`, `<section>_n` | Time and number of calls for each BDArmory section (see the table below) |

**Sections:**

| Section | Code |
|---|---|
| `wm_fixed` | `MissileFire.OnFixedUpdate` |
| `wm_update` | `MissileFire.OnUpdate` |
| `wm_pd` | `MissileFire.PointDefence` |
| `wm_gui` | `MissileFire.OnGUI` |
| `missile_fixed` | `MissileLauncher.OnFixedUpdate` (every missile part, fired or not) |
| `missile_gui` | `MissileLauncher.OnGUI` |
| `mmg_fixed` | `BDModularGuidance.OnFixedUpdate` |
| `ai_fbw` | `BDModulePilotAI.AutoPilot` |
| `ai_fixed` | `BDModulePilotAI.FixedUpdate` |
| `radar_fixed` | `ModuleRadar.FixedUpdate` |
| `radar_update` | `ModuleRadar.Update` and `LateUpdate` |
| `vrd` | `VesselRadarData` (`Update`, `LateUpdate` and `FixedUpdate`) |
| `rwr_fixed` | `RadarWarningReceiver.OnFixedUpdate` |
| `irst_fixed` | `ModuleIRST.FixedUpdate` |
| `weapon_fixed` | `ModuleWeapon.FixedUpdate` |
| `weapon_update` | `ModuleWeapon.Update` |
| `bullets` | `PooledBulletManager.FixedUpdate` |
| `rockets` | `PooledRocket.FixedUpdate` |
| `explosions` | `ExplosionFx.Update` and `FixedUpdate` |
| `hp_fixed` | `HitpointTracker.FixedUpdate` |
| `competition` | `BDACompetitionMode.FixedUpdate` |
| `setup_update` | `BDArmorySetup.Update` |
| `setup_gui` | `BDArmorySetup.OnGUI` (the BDA windows) |
| `target_manager` | `BDATargetManager.Update` |

Nested sections, already included in the ones above: `guard_scan` (`RadarUtils.GuardScanInDirection`), `target_selection` (`MissileFire.SmartFindTarget`), `heat_target` (`BDATargetManager.GetHeatTarget`), `radar_missile_lock`, `radar_scan_lock`, and `can_see_target`.

**Not covered:**

- BDArmory coroutines.
- Components that aren't instrumented: the surface, VTOL and orbital AIs, turrets, targeting cameras, countermeasures, FX helpers and event handlers. Their time counts as non-BDArmory.
- Unity's per-call overhead for `OnGUI` and `FixedUpdate`, which lands in `other_ms` and `fixed_scripts_ms`.

So `bda_ms` is a lower bound for BDArmory's real share.

**Summarising:**

```
python Overdrive/perf_summary.py perf_....csv --competition-only --skip-seconds 10
```

It prints:

- frame-time percentiles;
- the phase split;
- BDArmory against everything else;
- KSP and mods' FixedUpdate time, estimated;
- the top sections with microseconds per call;
- frame time by missiles in flight and by part count;
- physics steps per frame;
- GC pauses.

## 6. Settings advice for big battles

The impact estimates are for 20 jets and 100 missiles on a main-thread-bound PC. "High" means it plausibly changes the frame time by 10% or more.

### KSP (main menu Settings, or `<KSP>/settings.cfg`)

| Setting | Advice | Impact |
|---|---|---|
| Max Physics Delta-Time per Frame (`PHYSICS_FRAME_DT_LIMIT`) | 0.03-0.04. Higher values run more physics steps per slow frame, so the fps falls further. Lower values keep the game responsive but run in slow motion. | High (sets the number of steps per frame) |
| Shadows (`SHADOWS_QUALITY`, cascades, distance) | Two cascades or none, shorter distance | Medium (main-thread culling and draws for every part, per cascade) |
| Terrain detail and scatter (`TERRAIN_SHADER_QUALITY`, `PLANET_SCATTER`, `PLANET_SCATTER_FACTOR`) | Lower; scatter off for battles | Low to medium |
| Aero FX quality (`AERO_FX_QUALITY`) | Low or off. Supersonic jets and missiles trigger the per-part aero effects. | Medium |
| Pixel light count | Low (2-4). Each explosion light adds a forward pass per lit object, unless the Deferred mod is installed. | Medium in explosions |
| V-sync and frame limit | Off, or a limit with G-Sync | None in battle; explains the editor's 72 fps |
| Part count | Fewer parts per craft (procedural wings instead of many wing pieces, fewer struts and cosmetic parts); missiles with fewer parts | High. Everything in 2.2 scales with it. |

### BDArmory (`settings.cfg`, BDA settings window)

| Setting | Advice | Impact |
|---|---|---|
| `GUARD_SCAN_INTERVAL` | 2-3 for big battles (adds 20-40 ms of reaction delay) | Medium to high, depending on `guard_scan` |
| Weapon manager guard range and view angle (per craft) | Realistic values, for example 5-10 km visual range for jets rather than the 200 km maximum (`MAX_GUARD_VISUAL_RANGE` = 200000 is only the slider limit). Fewer missiles in sight means fewer linecasts and visibility checks. This changes detection behaviour. | Medium |
| `LIGHTFX` | False (no `Light` per explosion) | Medium in explosions |
| `BULLET_HITS` (with it `BULLET_DECALS`, `EJECT_SHELLS`, `SHELL_COLLISIONS`, `WATER_HIT_FX`) | Off, or keep hits and turn off `SHELL_COLLISIONS` and `EJECT_SHELLS` (shell casings with colliders) and decals (or lower `MAX_NUM_BULLET_DECALS`) | Medium with many guns |
| `PERFORMANCE_OPTIONS` ("Enable FX") | The master FX switch. False turns off explosion lights, water hit FX, gapless particle emitters and flare smoke all at once. Alternatively, keep it True and turn off individual items. | Medium |
| `DEBRIS_CLEANUP_DELAY` | 5-10 s instead of 15. Debris are vessels with parts and physics. | Medium after kills |
| `ASPECTED_IR_SEEKERS`, `VESSEL_RELATIVE_BULLET_CHECKS`, `ASPECTED_RCS`, `CHECK_WATER_TERRAIN` | Keep False (all add per-frame raycasts or calculations) | Medium if turned on |
| `DRAW_AIMERS`, `DEBUG_*`, `DEBUG_LINES`, On-Screen Telemetry | Off during measured battles | Low to medium |
| `PERSISTENT_FX`, `FIRE_FX_IN_FLIGHT` | Keep False | Low to medium |
| `RADAR_LOCKS_FOLLOW_MULTITARGET` | True is the Overdrive default; set it False to reduce lock tracking if `radar_scan_lock` is high | Low to medium |

### Other mods

| Mod | Advice | Impact |
|---|---|---|
| PhysicsRangeExtender | Set the range to the battle's real engagement range plus a margin (30-50 km for jets with BVR missiles; the laptop has 100 km). Turn the terrain extender off unless craft fight near the ground far from the camera. | High. Every vessel in range stays loaded and simulated. |
| KSPCommunityFixes | Install, and keep the performance patches on: `FlightIntegratorPerf`, `PartSystemsFastUpdate`, `CollisionEnhancerFastUpdate`, `IMGUIOptimization`, `OptimizedModuleRaycasts`, `MinorPerfTweaks`, `FloatingOriginPerf`, `CollisionManagerFastUpdate` | High if missing |
| Waterfall, Firefly, VaporCones, RocketSoundEnhancement | Remove or disable for battle benchmarks. Firefly (entry effects at missile speeds), Waterfall (per-effect updates on every engine) and RSE (audio per engine and per collision) all scale with vessels and engines. | Medium each |
| Scatterer, EVE (volumetric clouds), TUFX, Deferred | The GPU isn't the limit, but each adds cameras or passes that cost main-thread time. Try a run with EVE clouds and the Scatterer ocean off. Deferred makes explosion lights cheaper. | Low to medium |
| Kerbal Joint Reinforcement | Keep it if craft need it, but its extra joints add PhysX work and it re-joints on every vessel change. For measurement only, compare one run without it. | Medium |
| Kerbal Engineer, Trajectories | Hide KER flight readouts and turn Trajectories off during battles. Both simulate the active vessel every frame or every few frames. | Low to medium |
| AtmosphereAutopilot, FreeIva, KerbalKonstructs | Active-vessel-only or static. Low impact, unless a battle happens next to many KerbalKonstructs statics. | Low |

**Windows and CPU:** on the 9950X3D, make sure the AMD chipset driver is installed and Game Mode is on. If the pegged logical processor is in the 16-31 range, try `start /affinity 0xFFFF KSP_x64.exe` (CCD0) or Process Lasso. The PC test measures this.

## 7. Multithreading evaluation

Unity and KSP objects (`Part`, `Vessel`, `Transform`, `Rigidbody`, `Physics.*` queries, `GameObject`, IMGUI) may only be used from the main thread. Threading is only possible in this pattern:

1. snapshot plain data on the main thread;
2. compute on workers (`IJobParallelFor`, or Burst through KSPBurst, which provides Burst 1.7.4, Collections, Jobs and Mathematics);
3. apply the results on the main thread, in the same frame or one frame later.

`RaycastCommand` and `SpherecastCommand` are available in KSP's UnityEngine.PhysicsModule. They run physics queries on worker threads against the current physics scene.

Ranked by expected savings:

1. **Batched terrain line-of-sight raycasts (`RaycastCommand`).**
   - Scope: the guard scan's missile linecasts (what is left after 3.1), the `CanSeeTarget` terrain checks, and the radar and seeker terrain and notching checks.
   - Fit: these are single-hit queries on the Scenery layer, the only kind `RaycastCommand` supports in Unity 2019.4. Trigger handling follows `Physics.queriesHitTriggers`, the same as `Physics.Linecast`.
   - Savings: largest of all, if the probe shows `guard_scan` or `can_see_target` high. Most of the raycast time would move to worker threads.
   - Cost: the scans must be restructured into "collect the queries" and "consume the results". That needs either a central pre-pass at a `TimingManager` stage, with results in the same physics step, or results one step late.
   - Not implemented. Equivalence depends on physics-scene timing and can't be tested outside the game. It would need a setting defaulting off, plus an in-game A/B validation mode that runs both paths and compares them.
2. **Candidate prefilters for radar and seeker scans** (distance, angle, field of view over a snapshot of vessel positions; `IJobParallelFor`/Burst). Saves part of the M_active × V loop in `RadarUpdateMissileLock` and the radar scans. Medium savings; the expensive steps after the filter (raycasts, signature lookups) stay on the main thread. Not implemented: wait for `radar_missile_lock` and `radar_scan_lock` data.
3. **Target scoring and multi-target assignment** (`SmartFindTarget`). This runs once per `targetScanInterval` per weapon manager, not every frame. Small savings, and the scoring reads many live Unity objects. Not implemented.
4. **RWR contact sorting.** Small lists. Negligible. Not implemented.
5. **Bullets.** They need all hits along the ray (penetration), which 2019.4's single-hit `RaycastCommand` can't return. Not a fit.

**Implemented: none.** Nothing found so far is both a proven bottleneck and provably equivalent when threaded. The probe data decides whether candidate 1 is worth building behind a default-off setting with in-game validation.

**Can never be threaded:**

- the PhysX simulation as KSP drives it (Unity steps it on the main thread; its own internal worker use isn't something mods control);
- KSP part physics: joints, rigidbodies, FlightIntegrator, and every `PartModule` update;
- vessel creation and destruction, and the events they fire;
- IMGUI;
- anything that reads or writes `Transform`, `Rigidbody` or `GameObject` state.

## 8. DLSS frame generation

**Can we add DLSS frame generation to KSP ourselves? No.**

- **What DLSS-FG needs:** NVIDIA Streamline integrated into the engine's renderer, a D3D12 or Vulkan swapchain, and per-frame motion vectors, depth and a HUD-less colour buffer.
- **What KSP 1.12 is:** Unity 2019.4 on D3D11 with the built-in render pipeline. It is closed source, and mods are managed DLLs loaded after the engine has started. A mod can't switch the graphics API or hook Streamline into the engine's swapchain and present path. The UI (IMGUI and KSP's own UI) is drawn into the same buffer as the scene, so there is no HUD-less colour buffer. A mod could request camera motion vectors (TUFX's motion blur uses them), but that alone doesn't get frame generation.
- **Alternatives that work on any game:**
  - Lossless Scaling (an external app; frame generation from captured frames; adds latency; artifacts on UI and fast motion);
  - NVIDIA Smooth Motion (driver-level, RTX 50 series; D3D11 support is uncertain, so check the current driver);
  - AMD AFMF (AMD GPUs only, so not this PC).
- **None of these fixes the problem.** They interpolate presented frames. The simulation still runs below 10 fps and in slow motion, input latency gets worse, and interpolating from under 10 fps looks poor. Frame generation needs about 60 fps of real frames to look right. The bottleneck here is the main thread (scripts and physics), which only CPU-side work fixes: sections 3 and 6.

## 9. What needs PC data

- **The real BDArmory share.** `bda_ms` against `non_bda_ms` in the battle, and which BDA sections lead. This decides between further BDArmory work and a Harmony mod for KSP's own hot paths (see the decision rule in 2.2).
- **The physics step.** `physics_ms` per step, and its growth with `parts_total`.
- **Rendering and OnGUI.** `other_ms` against the graphics settings. An optional run with shadows and clouds off separates rendering from OnGUI and GC.
- **The guard scan after the fix.** `guard_scan` with `GUARD_SCAN_INTERVAL` 1 against 3.
- **The physics steps regime.** `fixed_steps` with the PC's Max Physics Delta-Time.
- **GC.** Pause frequency and length (`gc_count` frames against the others).
- **The CPU.** Whether the main thread sits on the V-Cache CCD, and what pinning it to CCD0 does.
- **The error log.** Counts of the "DEBUG ... detected at" lines with the old DLL.
