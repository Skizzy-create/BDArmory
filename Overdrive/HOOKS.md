# BDArmory Overdrive: integration hooks

Public API reference for plugins that reference `BDArmory.dll` (for example the Skizzy adapter). Everything here was read from the code on branch `overdrive-hooks` (commits `ec38cf761`..`b1a9446e4`). Names and signatures are exact; behaviour is what the code does, including its quirks.

## 1. Overview

The Overdrive hooks are small, additive extension points in BDArmory Plus: delegates, enums, events and read-only accessors that let an external controller assign targets, restrict fire, read radar contacts and fly custom routes.

- **Inert by default.** Every delegate is `null`, `fireAuthority` is `FireAuthority.Free`, and no external override exists. In that state each code path behaves as stock BDArmory. The cost of an unused hook is one null or enum check.
- **Drop-in.** Same assembly (`BDArmory`, `BDArmory.dll`), same namespaces, same `GameData\BDArmory` folder as BDArmory Plus. Nothing else has to change.
- **Threading.** Main thread only. Call hooks from `Update`/`FixedUpdate`/coroutines. All delegates and events are invoked on the main thread inside BDArmory's guard mode, target scan or AI update. Nothing here is thread-safe.
- **Exceptions.** Delegates and event handlers are called inside `try/catch`; an exception is logged and treated as described per hook. Don't rely on that.

### Detecting the fork at runtime

Don't reference the new members directly from code that must also load on stock BDA (a missing member throws `MissingMethodException`/`TypeLoadException` when the calling method is JIT-compiled). Probe by reflection and keep the typed calls in a separate class that is only touched when the probe says yes. Cheapest single check: the field `MissileFire.fireAuthority`, or the 3-argument `MissileFire.SetOverrideTarget(TargetInfo, float, bool)`.

The `bda.hooks` capability should report availability per hook this way:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class OverdriveProbe
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Per-hook availability. Safe on stock BDA Plus and when BDArmory is missing.</summary>
    public static Dictionary<string, bool> Probe()
    {
        var r = new Dictionary<string, bool>();
        var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BDArmory");
        Type T(string n) => asm?.GetType(n, false);
        var mf = T("BDArmory.Control.MissileFire");
        var ti = T("BDArmory.Targeting.TargetInfo");
        var vrd = T("BDArmory.Radar.VesselRadarData");
        var ai = T("BDArmory.Control.BDGenericAIBase");
        var settings = T("BDArmory.Settings.BDArmorySettings");
        r["F1"] = mf?.GetMethod("SetOverrideTarget", All, null, new[] { ti, typeof(float), typeof(bool) }, null) != null
               && mf.GetEvent("OnExternalOverrideLost", All) != null;
        r["F2"] = mf?.GetField("ExternalSecondaryTargets", All) != null;
        r["F3"] = mf?.GetField("fireAuthority", All) != null;
        r["F4"] = mf?.GetField("ExternalTargetFilter", All) != null;
        r["F5"] = vrd?.GetProperty("DisplayedContacts", All) != null;
        r["F6"] = ai?.GetMethods(All).Any(m => m.Name == "SetWaypoints" && m.GetParameters().Length == 6) == true;
        r["F12"] = mf?.GetProperty("LastScan", All) != null;
        r["OD1"] = settings?.GetField("VESSEL_MOVER_SPAWN_COUNT", All) != null;
        return r;
    }
}
```

On stock BDA Plus every entry is `false` (and `F5` etc. cannot be used at all); fall back to the stock `SetOverrideTarget(TargetInfo)`, reflection on `VesselRadarData`'s private lists, and so on.

## 2. Hook index

| Id | Status | Members |
|---|---|---|
| F1 | Done | `MissileFire.SetOverrideTarget(TargetInfo, float, bool)`, `ClearExternalOverride()`, `OnExternalOverrideLost`, `HasExternalOverride`, `ExternalOverrideTarget`, `ExternalOverrideSticky`, `ExternalOverrideTimeRemaining`, `ExternalOverrideEngageable`, `LastOverrideLostReason`, `LastOverrideLostTarget`, `LastOverrideLostTime`, `TeamAssistOverridesBlocked`, `IsExternallyAssigned(TargetInfo)`, `TargetOverride`; enum `OverrideLostReason` |
| F2 | Done | `MissileFire.ExternalSecondaryTargets`, `ExternalSecondaryTargetsOnly`, `ExternalSecondarySnapshot` |
| F3 | Done | `MissileFire.fireAuthority`, `FireAuthorityPermits(TargetInfo)`, `FireAuthorityPermits(Vessel, bool)`; enum `FireAuthority`; `ModuleWeapon` AI autofire gate |
| F4 | Done | `MissileFire.ExternalTargetFilter`, `IsTargetFilteredOut(TargetInfo)`, `IsTargetExcluded(TargetInfo)`; `BDATargetManager` query changes |
| F5 | Done | `VesselRadarData.DisplayedContacts`, `DisplayedIRContacts`, `ActiveLockedContactIndex` |
| F6 | Done | `BDGenericAIBase.SetWaypoints(List<Vector3>, float, int, IList<float>, bool, IList<float>)`, `IsCustomWaypointRoute`, `CurrentWaypointLap`, `WaypointLapLimit`, `OnCustomWaypointReached`, `OnCustomWaypointRouteCompleted`, `DefaultCustomWaypointRadius` |
| F6b | Done (pilot AI) | `BDGenericAIBase.waypointsYieldToCombat`, `patrolLeashDistance`, `patrolAnchor`, `patrolLeashCooldown`, `PatrolEngaged`, `PatrolTarget`, `OnPatrolEngage`, `OnPatrolResume` (section 8b) |
| F8 | Not done | See section 10 |
| F9 | Done | `MissileFire.FireMissileAt(MissileBase, Vessel, bool)`, `MissileFire.OnMissileFired` |
| F10 | Done (partial target-lost coverage) | `CompetitionScores.OnHit`, `OnDeath`; `BDACompetitionMode.OnCompetitionStarted`, `OnCompetitionEnded`; `BDATargetManager.OnTargetDetected`, `OnTargetLost` |
| F11 | Not done | See section 10 |
| F12 | Done | `MissileFire.LastScan` |
| OD1 | Done | Multi-spawn in the Vessel Mover; `BDArmorySettings.VESSEL_MOVER_SPAWN_*` |

Namespaces: `BDArmory.Control` (`MissileFire`, `BDGenericAIBase`, `FireAuthority`, `OverrideLostReason`, `OverrideScanOutcome`, `FireAuthorityRules`), `BDArmory.Targeting` (`TargetInfo`), `BDArmory.UI` (`BDATargetManager`, `LoadedVesselSwitcher`), `BDArmory.Radar` (`VesselRadarData`, `RadarDisplayData`, `IRSTDisplayData`, `ViewScanResults`, `IncomingMissile`), `BDArmory.Competition` (`BDTeam`), `BDArmory.Settings` (`BDArmorySettings`), `BDArmory.VesselSpawning` (`VesselMover`, `MultiSpawnLayout`).

Getting the objects: `vessel.FindPartModuleImplementing<MissileFire>()` for the weapon manager; `wm.AI as BDGenericAIBase` (or `vessel.FindPartModuleImplementing<BDModulePilotAI>()` etc.) for the AI; `vessel.GetComponent<TargetInfo>()` for a vessel's target record (or iterate `BDATargetManager.TargetList(wm.Team)`, the targets known to that team).

## 3. F1: external target assignment

### Members (`BDArmory.Control.MissileFire` unless noted)

```csharp
public void SetOverrideTarget(TargetInfo target, float durationS, bool sticky)
public void ClearExternalOverride()
public static event Action<MissileFire, TargetInfo, OverrideLostReason> OnExternalOverrideLost;

