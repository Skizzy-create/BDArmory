# BDArmory Overdrive perf: PC test

This is the message for the PC test agent. Work through the sections in order. Tick each step, and note the result next to anything that fails or looks odd.

Branch: `overdrive-perf`. Background and column meanings are in `PERF.md`.

**Goal:** measure where the frame time goes in a big battle of about 20 jets with many missiles (about 100 launched in total), to find out:

- how much of the frame is BDArmory and how much is everything else;
- what the new fixes and the `GUARD_SCAN_INTERVAL` option change.

Conventions:

- `<KSP>` is the PC install: `D:\Touch_Grass_Later\KerbalSpaceProgram\English`.
- `<REPO>` is the BDArmory source checkout on the PC: a clone of https://github.com/Skizzy-create/BDArmory, the folder that contains `BDArmory\` and `Overdrive\`.
- "Old DLL" is the BDArmory build currently installed on the PC. "New DLL" is the one built from `overdrive-perf`.
- **Log file:** `<KSP>\KSP.log`. KSP overwrites it every time it starts, so copy it after every run, before starting KSP again.
- Change only what a step says. Edit settings files only while KSP is closed: BDArmory writes `settings.cfg` when it exits and would overwrite your edit.
- Don't push anything to the BDArmory repo. The only push is the report branch in section G.

## A. Build the branch

1. In `<REPO>`, fetch and check out the branch:

   ```
   git fetch origin
   git switch overdrive-perf
   git pull
   ```

   If `overdrive-perf` isn't on origin, stop and ask the user to push it.
