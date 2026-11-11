// BDArmory Overdrive: fleet-wide point defence, game side (BDArmorySettings.GUARD_FLEET_DEFENCE). The rules and the tally are in FleetDefenceLogic.cs.
using System;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Control
{
    /// <summary>
    /// [Overdrive] One team-wide interceptor tally shared by every weapon manager: the interceptors in flight at each incoming missile (rebuilt from
    /// BDATargetManager.FiredMissiles at most once per physics frame, on the first point defence query of the frame) and the launch claims of the ships
    /// running an interceptor launch routine. A missile's current target is read at each rebuild, so a retargeted interceptor (MISSILE_RETARGETING)
    /// counts against its new target.
    /// </summary>
    public static class FleetPointDefence
    {
        static readonly FleetInterceptorTally tally = new();
        static readonly Dictionary<string, int> teamIndices = new(); // Team name -> stable index (claims are keyed by it), until the next scene.
        static readonly List<BDTeam> teams = new();
        static readonly Dictionary<int, Vessel> ships = new(); // Ship key -> vessel, for naming a covering ally in the DEBUG_GUARD_FIRE log.
        static readonly Dictionary<int, TargetInfo> claimTargets = new(); // Threat key -> threat, for dropping claims at dead or unloaded threats.
        static readonly Func<int, bool> threatGone = key => !claimTargets.TryGetValue(key, out TargetInfo target) || target == null || target.Vessel == null || !target.Vessel.loaded;
        static readonly List<int> goneThreats = new(); // Scratch list for pruning claimTargets.
        static float nextThreatPrune = float.NegativeInfinity;
        static List<IBDWeapon> builtFrom; // The FiredMissiles list of the scene the tally belongs to (a new list means a new scene).
        static float builtAt = float.NaN; // Time.fixedTime of the last rebuild.

        /// <summary>The key for a ship (0: none).</summary>
        public static int ShipKey(Vessel vessel)
        {
            if (vessel == null) return 0;
            int key = vessel.GetInstanceID();
            ships[key] = vessel;
            return key;
        }

        /// <summary>The key for an incoming missile (the TargetInfo instance ID, as point defence's other memories).</summary>
        public static int ThreatKey(TargetInfo target) => target != null ? target.GetInstanceID() : 0;

        static int TeamIndex(BDTeam team)
        {
            if (teamIndices.TryGetValue(team.Name, out int index)) return index;
            index = teams.Count;
            teams.Add(team);
            teamIndices.Add(team.Name, index);
            return index;
        }

        static void Reset(List<IBDWeapon> fired)
        {
            tally.Clear();
            teamIndices.Clear();
            teams.Clear();
            ships.Clear();
            claimTargets.Clear();
            builtFrom = fired;
            builtAt = float.NaN;
            nextThreatPrune = float.NegativeInfinity;
        }

        /// <summary>Rebuild the in-flight tally if it's not from this physics frame, and drop expired claims and claims at dead or unloaded threats.</summary>
        public static void EnsureFresh()
        {
            var fired = BDATargetManager.FiredMissiles;
            float now = Time.fixedTime;
            if (fired != builtFrom || now < builtAt) Reset(fired); // New scene (or time went backwards): start again.
            else if (now == builtAt) return;
            builtAt = now;
            tally.BeginRebuild();
            if (fired != null)
            {
                for (int i = 0; i < fired.Count; ++i)
                {
                    if (fired[i] is not MissileBase missile || missile == null) continue;
                    var target = missile.targetVessel; // Null when the target died.
                    if (target == null) continue;
                    var team = missile.Team;
                    if (!FleetDefenceRules.CountsAsInterceptor(team != null, target.isMissile, missile.HasFired, missile.launched, missile.HasExploded, missile.HasMissed, missile.GetWeaponClass() == WeaponClasses.Bomb)) continue;
                    tally.AddInterceptor(TeamIndex(team), target.GetInstanceID(), ShipKey(missile.SourceVessel));
                }
            }
            float time = Time.time;
            tally.EndRebuild(time);
            tally.PruneClaims(time, threatGone);
            if (tally.ClaimCount == 0) { if (claimTargets.Count > 0) claimTargets.Clear(); }
            else if (time >= nextThreatPrune) // Forget dead or unloaded threats (their claims were dropped above) at most once per second.
            {
                nextThreatPrune = time + 1f;
                goneThreats.Clear();
                foreach (var kvp in claimTargets) if (threatGone(kvp.Key)) goneThreats.Add(kvp.Key);
                for (int i = 0; i < goneThreats.Count; ++i) claimTargets.Remove(goneThreats[i]);
                goneThreats.Clear();
            }
        }

        /// <summary>The team indices whose interceptors count for the team: itself and the teams it lists as allies (neutral teams only count themselves).</summary>
        public static void FriendlyTeams(BDTeam team, List<int> into)
        {
            into.Clear();
            if (team == null) return;
            int own = TeamIndex(team);
            for (int i = 0; i < teams.Count; ++i)
            {
                if (i == own) into.Add(i);
                else if (!team.Neutral && !teams[i].Neutral && team.Allies.Contains(teams[i].Name)) into.Add(i);
            }
        }

        /// <summary>Allied interceptors in flight at the threat (not the ship's own). Call EnsureFresh first.</summary>
        public static int AlliedInFlight(List<int> friendlyTeams, int threat, int ship) => tally.AlliedInFlight(friendlyTeams, threat, ship);

        /// <summary>Unexpired allied launch claims at the threat (not the ship's own).</summary>
        public static int AlliedClaims(List<int> friendlyTeams, int threat, int ship) => tally.AlliedClaims(friendlyTeams, threat, ship, Time.time);

        /// <summary>The name of an allied ship covering the threat, for the log.</summary>
        public static string CoveringShipName(List<int> friendlyTeams, int threat, int ship)
        {
            int key = tally.CoveringShip(friendlyTeams, threat, ship, Time.time);
            if (key == 0) return "an ally";
            return ships.TryGetValue(key, out Vessel vessel) && vessel != null ? vessel.vesselName : "a lost ally";
        }

        /// <summary>
        /// Claim a threat for an interceptor launch routine by the weapon manager (allies count it as one interceptor). False if it can't be claimed (no team or vessel).
        /// The routine must call MarkLaunched when the interceptor leaves the rail, or ReleaseClaim if nothing launched; otherwise the claim expires after FleetDefenceRules.ClaimTime.
        /// </summary>
        public static bool Claim(MissileFire wm, TargetInfo target, out int shipKey, out int threatKey)
        {
            shipKey = 0; threatKey = 0;
            if (wm == null || wm.vessel == null || wm.Team == null || target == null || target.Vessel == null) return false;
            EnsureFresh(); // The claim's baseline is the ship's interceptors at the threat in this frame's tally.
            shipKey = ShipKey(wm.vessel);
            threatKey = ThreatKey(target);
            tally.AddClaim(TeamIndex(wm.Team), threatKey, shipKey, Time.time);
            claimTargets[threatKey] = target;
            return true;
        }

        /// <summary>The claimed interceptor left the rail: allies keep counting the claim until the interceptor is in the tally.</summary>
        public static void MarkLaunched(int shipKey, int threatKey) => tally.MarkLaunched(shipKey, threatKey);

        /// <summary>The launch failed: release the claim so the fleet isn't blocked.</summary>
        public static void ReleaseClaim(int shipKey, int threatKey) => tally.ReleaseClaim(shipKey, threatKey);
    }
}