public bool        HasExternalOverride { get; }          // externalOverride && overrideTarget != null
public TargetInfo  ExternalOverrideTarget { get; }       // the live external assignment, or null
public bool        ExternalOverrideSticky { get; }
public float       ExternalOverrideTimeRemaining { get; } // +Infinity until cleared, 0 when none
public bool        ExternalOverrideEngageable { get; private set; }
public OverrideLostReason LastOverrideLostReason { get; private set; }
public TargetInfo  LastOverrideLostTarget { get; private set; }  // may be destroyed (Unity-null)
public float       LastOverrideLostTime { get; private set; }    // Time.time
public int         TeamAssistOverridesBlocked { get; private set; }
public bool        IsExternallyAssigned(TargetInfo target)       // live assignment or in the F2 snapshot
public bool        TargetOverride { get; }                       // stock getter: overrideTimer > 0

// BDArmory.Control
public enum OverrideLostReason { None = 0, Expired, Cleared, Replaced, TargetDestroyed, NotEngageable, Filtered }
```

The stock `public void SetOverrideTarget(TargetInfo target)` is unchanged in signature.

### Semantics

- **Duration.** `durationS` is converted by `FireAuthorityRules.ExternalOverrideTimer`: a positive finite value is used as is; `<= 0` or `NaN` becomes `+Infinity` (until cleared).
- **The duration is game time.** The expiry is stored as `Time.time + durationS` and checked every `FixedUpdate` of the weapon manager, whether guard mode is on or not (guard off, not controllable, `PEACE_MODE`, GuardMode early returns: the clock keeps running). Target loss (`TargetDestroyed`) is checked at the same place. An assignment can therefore expire, and raise `Expired`, while guard mode is off.
- **Survives missile routines.** The timer is set when the assignment is made, so the stock "null the override at the end of GuardMode when `overrideTimer <= 0`" does not apply, even while `guardFiringMissile` skips target scans.
- **Takes effect at the next guard tick.** `SetOverrideTarget` forces a target scan (`targetScanTimer = -100`). The assignment is stored even when guard mode is off and acted on once it is on. No team or hostility check is made on the target: that is the caller's job.
- **Priority.** At each scan the assignment is examined first, before missile threats and the normal queries. It also bypasses the stock early returns "keep current target, about to enter gun range" and "max targets fired on with LASMs, keep target painted", which would otherwise keep the old target.
- **`TargetOverride` stays `true`** while an external assignment is live (the timer is above zero or infinite). `BDModulePilotAI` checks it (`BDModulePilotAI.cs` around `weaponManager.TargetOverride`, "Target is overridden, follow others' instructions") and so does not extend away from the target on its own.
- **Engageable vs sticky.** At each scan `SmartPickWeapon_EngagementEnvelope(target)` decides whether any weapon can engage it.
  - Engageable: engaged; `ExternalOverrideEngageable = true`; the external duration is not touched (the stock 15 s refresh is not applied).
  - Not engageable and **sticky**: the target is kept as `currentTarget` (the AI keeps closing on it), `selectedWeapon` is cleared so nothing fires, `ExternalOverrideEngageable = false`.
  - Not engageable and **not sticky**: dropped at that scan with `NotEngageable`, then normal target selection runs in the same scan.
- **Filter.** If `ExternalTargetFilter` rejects the (non-missile) target at a scan, the assignment is dropped with `Filtered` before the engagement check. Missile targets are never filtered. The check is made at scan time only, so a rejected assignment can survive until the next scan (default every 1 s). The same check applies to stock overrides (manual lock, team assist): a stock override on a target that `IsTargetFilteredOut` rejects (filter, or Tight without a filter and not assigned) is dropped at the scan instead of engaged, so team assist cannot pull a Tight or filtered unit off task.
- **Team assist.** BDArmory's own team assist (`UpdateGuardViewScan` "called for help") goes through a private `SetAssistOverrideTarget`, which does nothing while an external assignment is live and increments `TeamAssistOverridesBlocked`. It still works normally on a weapon manager without an external assignment.
- **Stock one-argument call.** Anyone calling the stock `SetOverrideTarget(TargetInfo)` (including with `null`, or with the same target) ends a live external assignment with `Replaced`, then applies the stock override (which has no timer of its own; the stock 15 s refresh rules apply).

### How loss is reported

`OnExternalOverrideLost(wm, lostTarget, reason)` is raised from `EndExternalOverride`, after the state is cleared and after `LastOverrideLost*` are set (so the handler may call `SetOverrideTarget` again). It is raised for external assignments only, never for stock/team-assist overrides. A handler exception is caught and logged with `Debug.LogError`. With `DEBUG_AI` on, each loss is also logged: `[BDArmory.MissileFire]: <vessel> lost its external target assignment (<reason>).`

| Reason | Raised when |
|---|---|
| `Expired` | The game-time expiry passes (only finite durations); checked every `FixedUpdate`, guard mode on or off. |
| `Cleared` | `ClearExternalOverride()` with a live assignment, or `SetOverrideTarget(null, ...)`. No event if there is no live assignment. |
| `Replaced` | `SetOverrideTarget(otherTarget, d, s)` while a different external assignment is live, or the stock `SetOverrideTarget(TargetInfo)` while any external assignment is live. Re-assigning the *same* target with the 3-argument overload raises nothing: it just refreshes duration and stickiness. |
| `TargetDestroyed` | `overrideTarget` is Unity-null (vessel died or the `TargetInfo` was destroyed). Checked every `FixedUpdate` and at the start of each override check in the scan. |
| `NotEngageable` | Non-sticky assignment, no weapon can engage the target at a scan. |
| `Filtered` | `ExternalTargetFilter` rejects the target at a scan. |

Not reported: destruction of the weapon manager's own vessel. Switching guard mode off does not end the assignment (it keeps its clock and is acted on when guard mode comes back); call `ClearExternalOverride()` yourself if you want it gone.

State after a loss: `LastOverrideLostReason`, `LastOverrideLostTarget`, `LastOverrideLostTime` describe the most recent loss (they are not reset by a new assignment); `HasExternalOverride` is false.

### Timer details

While an external assignment is live, the per-`FixedUpdate` check keeps the stock `overrideTimer` equal to the remaining time, so the stock decay at the end of `GuardMode()` (unchanged) never drops it, and `TargetOverride` stays true. The duration is accurate to one physics step.

## 4. F2: external secondary targets

```csharp
public Func<MissileFire, IList<TargetInfo>> ExternalSecondaryTargets;   // default null
public bool ExternalSecondaryTargetsOnly = false;
public IReadOnlyList<TargetInfo> ExternalSecondarySnapshot { get; }     // last snapshot, priority order
```

- **When called.** Once per target scan, at the start of `SmartFindTarget()`, whenever the delegate is set (also after it was set and then cleared, to empty the snapshot), even for weapon managers with `Max Turret Targets` and `Max Missile Tgts` both 1. In that single-target case the list is still snapshotted (it feeds `IsExternallyAssigned` and Tight authority) but no secondary assignment happens.
- **Secondary assignment** only happens when `multiTargetNum > 1 || multiMissileTgtNum > 1` and there is a primary target, in `SmartFindSecondaryTargets()`.
  - External entries come first, in the order returned (index 0 is highest priority), then BDArmory's own picks fill what is left.
  - Caps are the same as stock: vessels up to `max(Max Turret Targets, Max Missile Tgts)`; missile targets only if `targetMissiles` is on and `Max Turret Targets > 1`, up to `Max Turret Targets`. Entries over the cap are skipped silently.
  - Skipped entries: `null`, no `Vessel`, the weapon manager's own vessel, duplicates, anything the primary already is, and anything `IsTargetFilteredOut` rejects (the filter wins).
  - Hostility and visibility are **not** checked on external entries.
- **`ExternalSecondaryTargetsOnly`** = true suppresses BDArmory's own picks, but only at scans where the delegate actually returned a list (an empty list counts). If the delegate is null, returns `null` or throws, BDArmory's own picks are used as normal for that scan.
- **The list is copied** into an internal snapshot every scan and never stored, so you can return one reusable pre-allocated `List<TargetInfo>`.
- **Exceptions** in the delegate are caught and logged once per weapon manager (`ExternalSecondaryTargets threw (stock picks used; logged once per weapon manager)`); the scan proceeds with no external entries and the stock picks.
- **Fire authority.** Under `Tight`, snapshot members count as assigned (authorised) even when they did not make it into the capped secondary list.

## 5. F3: fire authority

```csharp
// BDArmory.Control
public enum FireAuthority { Free = 0, Tight = 1, Hold = 2 }