2. **Reference DLLs.** The project references `..\..\_LocalDev\KSPRefs\*.dll`, relative to `<REPO>\BDArmory\BDArmory.csproj`. That is the folder `_LocalDev\KSPRefs` next to `<REPO>`. If it is missing or older than the installed KSP:
   1. Create it.
   2. Copy `Assembly-CSharp.dll`, `KSPAssets.dll` and all `UnityEngine*.dll` from `<KSP>\KSP_x64_Data\Managed\` into it.

   Don't edit the `.csproj`.
3. Build and run the tests:

   ```
   dotnet build BDArmory/BDArmory.csproj -c Release -p:PostBuildEvent= -nologo -v q
   dotnet run --project Overdrive/tests/OverdriveTests
   ```

   Expected: `Build succeeded`, `0 Error(s)`, and `156 passed, 0 failed`. The DLL is `<REPO>\BDArmory\bin\Release\BDArmory.dll`; check its timestamp.

## B. Record the setup

Create a report folder outside `GameData`, for example `<KSP>\..\perf-report-<YYYY-MM-DD>\`. Write the following into `ENVIRONMENT.md` there. It explains most of the results, so don't skip it.

1. **Hardware and drivers:**
   - CPU (Ryzen 9 9950X3D), RAM size and speed;
   - GPU (RTX 5080) and the NVIDIA driver version;
   - whether the AMD chipset driver is installed (Apps and Features: "AMD Chipset Software") and its version;
   - whether Windows Game Mode is on;
   - the monitor refresh rate (143 Hz?) and G-Sync on or off.
2. **KSP settings,** from `<KSP>\settings.cfg`: copy the lines for `PHYSICS_FRAME_DT_LIMIT`, `SHADOWS_QUALITY`, `SHADOWS_FLIGHT_PROJECTION`, `SYNC_VBL`, `FRAMERATE_LIMIT`, `SCREEN_RESOLUTION_WIDTH`, `SCREEN_RESOLUTION_HEIGHT`, `TERRAIN_SHADER_QUALITY`, `PLANET_SCATTER`, `PLANET_SCATTER_FACTOR`, `AERO_FX_QUALITY`, `TEXTURE_QUALITY` and `MAX_VESSELS_BUDGET`.
3. **PhysicsRangeExtender:** `<KSP>\GameData\PhysicsRangeExtender\settings.cfg` (`GlobalRange`, `TerrainExtenderEnabled`).
4. **KSPCommunityFixes:** is it installed? If yes, copy the values of `FlightIntegratorPerf`, `PartSystemsFastUpdate`, `CollisionEnhancerFastUpdate`, `IMGUIOptimization`, `OptimizedModuleRaycasts`, `MinorPerfTweaks`, `FloatingOriginPerf` and `CollisionManagerFastUpdate` from `<KSP>\GameData\KSPCommunityFixes\Settings.cfg`, and from any `KSPCommunityFixes` user override file if one exists.
5. **Mod list:** the folder names in `<KSP>\GameData\`.
6. **BDArmory settings,** from `<KSP>\GameData\BDArmory\PluginData\settings.cfg`: `PERFORMANCE_OPTIONS`, `LIGHTFX`, `BULLET_HITS`, `BULLET_DECALS`, `MAX_NUM_BULLET_DECALS`, `EJECT_SHELLS`, `SHELL_COLLISIONS`, `DEBRIS_CLEANUP_DELAY`, `MAX_GUARD_VISUAL_RANGE`, `ASPECTED_IR_SEEKERS`, `VESSEL_RELATIVE_BULLET_CHECKS`, `DRAW_AIMERS`, `RADAR_LOCKS_FOLLOW_MULTITARGET`, and any `DEBUG_*` set to True.

## C. Install the new DLL safely

1. Quit KSP.
2. Back up the old DLL. Copy `<KSP>\GameData\BDArmory\Plugins\BDArmory.dll` to a folder outside `GameData`, for example `<KSP>\..\bda-backup\BDArmory.dll`. Don't leave a backup inside `GameData`: KSP would load it twice.
3. Back up `<KSP>\GameData\BDArmory\PluginData\settings.cfg` to the same backup folder.
4. Don't copy the new DLL yet. Run A uses the old DLL.

## D. The battle

Use the same battle for every run: same craft, same spawn, same settings, same camera. Results vary from run to run, so the battle must be big enough and long enough that the averages mean something.

1. **Craft.** Use the jets from the original report if you can find them. Check the save's `Ships\SPH` folder and `<KSP>\AutoSpawn\`, or ask the user which craft. Otherwise, use one armed air-to-air jet with guns and 4 to 6 missiles, so that 20 jets launch about 100 missiles in total. Note the craft name and part count in the report.
2. **Spawn** 20 jets in two teams of 10, in the air, about 10 to 20 km apart. Use either:
   - BDArmory's Vessel Spawner, with the craft in an `<KSP>\AutoSpawn\<folder>` and the Spawn window;
   - or the Vessel Mover multi-spawn (count 10, "Spawn in the air", 3,000 m, speed 250 m/s) once per team, then set the teams.

   Write down exactly what you did, so that every run repeats it.
3. **Start** a BDArmory competition, so guard mode, the AIs and scoring are all on.
4. Use the default camera following one jet. Keep BDArmory windows closed during the battle, except the minimum needed to start it, and keep the KSP window focused.
5. Let the battle run for at least 3 minutes of real time, or until one team is destroyed. Then return to the Space Center, which closes the flight scene and finishes the CSV.
6. **Task Manager**, during the heaviest part of every run (most missiles in the air):
   1. Open the Performance tab, select CPU, right-click the graph, and choose "Change graph to" then "Logical processors".
   2. Note the overall CPU %, and which logical processor numbers are near 100%. Is it one processor? Is it in 0-15 (CCD0, the V-Cache CCD on the 9950X3D) or in 16-31?
   3. Take a screenshot (Win+Shift+S) and save it in the report folder as `taskmgr_<run>.png`.
   4. Note the GPU % from the same tab.
7. **FPS.** With the new DLL, the probe records it. For run A, read an FPS overlay (the NVIDIA app overlay, Alt+R, or any other) at about 30 s, 1 min, 2 min and 3 min, and note the lowest value you see.
8. After each run, quit KSP, or at least leave the flight scene. Then copy:
   - `<KSP>\KSP.log` to `KSP_<run>.log` in the report folder;
   - with the new DLL, the newest `<KSP>\GameData\BDArmory\PluginData\PerfProbe\perf_*.csv` to `perf_<run>.csv`.

## E. The runs

Do runs A, B and C. D and E are optional but useful.

1. **Run A: old DLL, baseline.** Play the battle (section D) with the old DLL. Record the fps readings, the Task Manager observations, and `KSP_A.log`.
2. **Install the new DLL.** Quit KSP and copy `<REPO>\BDArmory\bin\Release\BDArmory.dll` over `<KSP>\GameData\BDArmory\Plugins\BDArmory.dll`. Then edit `<KSP>\GameData\BDArmory\PluginData\settings.cfg`: inside the `BDASettings { ... }` node, add these two lines, or change them if they are already there:

   ```
   	PERF_PROBE = True
   	GUARD_SCAN_INTERVAL = 1
   ```
3. **Run B: new DLL, probe on, stock scan interval.** Start KSP. In `KSP.log`, expected: no BDArmory `Exception` during loading. Then play the battle. When the flight scene starts, `KSP.log` has a line `[BDArmory.PerfProbe]: Recording to ...perf_....csv` followed by the system info. When the scene ends, it has `[BDArmory.PerfProbe]: Wrote N rows`. Copy the CSV and the log.
4. **Run C: `GUARD_SCAN_INTERVAL = 3`.** Quit KSP, set `GUARD_SCAN_INTERVAL = 3` in `settings.cfg`, and play the same battle. Also watch for gameplay differences: do the jets still use flares and chaff, and evade incoming missiles about as well as in B? Note anything that looks worse.
5. **Run D (optional): pinned to the V-Cache CCD.** Use this if the pegged processor in B was in 16-31, or to check either way. Set `GUARD_SCAN_INTERVAL = 1` again. Start KSP with its affinity set to CCD0, either by launching it from a command prompt with `start "" /affinity 0xFFFF "<KSP>\KSP_x64.exe"`, or with Task Manager, Details, right-click `KSP_x64.exe`, Set affinity, processors 0-15. If KSP normally starts through the Epic launcher, start the exe directly for this run, or set the affinity in Task Manager as soon as KSP is running. Play the same battle.
6. **Run E (optional): graphics and FX reduced.** `GUARD_SCAN_INTERVAL = 1`, no affinity.
   - In KSP's settings, turn shadows off. Write down the old value so you can restore it.
   - In BDArmory's settings, set `PERFORMANCE_OPTIONS = False`, which turns off explosion lights and other FX.
   - Play the same battle. This separates rendering and FX from the rest.
   - Restore both settings afterwards.

## F. Analyse

For each CSV (B, C, D, E), run the summary script from `<REPO>`:

```
python Overdrive/perf_summary.py "<report folder>\perf_B.csv" --competition-only --skip-seconds 10 > "<report folder>\summary_B.md"
```

If `--competition-only` leaves no rows (no competition was started), leave it out. The script needs only Python 3.

In each `KSP_<run>.log`:

1. Count the lines containing `detected at` (`grep -c "detected at" KSP_A.log`). With the old DLL, these are the "DEBUG ... detected at" errors. With the new DLL they only appear with Debug AI or Debug Radar on, so B should have 0.
2. Count `Exception` lines (`grep -c "Exception" ...`) and list the distinct exception messages, each with its first stack trace.
3. Copy the `[BDArmory.PerfProbe]` lines.

## G. Report and push

1. Write `REPORT.md` in the report folder with:
   1. **Summary**, a few lines:
      - fps (mean and lowest) for each run;
      - BDArmory's share of the frame (`bda_ms`) against everything else (`non_bda_ms`);
      - the biggest phase among FixedUpdate scripts, physics, Update and everything else;
      - the top 5 BDArmory sections;
      - physics steps per frame;
      - GC pauses.
   2. **A table, one row per run:** mean and p95 `frame_ms`, `bda_ms`, the estimated "KSP + other mods' FixedUpdate", `physics_ms`, `update_ms`, `other_ms`, mean `fixed_steps`, peak `missiles_in_flight`, peak `parts_total`, and GC frames.
   3. **B against C:** the change in `guard_scan_ms`, `wm_fixed_ms` and `frame_ms`, and any gameplay differences you noticed.
   4. **D and E results,** if run.
   5. **Task Manager notes** for each run: overall CPU %, the pegged processor numbers, GPU %.
   6. **Log findings:** the `detected at` counts for A and B, the exceptions, anything else unusual.
   7. **Deviations** from this plan, and anything that didn't work.

   Also include `ENVIRONMENT.md`, the `summary_*.md` files, the `perf_*.csv` files, the `KSP_*.log` files and the screenshots. Zip any file over 20 MB; GitHub rejects files over 100 MB.
2. **Check for secrets** before committing. Don't include anything with tokens, passwords or account details. `KSP.log` and the CSVs normally contain none, but search the files for `token`, `password` and `key=` first.
3. **Push** to the report repository, https://github.com/Skizzy-create/skizzy-protocol, on the branch `test-reports/<YYYY-MM-DD>`, using the date of the test:

   ```
   git clone https://github.com/Skizzy-create/skizzy-protocol    (or: cd skizzy-protocol && git fetch origin)
   cd skizzy-protocol
   git switch test-reports/<YYYY-MM-DD> 2>/dev/null || git switch -c test-reports/<YYYY-MM-DD>
   mkdir -p test-reports/perf-overdrive-<YYYY-MM-DD>
   (copy the report folder's files into it)
   git add test-reports/perf-overdrive-<YYYY-MM-DD>
   git commit -m "Perf test report <YYYY-MM-DD>: BDArmory overdrive-perf, 20 jets, about 100 missiles"
   git push -u origin test-reports/<YYYY-MM-DD>
   ```

   - The commit author email must be `asliakartik@gmail.com`. Check `git config user.email`; if it is different, commit with `git -c user.email=asliakartik@gmail.com commit ...`.
   - If commit signing is set up and fails, stop and report. Don't turn signing off.
   - Never force-push.

## H. Restore

1. Quit KSP.
2. Copy the backed-up `BDArmory.dll` and `settings.cfg` back into `<KSP>\GameData\BDArmory\Plugins\` and `<KSP>\GameData\BDArmory\PluginData\`. This also turns the probe off. Skip this only if the user wants to keep the new DLL; in that case, set `PERF_PROBE = False` and `GUARD_SCAN_INTERVAL = 1` instead.
3. Restore any KSP setting you changed for run E.
4. Move `<KSP>\GameData\BDArmory\PluginData\PerfProbe\` out of `GameData`, after its CSVs are in the report.
5. Start KSP once and check that the main menu loads without BDArmory exceptions in `KSP.log`.
