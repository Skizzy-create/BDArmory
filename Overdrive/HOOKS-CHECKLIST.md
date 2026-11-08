# BDArmory Overdrive hooks: in-game test checklist

Branch `overdrive-hooks` (commits from `ec38cf761`, including the review fixes). Work through the sections in order. Tick each step, and note the result next to anything that fails. The API is documented in `HOOKS.md`.

Conventions:

- `<KSP>` is the KSP install folder.
- "Old DLL" is the BDArmory build you are replacing; "new DLL" is the one built from this branch.
- Log file: `<KSP>\KSP.log`. Search it for `[BDArmory.MissileFire]` and `[BDArmory.VesselMover]`; also search for `Exception` and `NullReferenceException` after every scenario.
- Debug toggles are in the BDA settings window (the BDA toolbar button), in the Debugging section. Click "Enable debug settings" first; it reveals `Debug AI` (DEBUG_AI), `Debug Spawning` (DEBUG_SPAWNING), `Debug Weapons` (DEBUG_WEAPONS) and `On-Screen Telemetry` (DEBUG_TELEMETRY). Closing the Debugging section turns them all off again. They are also persisted in `<KSP>\GameData\BDArmory\PluginData\settings.cfg`.

## A. Install the test build safely

1. Quit KSP.
2. Back up the current DLL: copy `<KSP>\GameData\BDArmory\Plugins\BDArmory.dll` to a folder outside GameData, for example `<KSP>\..\bda-backup\BDArmory.dll`. Do not leave the backup inside `GameData` (KSP would load it twice).
3. Also back up `<KSP>\GameData\BDArmory\PluginData\settings.cfg`.
4. Copy the new DLL from the repo, `D:\KerbalSpaceProgram\BDArmory\BDArmory\bin\Release\BDArmory.dll`, over `<KSP>\GameData\BDArmory\Plugins\BDArmory.dll`. Make sure that file is the Release build you just compiled (check its timestamp).
5. Start KSP. Expected: the main menu loads, and `KSP.log` has no `Exception` from BDArmory during loading.
6. **Rollback:** quit KSP, copy the backed-up `BDArmory.dll` (and `settings.cfg` if you want the old settings) back over the files in `<KSP>\GameData\BDArmory\`. The settings file keeps working with the old DLL: unknown `VESSEL_MOVER_SPAWN_*` keys are ignored (and kept when it saves). Restoring the backed-up `settings.cfg` as well is the cleanest rollback. Remove `<KSP>\GameData\OverdriveHarness\` too if you installed the test harness: it needs the new DLL.

## B. Regression with the hooks unused (old DLL vs new DLL)

Goal: with no external code touching the hooks, the new DLL behaves like the old one. Run each scenario once with the old DLL and once with the new DLL, same craft, same positions, same settings. Behaviour is random, so compare patterns rather than exact outcomes. Enable `Debug AI` for these runs.

1. **Air battle, 2 teams x 4 aircraft.** Spawn two teams of four armed aircraft facing each other (guns plus missiles; use the same craft in both runs). Start a competition or turn on guard mode on every aircraft.
2. **Naval battle, 2 teams x 3 ships.** Spawn two teams of three armed ships (guns, missiles, and point defence if the craft has it) on open water. Start a competition or turn on guard mode.
3. For each run, compare:
   1. Target switching: ships and aircraft acquire targets and change them at about the same rate; nobody sits idle with an enemy in range.
   2. Team assist: with `Debug AI` on, `KSP.log` contains `[BDArmory.MissileFire]: <name> called for help from <name>` lines in both runs, and the targets do get swapped.
   3. Missile salvos: missiles are launched, including parallel salvos if the craft's weapon manager is set up for them.
   4. Guns: guns fire at targets in range (watch for `Wpn Autofire False (Weapons ...)` messages in the weapon debug labels; they must not appear when the hooks are unused).
   5. Point defence: APS and point-defence guns shoot down incoming missiles; interceptor missiles still launch.
   6. Competition scoring and log: the score window and `<KSP>\GameData\BDArmory\Logs\` competition log (when written) report kills, hits and so on as before.
4. In `KSP.log`, expected: no new exceptions, no `ExternalTargetFilter threw`, no `ExternalSecondaryTargets threw`, no `lost its external target assignment`, `breaking off its waypoints` or `handler threw` lines.
5. Frame time (the FPS counter or the telemetry window) is unchanged within noise between the old and new DLL.
6. **Waypoint-course competition (stock `SetWaypoints` path).** Create a stock BDA waypoint course (waypoints placed with the BDA waypoint tool, or an existing `WaypointCourses` entry) and run a waypoint competition with two or three aircraft. Expected: the aircraft fly the gates in order, laps count, scores are registered, and slow runners are culled as before. No `[BDArmory.BDGenericAIBase]` lines mention "custom".

## C. Multi-spawn (OD1)

Open the Vessel Mover window (BDA toolbar). In the "Multi-spawn options" section set the values below, then use the Spawn button and click on the ground, water or sky as for a normal spawn. Enable `Debug Spawning`; every multi-spawn then logs `[BDArmory.VesselMover]: Multi-spawn of N <craft> (...)`. Use an armed craft (it has a weapon manager) for the naming checks. After each run, check `KSP.log` for exceptions and for `Multi-spawn copy i/N failed`.

### C1. Counts

For each location below, run counts 1, 6 and 60.

1. **Ground, KSC grass** (open field away from buildings), air off, abreast, spacing auto, per row auto. Expected: N craft in a straight line, evenly spaced, no overlaps, and no two craft touching. Count 60 wraps into rows (about 2 km wide by default).
2. **Ground, near KSC buildings** (click so the line crosses a building or a parked vessel). Expected: the on-screen message "N slot(s) shifted along the line to clear obstacles." or "Spawn point blocked: line moved X m to clear space.", and the same text in the moved/shifted case with `Debug Spawning`. No craft is placed inside a building.
3. **Sea, open water.** Use a ship craft. Expected: copies float in a line, not on land.
4. **Sea, near the coast** (click so the line would hit land). Expected: a "slots shifted", "line moved" or "wrapped into rows" message; no copy ends up on land. If nothing fits, "Multi-spawn cancelled: no clear space within N km (best spot: x/N slots free)." is shown and nothing is spawned. N is at least 2 km even for a small craft (the search always reaches 2 km). This is an acceptable result for count 60 in a small bay.
4b. **Wrap rule: narrow strip.** Ground: click on the KSC runway (or a narrow strip of land between water) with count 12 abreast and a heading across the strip, so a straight line can't fit. Expected: "Clear space along the line ran out: wrapped into N rows." (or "Spawn point blocked: line moved X m to clear space, in N rows."); the copies form several short rows on the strip, none on the water or grass edge if the strip is land. Repeat in trail along a narrow strip: columns along the heading instead.
4c. **Search time.** Click in a spot where nothing fits (count 60 on a small island). Expected: "Multi-spawn: finding clear space..." appears, then the cancel message within about a second; the game doesn't freeze for several seconds.
5. **Air.** Tick "Spawn in the air", altitude 1000 m ASL, then run twice, once with speed 0 and once with speed 200 m/s. Expected: all copies at the same altitude and heading, spaced by wingspan plus margin; with speed 200 they fly on at about 200 m/s, with speed 0 they use the pilot AI's idle speed. Copies do not collide in the first minute.
6. **Count 1, air off.** Expected: the unchanged stock flow, including the crew chooser dialog (the crew chooser is skipped for multi-spawn counts above 1). The label in the window reads "Count 1 on the surface uses the normal single spawn." and the Spawn button has no `xN` suffix.

### C2. Layout options (use count 6, then 60)

1. Abreast: the line is perpendicular to the heading. Trail: the line is along the heading. Change the heading and confirm the line rotates with it.
2. Spacing auto: spacing follows the craft's size plus margin (the `Debug Spawning` log line prints `spacing X m`). Spacing manual (for example 80 m): the log line shows 80 m and the copies are 80 m apart.
3. Per row: set 3 with count 6: expected 2 rows of 3 (the log line prints `2 rows of 3`). Per row auto with count 60: wraps at about 2 km.
4. Set per row larger than the count: expected a single row.
5. **Count and air are session-only.** Set count 6, tick "Spawn in the air", change the spacing to 80 m, then quit KSP and start it again. Expected: count is back to 1 and "Spawn in the air" is off; spacing is still 80 m (the layout options are saved). `settings.cfg` has no `VESSEL_MOVER_SPAWN_COUNT` or `VESSEL_MOVER_SPAWN_AIR` line written by the new DLL (an old line from an earlier test build is harmless and ignored).

### C3. Warnings and messages

1. Count 51 or more: the label "Warning: N vessels is a large spawn and may slow the game down a lot. It will still go ahead." is shown in the window. When you spawn, a "Large spawn: ..." summary (estimated physics ms per frame, how many copies are outside the physics load range) is shown, and `KSP.log` has a `[BDArmory.VesselMover]: Large spawn: N vessels, +P parts ...` warning line. The spawn still goes ahead.
2. Count 50 shows no warning.
3. The "line moved" / "slots shifted" / "wrapped into rows" / "no clear space" messages: see C1.2, C1.4 and C1.4b.
4. At the end of the spawn the window shows "Multi-spawn finished: S/N spawned" (with ", F failed" if any, and ", wrapped into N rows" when the wrap rule was used).

### C4. Names and performance

1. With a craft that has a weapon manager, spawn 6 copies. Expected: the copies have unique vessel names (for example `Name`, `Name_2`, ...; the exact suffix comes from the normal name deconfliction). Craft without a weapon manager keep the same name.
2. Frame rate with 60: spawns are staggered over frames. Observe the FPS counter while the 60 copies spawn: there are short dips per spawned craft, but the game does not freeze for seconds. After spawning, the frame rate is as low as the added part count dictates (compare with the "est. ms physics per frame" in the message), and the game stays responsive. Note the lowest FPS.

## D. F3: Hold really holds (and Tight)

Setup: one armed vessel A (guns, missiles, and a turret or point-defence weapon if you have one) against one or two enemy targets within gun range, A under AI control with guard mode on. If A has point defence, add an enemy missile-armed craft later (step D6).

### Driving the hooks

There is no UI for the hooks. Either:

- use the test harness below (a small plugin that you compile against the game's DLLs and drop in `<KSP>\GameData\OverdriveHarness\`), or
- use Unity Explorer / a KSP debug console to set `MissileFire.fireAuthority`, call `SetOverrideTarget(TargetInfo, 60f, true)` and so on on the active vessel's primary weapon manager (`FlightGlobals.ActiveVessel.ActiveController().WM`, extension in `BDArmory.Extensions`; with several weapon managers on one craft only the primary one acts on the hooks).

### Optional test harness

Compile this as a class library referencing `Assembly-CSharp.dll`, `UnityEngine*.dll` and `BDArmory.dll` (from `<KSP>\KSP_x64_Data\Managed` and `<KSP>\GameData\BDArmory\Plugins`), and put the resulting DLL in `<KSP>\GameData\OverdriveHarness\Plugins\`. It is a test tool only. Keys act on the active vessel while Right Ctrl is held: Keypad 1 Free, 2 Tight, 3 Hold, 4 sticky 60 s assignment to the nearest enemy, 5 clear the assignment, 6 start a 6-point route.

```csharp
using System.Collections.Generic;
using UnityEngine;
using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Targeting;
using BDArmory.UI;