// MissileFire
public FireAuthority fireAuthority = FireAuthority.Free;
public bool FireAuthorityPermits(TargetInfo target)
public bool FireAuthorityPermits(Vessel targetVessel, bool isProjectile = false)
```

| Mode | Behaviour |
|---|---|
| `Free` | Stock. Nothing is gated. |
| `Tight` | Fires only at authorised targets: missiles and, for point defence, shells/rockets (self-defence); vessels that are assigned (the F1 external assignment or an F2 snapshot member); and vessels that pass `ExternalTargetFilter`. A filter that rejects a vessel wins over an assignment. With **no** filter set, unassigned vessels are also removed from target selection (see F4), so the weapon manager only ever picks assigned targets and incoming missiles. A fire request with no resolvable target vessel is refused. |
| `Hold` | Keeps scanning, selecting targets, selecting weapons, locking radar/heat/lasers, and turrets keep aiming. Nothing fires: guns, rockets, lasers, missiles, torpedoes, bombs, APS/point-defence guns and interceptor missiles. |

### Gated code paths

- `ModuleWeapon.CheckAIAutofire` (end of the method, `ModuleWeapon.cs`, tag `Overdrive F3`): for AI-controlled weapons (`autoFire && aiControlled`) of a weapon manager that is not `Free`, `autoFire` is forced `false` and `autoFireFailReason` is set to `"Weapons hold"` or `"Weapons tight"`. The target vessel comes from `FireAuthorityTargetVessel` (slaved target, targeting-pod lock, else `visualTargetVessel` / `lastVisualTargetVessel`). `isProjectile` is true only for APS weapons engaging a shell or rocket. This gate applies **whether or not guard mode is on**, but only to AI-controlled fire, not to a human holding the trigger.
- `MissileFire.GuardMode`, missile branch: under `Hold` neither a normal launch nor a parallel salvo (`GuardParallelSalvoRoutine`) is started.
- `MissileFire.GuardMode`, bomb branch: `GuardFireBlocked(guardTarget)` prevents `GuardBombRoutine`.
- `MissileFire.GuardMissileLaunchAuthorized`: returns false unless `FireAuthorityPermits(target)` (used by the normal path and per target in the parallel salvo).
- `MissileFire.FireCurrentMissile` and `FireParallelMissile`: backstops, `GuardFireBlocked(targetVessel ?? guardTarget)`. These cover interceptor/point-defence missiles (fired through `GuardMissileRoutine` to `FireCurrentMissile`) and the manual fire-missile action while guard mode is on. With guard mode off these backstops do nothing (`GuardFireBlocked` requires `guardMode`).
- Target selection under `Tight` (F4).

`autoFireFailReason` is visible in the weapon debug labels (`Wpn Autofire False (Weapons hold)`) when `DEBUG_WEAPONS` or `DEBUG_TELEMETRY` is on.

### Edge cases

- A gun burst already in progress (`BurstFire` weapons with `RoundsRemaining > 0 && RoundsRemaining < RoundsPerMag`) finishes its remaining rounds: `ModuleWeapon.fireConditionCheck` continues the burst independently of `autoFire`.
- Missiles already in flight are unaffected; missiles keep their locks and guidance.
- Under `Tight`, a missile launch via `FireCurrentMissile` with no `targetVessel` argument and no `guardTarget` is refused.
- Switching to `Free` takes effect on the next autofire evaluation / guard tick; no state needs resetting.
- Not gated: `ModuleWeapon`s that are not `aiControlled`, and guard-mode-off missile launches (including manual ones).

## 6. F4: external target filter

```csharp
public Func<MissileFire, TargetInfo, bool> ExternalTargetFilter;   // return false to stop this WM selecting the target
public bool IsTargetFilteredOut(TargetInfo target)                 // filter, or Tight authority without a filter
public bool IsTargetExcluded(TargetInfo target)                    // now includes IsTargetFilteredOut
```

- Consulted for **vessel targets only**. Missiles (`TargetInfo.isMissile`) are never filtered, so point defence always works.
- Where it applies:
  - `MissileFire.IsTargetExcluded`, used by the `BDATargetManager` queries `GetAirToAirTarget`, `GetClosestTarget`, `GetLeastEngagedTarget`, `GetClosestTargetWithBiasAndHysteresis`, `GetHighestPriorityTarget` and the other queries that already called `IsTargetExcluded` (missile targets pass the filter, see above);
  - directly in `GetAirToAirTargetAbortExtend`, `GetAllTargetsExcluding` and `GetPrioritizedVesselTargets` (the last feeds the stock secondary-target picks);
  - the override check in `SmartFindTargetInternal`, for external assignments (reason `Filtered`) and stock/team-assist overrides alike;
  - the F2 external secondary list;
  - the AI's `GetGuardNonTarget` (through the queries above).
- Under `Tight` **with no filter**, `IsTargetFilteredOut` rejects every vessel that is not externally assigned. With a filter, the filter alone decides.
- **Exceptions** thrown by the delegate are treated as a pass, and logged once per weapon manager: `ExternalTargetFilter threw (treated as a pass; logged once per weapon manager)`.
- **Performance.** The delegate runs on the main thread, potentially several times per candidate target per scan (every weapon manager, every `targetScanInterval`). Keep it a few dictionary/hash-set lookups, allocation-free, with no KSP API calls that are slow or throw. Precompute zone/ROE membership elsewhere and look it up.
- A filter rejection does not remove an already-selected primary target until the next scan, except for external assignments (checked each scan, see F1).

## 7. F5: radar contact lists

```csharp
// BDArmory.Radar.VesselRadarData
public IReadOnlyList<RadarDisplayData> DisplayedContacts { get; }
public IReadOnlyList<IRSTDisplayData>  DisplayedIRContacts { get; }
public int ActiveLockedContactIndex { get; }   // index into DisplayedContacts, or -1

public struct RadarDisplayData { Vessel vessel; Vector2 pingPosition; bool locked; ModuleRadar detectedByRadar; TargetSignatureData targetData; float signalPersistTime; float velAngle; int jammedIndex; }
public struct IRSTDisplayData  { Vessel vessel; Vector2 pingPosition; float magnitude; ModuleIRST detectedByIRST; TargetSignatureData targetData; float signalPersistTime; }   // fields are public
```

- `DisplayedContacts` holds the radar and sonar contacts on the vessel's display, including datalinked contacts and locked contacts. `DisplayedIRContacts` holds IRST contacts.
- **Live lists, not copies.** Read on the main thread, don't keep across frames, don't mutate. They are `Array.Empty` before the radar data initialises. The structs are copied by value when you index.
- `ActiveLockedContactIndex` is `-1` when `locked` is false or no lock exists, otherwise the index of the active lock in `DisplayedContacts`.
- Obtain the object with `wm.vesselRadarData` (public field, may be null when the vessel has no radar/IRST) or `vessel.GetComponent<VesselRadarData>()`.
- No allocations on access.

## 8. F6: custom waypoint routes

```csharp
// BDArmory.Control.BDGenericAIBase (base of BDModulePilotAI, BDModuleSurfaceAI, BDModuleVTOLAI)
public bool SetWaypoints(List<Vector3> points, float radiusM, int laps,
                         IList<float> maxSpeeds = null, bool guardAtEnd = false, IList<float> radii = null)