[KSPAddon(KSPAddon.Startup.Flight, false)]
public class OverdriveHarness : MonoBehaviour
{
    void Start() { MissileFire.OnExternalOverrideLost += OnLost; }
    void OnDestroy() { MissileFire.OnExternalOverrideLost -= OnLost; }

    static void OnLost(MissileFire wm, TargetInfo t, OverrideLostReason r) =>
        Debug.Log($"[OverdriveHarness] {wm.vessel.vesselName} lost external target: {r}");

    void Update()
    {
        if (!Input.GetKey(KeyCode.RightControl)) return;
        var v = FlightGlobals.ActiveVessel;
        var controller = v != null ? v.ActiveController() : null;
        var wm = controller != null ? controller.WM : null;   // the primary weapon manager
        if (wm == null) return;

        if (Input.GetKeyDown(KeyCode.Keypad1)) { wm.fireAuthority = FireAuthority.Free;  Debug.Log("[OverdriveHarness] Free"); }
        if (Input.GetKeyDown(KeyCode.Keypad2)) { wm.fireAuthority = FireAuthority.Tight; Debug.Log("[OverdriveHarness] Tight"); }
        if (Input.GetKeyDown(KeyCode.Keypad3)) { wm.fireAuthority = FireAuthority.Hold;  Debug.Log("[OverdriveHarness] Hold"); }
        if (Input.GetKeyDown(KeyCode.Keypad4))
        {
            TargetInfo best = null; float bestSqr = float.MaxValue;
            foreach (var t in BDATargetManager.TargetList(wm.Team))
            {
                if (t == null || t.isMissile || t.Vessel == null) continue;
                float d = (t.Vessel.CoM - v.CoM).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = t; }
            }
            if (best != null) { wm.SetOverrideTarget(best, 60f, true); Debug.Log($"[OverdriveHarness] sticky 60 s -> {best.Vessel.vesselName}"); }
        }
        if (Input.GetKeyDown(KeyCode.Keypad5)) { wm.ClearExternalOverride(); Debug.Log("[OverdriveHarness] cleared"); }
        if (Input.GetKeyDown(KeyCode.Keypad6))
        {
            var ai = wm.AI as BDGenericAIBase;
            if (ai == null) { Debug.Log("[OverdriveHarness] no AI"); return; }
            var pts = new List<Vector3>();
            for (int i = 0; i < 6; ++i)
                pts.Add(new Vector3((float)(v.latitude + 0.01 * Mathf.Cos(i * Mathf.PI / 3)),
                                    (float)(v.longitude + 0.01 * Mathf.Sin(i * Mathf.PI / 3)),
                                    v.LandedOrSplashed ? 0f : 500f));
            Debug.Log($"[OverdriveHarness] SetWaypoints -> {ai.SetWaypoints(pts, 300f, 2)}");
        }
    }
}
```

The harness needs `KSP.log` for feedback: each action logs a `[OverdriveHarness]` line. The "nearest enemy" in Keypad 4 comes from `BDATargetManager.TargetList(wm.Team)`, the targets known to that team; if A has not detected anyone yet it does nothing.

### Steps

1. Start the scenario, make vessel A the active vessel, guard mode on. Confirm that with `Free` A engages normally (guns fire, missiles launch). Turn on `Debug Weapons` or `On-Screen Telemetry`.
2. **Hold.** Press Right Ctrl + Keypad 3 (or set `fireAuthority = FireAuthority.Hold`). With an enemy in range, expected:
   1. No gun, rocket or laser shots from A (the on-screen weapon debug lines read `Wpn Autofire False (Weapons hold)`).
   2. No missile launches, including parallel salvos and bombs, even though A has a target and a weapon selected.
   3. APS, point-defence guns and interceptor missiles do not fire either (test D6).
   4. Turrets still track the target (they keep aiming and the aim debug lines continue to update); radar and other locks stay.
   5. Guard mode stays on and A keeps selecting and switching targets (`guardTarget` / current target shown in the weapon manager debug).
   6. A gun burst that had already started may finish its last rounds; that is expected.
3. **Back to Free.** Press Keypad 1. Expected: firing resumes within a second or two (the autofire label goes back to `Wpn Autofire True` and missiles launch again).
4. **Tight, sticky assignment.** With two enemies in range (E1 nearer, E2 farther), press Keypad 2 (Tight) and then Keypad 4 (sticky assignment to the nearest enemy, E1). Expected:
   1. A shoots E1 only. The `Wpn Autofire False (Weapons tight)` label appears for any weapon whose current visual target is not E1.
   2. E2 is never shot or targeted by A, even when E1 is out of A's weapon envelopes. (With Tight and no filter set, A's target selection excludes unassigned vessels.) The AI keeps closing on E1 while it is sticky and not engageable.
5. Clear the assignment (Keypad 5) while still Tight: A stops selecting any vessel target (no filter and no assignment) and stops firing at vessels. `KSP.log` shows `OnExternalOverrideLost` with reason `Cleared`.
6. **Incoming missiles are still intercepted under Tight.** Under Tight with A having point defence or an interceptor, fire a missile at A from an enemy. Expected: A's point-defence guns still engage the missile, and interceptor missiles are still launched. Then repeat under Hold: no interception at all (A will be hit; this is the expected cost of Hold).
7. Set Free again. Expected: all behaviour back to normal.

## E. F1: external target assignments

Run these with A on guard, `Debug AI` on, the harness (or Unity Explorer) active, and `OnExternalOverrideLost` logged.

1. **Survives a missile salvo.** A has missiles and guns and `Max Turret Targets` 1. Press Keypad 4 to assign the nearest enemy E1 sticky for 60 s. Let A launch a salvo at E1 (fire authority Free). Expected: the assignment is still E1 after the launch (A does not drop back to its own choice; `HasExternalOverride` stays true and no `OnExternalOverrideLost` line is logged during the salvo).
2. **Team assist does not steal it.** Put A on a team with a friendly craft B near it, and enemy E1 and E2 on the other team. Assign E1 to A (Keypad 4). Make E2 threaten A (for example E2 shoots at A, so B's team-assist runs). Expected: with `Debug AI`, `KSP.log` shows `called for help` lines for B, but A keeps E1; A's `TeamAssistOverridesBlocked` increases (inspect with Unity Explorer). A is not swapped to E2.
3. **Expiry.** Assign E1 with a short duration (use Unity Explorer: `wm.SetOverrideTarget(e1, 5f, true)`; the harness uses 60 s so wait it out if you prefer). Expected: after about 5 s of game time `KSP.log` has `[OverdriveHarness] ... lost external target: Expired` (and, with `Debug AI`, `[BDArmory.MissileFire]: A lost its external target assignment (Expired).`).
4. **Duration runs with guard mode off.** Assign E1 for 20 s and turn guard mode off straight away. Expected: `Expired` is logged about 20 s later even though guard mode is off. Assign again for 60 s, turn guard off for 10 s and back on: A goes back to E1.
5. **Target destroyed.** Assign E1 sticky, then destroy E1 (shoot it down or use the debug kill tools). Expected: `OnExternalOverrideLost` with `TargetDestroyed` is logged, and A returns to normal target selection.
6. **Replaced.** Assign E1, then assign E2 (Keypad 4 after moving closer to E2, or Unity Explorer). Expected: `Replaced` is logged for E1. Using the stock BDA target-lock key on A's weapon manager (manual `SetOverrideTarget` via GUI/AI) while an external assignment is live also logs `Replaced`.
7. **Non-sticky, not engageable.** In Unity Explorer, `wm.SetOverrideTarget(farEnemy, 60f, false)` with an enemy beyond every weapon's range. Expected: dropped at the next scan (within about 1 s) with `NotEngageable`. Then repeat with `sticky = true`: the assignment stays and A's AI keeps closing in on it; nothing is logged.
8. **Filter.** Set `wm.ExternalTargetFilter = (mf, t) => false;` after assigning E1 (Unity Explorer). Expected: at the next scan the assignment is dropped with `Filtered`. Clear the filter again afterwards.
8a. **Team change.** Assign E1 sticky, then move E1 to A's team (the team selector of E1's weapon manager). Expected: `OnExternalOverrideLost` with `TargetDestroyed` (the team change recreates E1's `TargetInfo`), and A never launches at E1 afterwards. Then assign E2, and move A itself to E2's team: `NotHostile` at the next scan (within about 1 s) and no further shots at E2.
8b. **Team assist respects Tight.** With A on Tight and no assignment, repeat test 2 (B calls for help against E2). Expected: A does not engage E2 (the assist override is dropped at the scan because E2 is filtered out under Tight).
9. After each case, check `KSP.log` for exceptions.

## F. F6: custom route quick check (optional)

1. Use a surface ship or a pilot-AI aircraft that is active (AI enabled, `pilotEnabled` true) and is the active vessel.
2. Press Right Ctrl + Keypad 6 (harness). It calls `SetWaypoints` with 6 points on a circle of about 1 km, radius 300 m, 2 laps. Expected: `[OverdriveHarness] SetWaypoints -> True`, and the vessel starts sailing/flying the route.
3. Watch the AI status in the AI GUI: it shows the lap (laps are shown because there are 2) and the current gate. After 12 gate passes the vessel stops following the route, and `IsCustomWaypointRoute` is false.
4. No competition scoring or culling: the score window does not register waypoint gates, and the vessel is not culled for being slow.
5. Disable the AI first and press Keypad 6 again. Expected: `SetWaypoints -> False` (the AI is not active).
6. Run the stock BDA waypoint course afterwards (section B6) to check the two systems do not interfere.
7. While a custom route is running, disable the AI and call `SetWaypoints` with a different route. Expected: `False`, and on re-enabling the AI the original route state is unchanged.
8. **Single-point route.** In Unity Explorer call `ai.SetWaypoints(onePointList, 300f, 3)` with one point ahead of a ship. Expected: lap 1 is counted when the ship passes the point, lap 2 only after it has left the 300 m radius and come back (it does not run through all 3 laps in one pass), and the route completes after the third pass.
9. **Handler replaces the route.** Subscribe an `OnCustomWaypointReached` handler that calls `ai.ClearWaypoints()` (or `SetWaypoints` with a new route) on gate 2. Expected: no exception, the route stops (or the new route starts at its gate 0) and no `OnCustomWaypointRouteCompleted` is raised for the old route.

## G. F6b: patrol engage (pilot AI)

Setup: a pilot-AI fighter A (guns, `Debug AI` on) flying a custom route (Keypad 6) or a stock course, guard mode on, team 1. Enemy E1 (and E2 for test 5) on team 2, placed so they cross the route. In Unity Explorer, select A's `BDModulePilotAI` and log `BDGenericAIBase.OnPatrolEngage` / `OnPatrolResume` (or watch `PatrolEngaged`).

1. **Stock with the flag off.** `waypointsYieldToCombat = false` (default). Expected: A keeps flying the route and does not chase E1 (stock pilot-AI behaviour), `PatrolEngaged` stays false.
2. **Engage, then resume at the same waypoint.** Set `waypointsYieldToCombat = true`, fire authority Free. Note `CurrentWaypointIndex` when E1 is detected. Expected: A breaks off and attacks E1 (`OnPatrolEngage` logged; `breaking off its waypoints` in `KSP.log`). `CurrentWaypointIndex` does not change while engaged. Kill E1 (or let A kill it). Expected: `OnPatrolResume(..., false)` and A heads back to the same waypoint index it had before, then continues the route and lap count normally.
3. **Leash breaks off the chase.** Set `patrolLeashDistance = 3000` and let a faster E1 run away from the route. Expected: `PatrolLeashAnchor` is the position where A left its route; when A is more than 3 km from that point (not from the next waypoint), `OnPatrolResume(..., true)` (`broke off the chase ... patrol leash` in `KSP.log`) and A returns to the route. A does not re-engage that E1 for 30 s (`patrolLeashCooldown`). If E1 was A's external assignment, `OnExternalOverrideLost` with `Cleared` is logged.
4. **Hold stays on the pattern.** Set `wm.fireAuthority = Hold` with the flag on. Expected: A never breaks off its route for E1 and fires nothing; `PatrolEngaged` stays false. Switch back to Free: A engages.
5. **Tight stay-on-task attacker ignores crossing hostiles.** Fire authority Tight, assign E1 sticky (Keypad 4 or `SetOverrideTarget(e1, 0, true)`), flag on, and fly E2 across A's path closer than E1. Expected: A engages E1 only, never targets or fires at E2 (not even through team assist from a friendly), and resumes the route when E1 dies. Repeat with a filter `wm.ExternalTargetFilter = (mf, t) => t.Vessel == e1Vessel;` instead of the assignment: same result.
5b. **Out of ammo stays on the route.** Fly A with nearly empty guns (or no weapons), flag on, Free. Expected: once A is out of ammo it resumes its route (`OnPatrolResume(..., false)`) and keeps passing gates; it does not ram or orbit E1 with `CurrentWaypointIndex` frozen.
5c. **Another command ends the engagement.** While A is engaged (`PatrolEngaged` true), give it another order (a wingman command such as follow or attack position from a `ModuleWingCommander`, or release), or switch the AI off. Expected: `OnPatrolResume(..., false)` at once and `PatrolEngaged` false; turning the AI back on doesn't leave a stale engagement.
6. **Surface and VTOL AIs unchanged.** Repeat test 2 with a ship: it engages E1 whether the flag is set or not (stock target-first behaviour), and the leash has no effect.
7. Check `KSP.log` for exceptions.

## H. F9 and F10 events (optional)

1. Subscribe a logger to `MissileFire.OnMissileFired`. Let A launch a missile in guard mode and fire one manually. Expected: one log line per launch with the right missile and target, and no line when a launch is refused.
2. In Unity Explorer, `wm.FireMissileAt(someMissileOnA, e1Vessel)`. Expected: `True` and a launch at E1; repeat with Hold and guard on: `False`, no launch. With a rotary launcher, call it on a missile that is not the one in the firing position: `False`, no launch and no `OnMissileFired` line.
3. Subscribe to `CompetitionScores.OnHit`, `OnDeath`, `BDACompetitionMode.OnCompetitionStarted`/`OnCompetitionEnded`, `BDATargetManager.OnTargetDetected` and `OnTargetLost`. Run a short competition. Expected: started/ended once each, hit lines as damage is scored, one death line per kill, detection lines as teams spot each other, and lost lines when a vessel dies or (within about 5 s) when a contact stops being a threat. With nothing subscribed, the competition behaves exactly as before (section B).
4. **Ended only after started.** With the loggers subscribed, start a competition and cancel it during the start sequence, or start one that fails (one team only), then leave the flight scene. Expected: no `OnCompetitionEnded` line for any of these (no `OnCompetitionStarted` came first).