public bool IsCustomWaypointRoute { get; }
public int  CurrentWaypointLap { get; }        // 1-based
public int  WaypointLapLimit { get; }
public const float DefaultCustomWaypointRadius = 500f;
public static event System.Action<BDGenericAIBase, int, int> OnCustomWaypointReached;      // (AI, waypoint index, lap)
public static event System.Action<BDGenericAIBase>           OnCustomWaypointRouteCompleted;
```

Existing members still apply: `ClearWaypoints()`, `IsRunningWaypoints`, `CurrentWaypointIndex`, and the stock `SetWaypoints(List<Vector3>)`.

- **Points** are `Vector3(latitude, longitude, altitude above terrain in metres)`. The list is copied. Any number of points.
- **Independent of the stock waypoint system:** `WaypointCourses`, `WAYPOINT_COURSE_INDEX`, `WAYPOINT_LOOP_INDEX`, `WAYPOINT_GUARD_INDEX` and `WAYPOINTS_SCALE` are not read for a custom route.
- `radiusM`: gate radius for points without their own radius; `<= 0` means 500 m. `radii`: optional per-point radius; a missing entry or an entry `<= 0` falls back to `radiusM`.
- `laps`: clamped to at least 1.
- `maxSpeeds`: optional per-point speed limit (m/s) applied to the AI's max speed (`BDModulePilotAI.maxSpeed` / `BDModuleSurfaceAI.MaxSpeed` / `BDModuleVTOLAI.MaxSpeed`) when heading for that point. An entry `<= 0` or missing means the AI's own max speed. The AI's max speed at the start of the route is saved and **restored** when the route ends (completion, `ClearWaypoints()`, or a new `SetWaypoints`), but only if `maxSpeeds` was non-empty.
- **Gate reached:** same geometry as stock (inside the radius and at closest approach this physics step). Raises `OnCustomWaypointReached(ai, index, lap)` (index and lap of the gate just passed), then moves on. Raising is wrapped in `try/catch`.
- **Completion** (after the last gate of the last lap): `waypoints` cleared, speed restored, command released, then if `guardAtEnd` and the vessel has a weapon manager with guard mode off, guard mode is switched on; finally `OnCustomWaypointRouteCompleted(ai)` is raised. The last gate also raises `OnCustomWaypointReached` first.
- **Not a competition run:** no `Scores.RegisterWaypointReached`, no waypoint-course scoring, and `BDACompetitionMode.CullSlowWaypointRunners` skips custom routes. `WAYPOINT_GUARD_INDEX` is ignored (use `guardAtEnd`). The AI status line shows laps only when `laps > 1` and no course gate name.
- **Return value.** `false` if `points` is null/empty (this also calls `ClearWaypoints()`), or if the AI is not active (`pilotEnabled` is false). In the `pilotEnabled == false` case nothing is changed (any running route continues); call `SetWaypoints` again once the AI is active.
- Calling the stock `SetWaypoints(List<Vector3>)` (with any list, including null/empty) or `ClearWaypoints()` ends a custom route.
- Replaces any running route (custom or course) on that AI. Per-AI state, so many AIs can run different routes at once.

## 8b. F6b: patrol engage

```csharp
// BDArmory.Control.BDGenericAIBase
public bool    waypointsYieldToCombat = false;   // break off waypoints to attack the guard target
public float   patrolLeashDistance = 0f;         // metres from the anchor while engaging; 0 = unlimited
public Vector3d patrolAnchor = Vector3d.zero;    // (latitude, longitude, altitude ASL); zero = the current waypoint
public float   patrolLeashCooldown = 30f;        // seconds a leash-broken target is ignored for patrol engagement
public bool    PatrolEngaged { get; }
public Vessel  PatrolTarget { get; }
public static event System.Action<BDGenericAIBase, Vessel> OnPatrolEngage;  // (AI, target)
public static event System.Action<BDGenericAIBase, bool>   OnPatrolResume;  // (AI, leash broke the chase)
```

**Which AIs.** Only `BDModulePilotAI` changes. Stock, its waypoint branch comes before its attack branch, so a pilot-AI aircraft on waypoints ignores enemies. `BDModuleSurfaceAI` and `BDModuleVTOLAI` already attack a guard target before following waypoints (stock); for them the fields have no effect and there is no leash. The orbital AI has no waypoints. Works with both F6 custom routes and stock courses.

**Behaviour** (pilot AI, `waypointsYieldToCombat = true`, waypoints running, guard mode on, not `PEACE_MODE`):

- When the weapon manager has a target and fire authority allows it, the AI skips its waypoint steering and runs its normal attack logic. `PatrolEngaged` becomes true and `OnPatrolEngage(ai, target)` is raised (again if the target changes).
- While engaged the waypoint state (index, lap) is frozen: no gate is passed or skipped. When the target is gone (killed, lost, deselected), or the flag is cleared, the AI resumes **the same waypoint** and `OnPatrolResume(ai, false)` is raised.
- **Fire authority.** `Hold` never yields (the AI stays on its waypoints). `Tight` yields only for targets `FireAuthorityPermits` (externally assigned, or passing the `ExternalTargetFilter`). `Free` yields for any target the weapon manager selected.
- **Leash.** With `patrolLeashDistance > 0`, if the vessel gets farther than that from the anchor (`patrolAnchor`, or the current waypoint when zero) while engaging, it breaks off: `OnPatrolResume(ai, true)`, the target is ignored for patrol engagement for `patrolLeashCooldown` seconds, and if that target was the F1 external assignment the assignment is cleared (reason `Cleared`) so it is not pulled straight back. The weapon manager may keep the target selected and still fire at it opportunistically while the AI flies the pattern.
- Waypoints ending (completed, cleared, replaced) end the engagement with `OnPatrolResume(ai, false)`.
- Defaults (`false`, 0) are stock behaviour; nothing is evaluated while `waypointsYieldToCombat` and `PatrolEngaged` are false.

### Stay on task vs engage on contact

- **Engage on contact:** `fireAuthority = Free`, no `ExternalTargetFilter`, `waypointsYieldToCombat = true`. The unit breaks off for any hostile it detects and selects, and returns to the pattern afterwards.
- **Stay on task:** `fireAuthority = Tight` plus an F1 assignment (`SetOverrideTarget(target, d, sticky: true)`) and/or an F4 filter that only passes the task targets. Crossing hostiles are ignored because every selection path respects the filter: `SmartFindTargetInternal` (normal queries go through `IsTargetExcluded`, which includes `IsTargetFilteredOut`), the override check (stock team-assist overrides on filtered targets are dropped, and team assist cannot replace a live external assignment), the secondary targets, and the AI's `GetGuardNonTarget` (the `BDATargetManager` queries). Under Tight with no filter, everything not externally assigned is filtered out. Incoming missiles are never filtered, so point defence still works.
- **Own-sensor detection.** Detection is per team: when a vessel's own radar (or visual/IRST scan) reports a contact, `BDATargetManager.ReportVessel` adds it to that team's database and sets `detected[team]`. So a stealth aircraft first seen by a drone's own radar is immediately a valid target for that drone (and its team); nothing requires a ground radar. Use F5 (`VesselRadarData.DisplayedContacts`) to see per-vessel what each drone's own sensors hold.

## 9. F12 and OD1

### F12: last guard scan

```csharp
public ViewScanResults LastScan { get; }   // MissileFire; copy of the struct from the last UpdateGuardViewScan
```

`ViewScanResults` (`BDArmory.Radar`) fields: `foundMissile`, `foundHeatMissile`, `foundRadarMissile`, `foundAntiRadiationMissile`, `foundGPSMissile`, `foundPassiveMissile`, `foundAGM`, `foundTorpedo`, `List<IncomingMissile> incomingMissiles` (sorted by distance; may be null; the live list, valid until the next scan), `firingAtMe`, `missDistance`, `missDeviation`, `threatPosition`, `threatVessel`, `threatWeaponManager`. `IncomingMissile` fields: `guidanceType`, `distance`, `time` (to CPA), `position`, `vessel`, `weaponManager`. The struct is only refreshed while guard mode (or the non-guard CM/RWR scan) runs.

### OD1: multi-spawn (user feature)

A user-facing feature of the Vessel Mover window ("Multi-spawn options" section): spawn N copies of a craft in a line (abreast or in trail, wrapping into rows), with clear-space finding, on the ground, at sea or in the air. A count above 1, or "Spawn in the air", routes the Spawn button through `VesselMover.MultiSpawnVessels`; count 1 on the surface is the unchanged stock flow (including the crew chooser, which is skipped for multi-spawn). Skizzy can drive it by setting these persistent settings (`BDArmory.Settings.BDArmorySettings`, saved with the other BDA settings) and then triggering the spawn from the window:

| Field | Default | Meaning |
|---|---|---|
| `int VESSEL_MOVER_SPAWN_COUNT` | 1 | Copies to spawn; no cap, a warning above 50 |
| `bool VESSEL_MOVER_SPAWN_TRAIL` | false | In trail (along the heading) instead of abreast |
| `float VESSEL_MOVER_SPAWN_SPACING` | 0 | Metres between copies; 0 = auto from craft size plus margin |
| `int VESSEL_MOVER_SPAWN_PER_ROW` | 0 | Copies per row; 0 = auto (wrap at 2 km) |
| `float VESSEL_MOVER_SPAWN_HEADING` | 90 | Heading of the line's craft in degrees |
| `bool VESSEL_MOVER_SPAWN_AIR` | false | Spawn in the air |
| `float VESSEL_MOVER_SPAWN_ALTITUDE` | 1000 | Air spawn altitude above sea level, metres |
| `float VESSEL_MOVER_SPAWN_SPEED` | 0 | Air spawn initial speed, m/s; 0 = the pilot AI's idle speed |

`public static bool VesselMover.MultiSpawnRequested` is true when the count is above 1 or `VESSEL_MOVER_SPAWN_AIR` is set. The pure layout logic is `BDArmory.VesselSpawning.MultiSpawnLayout` (`Plan(...)`, `PerRow`, `AutoSpacing`, ...), with `MultiSpawnStatus` { `Placed`, `Shifted`, `Moved`, `NoClearSpace`, `InvalidCount` }. There is no public method that starts a spawn without the window's click-to-place step; the spawn point is chosen by clicking on the map/terrain as for the single spawn. Spawns are staggered over frames; copies get unique names through the normal name deconfliction (only vessels with a weapon manager are renamed). The algorithm is documented in `skizzy-protocol/design/06-flight-control.md` section 9.1.

## 10. F8-F11 (P1)

### F9: missile fire API (done)

```csharp
// MissileFire
public bool FireMissileAt(MissileBase missile, Vessel target, bool checkClearance = true);
public static event Action<MissileFire, MissileBase, Vessel> OnMissileFired;   // (WM, missile, target vessel or null)
```

- `FireMissileAt` refuses (returns false) a null missile, a missile not on this vessel, or one already launched; and EMP/nuclear warheads while guard mode is on without a current target (the blast stand-off logic needs one). Otherwise it goes through the normal `FireCurrentMissile` path: fire authority applies while guard mode is on; with `checkClearance` a blocked missile can be swapped for an identical one with clearance (stock behaviour).
- `OnMissileFired` is raised after a launch through `FireCurrentMissile` (guard mode, manual fire, `FireMissileAt`) and the parallel salvo. For rails and turrets the reported missile is the one asked for; the mount may launch its ready missile instead. Missiles fired by other mods' code paths are not reported.

### F10: game events (done, partial coverage of target loss)

```csharp
// BDArmory.Competition.CompetitionScores
public static event Action<string, string, DamageFrom, int> OnHit;            // (attacker, victim, Guns/Rockets/Missiles/Ramming, parts hit; 1 for a bullet)
public static event Action<string, string, DamageFrom, AliveState> OnDeath;   // (victim, last attacker or "", last damage type, alive state)
// BDArmory.Competition.BDACompetitionMode
public static event Action OnCompetitionStarted;
public static event Action OnCompetitionEnded;
// BDArmory.UI.BDATargetManager
public static event Action<TargetInfo, BDTeam> OnTargetDetected;   // first added to that team's target database
public static event Action<TargetInfo, BDTeam> OnTargetLost;       // explicitly removed from that team's database
```

- `OnHit` and `OnDeath` are only raised for scored events, i.e. during a competition (the `Register*` methods return early otherwise). Vessel names are the scoring names.
- `OnTargetLost` covers `RemoveTarget` calls; the periodic clean-up that drops null, same-team or non-threat targets from the lists is not reported. Treat `OnTargetDetected` as "new contact for the team" and poll `TargetList(team)` for an exact view.
- All are static, null-checked, raised synchronously on the main thread, and handler exceptions are caught and logged.

### F8 (not done)

Steering override / post-autopilot hook and a configurable orbit radius were not implemented in this pass. The stock `FlyOrbit` radius (2500 m) is unchanged.

### F11 (not done)

Skipped in this pass.

## 11. Recipes

## 11. Recipes

All snippets compile against the real signatures (checked with `csc` against the KSP assemblies and a build of this branch).

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using BDArmory.Control;
using BDArmory.Radar;
using BDArmory.Targeting;
using BDArmory.UI;
```

**Assign a sticky target for 60 s and react to loss**

```csharp
static void AssignSticky(MissileFire wm, Vessel enemy)
{
    var target = enemy.GetComponent<TargetInfo>();
    if (target == null) return;
    MissileFire.OnExternalOverrideLost -= OnLost;   // avoid double subscription
    MissileFire.OnExternalOverrideLost += OnLost;
    wm.SetOverrideTarget(target, 60f, true);
}

static void OnLost(MissileFire wm, TargetInfo target, OverrideLostReason reason)
{
    Debug.Log($"[Adapter] {wm.vessel.vesselName} lost {(target != null && target.Vessel != null ? target.Vessel.vesselName : "(gone)")}: {reason}");
    // Expired: re-issue if the order still stands. Replaced/Cleared: someone else (or we) changed it.
    if (reason == OverrideLostReason.Expired && target != null) wm.SetOverrideTarget(target, 60f, true);
}
```

**Tight fire authority with a target filter**

```csharp
static void TightWithFilter(MissileFire wm, HashSet<Vessel> hostile)
{
    // Cheap, allocation-free, main thread. Called many times per scan.
    wm.ExternalTargetFilter = (mf, t) => t.Vessel != null && hostile.Contains(t.Vessel);
    wm.fireAuthority = FireAuthority.Tight;   // fires only at filter-passing or assigned targets, plus self-defence against missiles
}
// Back to stock: wm.ExternalTargetFilter = null; wm.fireAuthority = FireAuthority.Free;
```

**Feed secondary targets from a reusable list**

```csharp
static readonly List<TargetInfo> secondary = new List<TargetInfo>(16);

static void FeedSecondaries(MissileFire wm, Func<IEnumerable<Vessel>> pick)
{
    wm.ExternalSecondaryTargets = mf =>
    {
        secondary.Clear();                        // BDArmory copies the list; reusing it is fine
        foreach (var v in pick())
        {
            var ti = v != null ? v.GetComponent<TargetInfo>() : null;
            if (ti != null) secondary.Add(ti);    // priority order, highest first
        }
        return secondary;
    };
    wm.ExternalSecondaryTargetsOnly = false;      // true: no BDArmory picks after ours
    // Takes effect when Max Turret Targets or Max Missile Tgts is above 1.
}
```

**Run a custom route**

```csharp
static bool Route(Vessel v)
{
    var ai = v.FindPartModuleImplementing<BDModulePilotAI>() as BDGenericAIBase;   // or the surface/VTOL AI
    if (ai == null) return false;
    var points = new List<Vector3>();
    for (int i = 0; i < 6; ++i)
        points.Add(new Vector3((float)(v.latitude + 0.01 * Math.Cos(i * Math.PI / 3)),
                               (float)(v.longitude + 0.01 * Math.Sin(i * Math.PI / 3)),
                               500f));                 // lat, lon, altitude above terrain
    BDGenericAIBase.OnCustomWaypointReached += (a, index, lap) => Debug.Log($"[Adapter] {a.vessel.vesselName} gate {index} lap {lap}");
    BDGenericAIBase.OnCustomWaypointRouteCompleted += a => Debug.Log($"[Adapter] {a.vessel.vesselName} route done");
    // 300 m default radius, 2 laps, 150 m/s on gates 1 and 3, enter guard mode at the end.
    return ai.SetWaypoints(points, 300f, 2, new float[] { 0, 150, 0, 150, 0, 0 }, true);   // false: empty list or AI not active
}
```

**Read radar contacts**

```csharp
static void ReadContacts(MissileFire wm)
{
    var vrd = wm.vesselRadarData;                 // null without radar/IRST
    if (vrd == null) return;
    var contacts = vrd.DisplayedContacts;         // live list: main thread, don't keep across frames
    int active = vrd.ActiveLockedContactIndex;    // -1: none
    for (int i = 0; i < contacts.Count; ++i)
    {
        var c = contacts[i];
        if (c.vessel == null) continue;
        Debug.Log($"{c.vessel.vesselName} locked={c.locked} activeLock={i == active}");
    }
    for (int i = 0; i < vrd.DisplayedIRContacts.Count; ++i)
        if (vrd.DisplayedIRContacts[i].vessel != null) { /* IRST contact */ }
    var scan = wm.LastScan;                       // F12
    if (scan.incomingMissiles != null && scan.incomingMissiles.Count > 0) { /* nearest first */ }
}
```

## 12. Performance

- No per-frame allocations are added by the hooks. The F1 events and F2 snapshot reuse preallocated collections; `FireAuthorityPermits(Vessel, bool)` uses `TryGetComponent` (no allocation); the F5 accessors return the existing lists.
- Unused cost: one null check or enum comparison at each gate (`ExternalTargetFilter == null && fireAuthority != Tight`; `fireAuthority != Free`; `externalOverride`; `ExternalSecondaryTargets != null || externalSecondaryList.Count > 0`).
- Your delegates:
  - `ExternalTargetFilter` is the hot one: it can run several times per candidate target per weapon manager per scan, and from `GetGuardNonTarget` on the AI update. No LINQ, no string building, no allocations, no logging, no KSP calls that search the scene, and never block.
  - `ExternalSecondaryTargets` runs once per scan per weapon manager: reuse the list you return.
  - `OnExternalOverrideLost`, `OnCustomWaypointReached`, `OnCustomWaypointRouteCompleted`, `OnPatrolEngage`, `OnPatrolResume`, `OnMissileFired` and the F10 events are static events raised synchronously on the main thread inside the game loop: do the minimum and queue the rest.
  - Don't call `SetOverrideTarget` every frame for the same target: each call forces a new target scan (`targetScanTimer = -100`) on the next guard tick. Assign once, then react to `OnExternalOverrideLost`.
- Logging: with `DEBUG_AI` on, `[BDArmory.MissileFire]` and `[BDArmory.BDGenericAIBase]` log assignment losses and route progress.
