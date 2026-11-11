// BDArmory Overdrive: fleet-wide point defence (BDArmorySettings.GUARD_FLEET_DEFENCE).
// Pure logic, no Unity dependencies, so it can be unit tested outside KSP (see Overdrive/tests). The game side is FleetPointDefence.cs.
using System;
using System.Collections.Generic;

namespace BDArmory.Control
{
    /// <summary>
    /// [Overdrive] Fleet-wide point defence rules: an incoming missile gets a new interceptor only when the ones already chasing it, from any friendly ship,
    /// have missed or been destroyed. Point defence compares the team-wide count (own + allied interceptors in flight + allied launch claims) against Missiles/Tgt
    /// (and the shoot-shoot quota), instead of only its own interceptors.
    /// </summary>
    public static class FleetDefenceRules
    {
        /// <summary>How long a launch claim lasts (a ship started an interceptor launch routine at a threat; allies count it as one interceptor until the missile is in flight).</summary>
        public const float ClaimTime = 2.5f;

        /// <summary>
        /// The interceptors at an incoming missile that point defence counts against Missiles/Tgt.
        /// Fleet defence off: our own only (missiles away + queued launches), exactly as before.
        /// </summary>
        public static int TeamInterceptors(int own, int allied, int alliedClaims, bool fleetDefence)
            => fleetDefence ? Math.Max(0, own) + Math.Max(0, allied) + Math.Max(0, alliedClaims) : Math.Max(0, own);

        /// <summary>Whether an incoming missile is skipped only because of allied interceptors/claims (ours alone wouldn't fill the quota). For the DEBUG_GUARD_FIRE log.</summary>
        public static bool CoveredByAllies(int own, int team, int quota) => !GuardFireRules.TargetSaturated(own, quota) && GuardFireRules.TargetSaturated(team, quota);

        /// <summary>
        /// Whether a fired missile counts as an interceptor at its target for the fleet tally: it has a team, its (current) target is a missile,
        /// and it counts the way our own missiles count towards missiles away (GuardFireRules.CountsAsMissileAway: not missed, not exploded).
        /// </summary>
        public static bool CountsAsInterceptor(bool hasTeam, bool targetIsMissile, bool hasFired, bool launched, bool hasExploded, bool hasMissed, bool isBomb)
            => hasTeam && targetIsMissile && GuardFireRules.CountsAsMissileAway(true, hasFired, launched, hasExploded, hasMissed, isBomb);
    }

    /// <summary>
    /// [Overdrive] The team-wide interceptor tally for fleet point defence, plus the short launch claims that cover the same-frame race
    /// (two ships deciding in the same frame don't see each other's launch yet).
    /// The in-flight counts are rebuilt once per physics frame from the fired missiles (BeginRebuild, AddInterceptor..., EndRebuild); claims live across frames.
    /// Keys are opaque ints (team index, Unity instance IDs of the threat's TargetInfo and of the firing vessel; ship 0 = unknown). No allocations once warm.
    /// </summary>
    public sealed class FleetInterceptorTally
    {
        struct Entry
        {
            public int Count;
            public int ShipA, ShipB; // The first two distinct ships with interceptors at the threat (0: none), for naming a covering ally in the log.
        }

        struct Claim
        {
            public int Team, Threat, Ship;
            public float Expires;
            public bool Launched; // The interceptor left the rail; the claim stays until the tally sees it (or it expires).
            public int Baseline; // The ship's interceptors at the threat in the tally when the claim was made (+ its earlier launched claims there).
        }

        readonly Dictionary<long, Entry> inFlight = new(); // (team, threat) -> interceptors in flight.
        readonly Dictionary<long, int> fromShip = new(); // (ship, threat) -> interceptors in flight.
        readonly List<Claim> claims = new();

        static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        /// <summary>The number of live claims (including expired ones not yet pruned).</summary>
        public int ClaimCount => claims.Count;

        #region The in-flight tally
        /// <summary>Start a rebuild of the in-flight counts (claims are kept).</summary>
        public void BeginRebuild()
        {
            inFlight.Clear();
            fromShip.Clear();
        }

        /// <summary>Count an interceptor of the team in flight at the threat, fired by the ship (0: unknown).</summary>
        public void AddInterceptor(int team, int threat, int ship)
        {
            long key = Key(team, threat);
            inFlight.TryGetValue(key, out Entry entry);
            ++entry.Count;
            if (ship != 0)
            {
                if (entry.ShipA == 0) entry.ShipA = ship;
                else if (entry.ShipB == 0 && entry.ShipA != ship) entry.ShipB = ship;
                long shipKey = Key(ship, threat);
                fromShip.TryGetValue(shipKey, out int n);
                fromShip[shipKey] = n + 1;
            }
            inFlight[key] = entry;
        }

        /// <summary>
        /// Finish the rebuild: drop the claims that have expired and the launched claims whose interceptor is now in the tally
        /// (the ship has more interceptors at the threat than when it claimed it), so a launch is counted exactly once.
        /// </summary>
        public void EndRebuild(float now)
        {
            for (int i = claims.Count - 1; i >= 0; --i)
            {
                var c = claims[i];
                if (now >= c.Expires || (c.Launched && FromShip(c.Ship, c.Threat) > c.Baseline)) claims.RemoveAt(i);
            }
        }

        /// <summary>The interceptors in flight at the threat from the given teams (own team and its allies).</summary>
        public int InFlight(List<int> teams, int threat)
        {
            if (teams == null || inFlight.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < teams.Count; ++i)
                if (inFlight.TryGetValue(Key(teams[i], threat), out Entry entry)) n += entry.Count;
            return n;
        }

        /// <summary>The interceptors in flight at the threat fired by the ship.</summary>
        public int FromShip(int ship, int threat) => ship != 0 && fromShip.Count > 0 && fromShip.TryGetValue(Key(ship, threat), out int n) ? n : 0;

        /// <summary>The interceptors in flight at the threat from the given teams, except the ship's own.</summary>
        public int AlliedInFlight(List<int> teams, int threat, int ship) => Math.Max(0, InFlight(teams, threat) - FromShip(ship, threat));
        #endregion

        #region Launch claims
        /// <summary>
        /// The ship started an interceptor launch routine at the threat: allies count it as one interceptor until it expires, is released (the launch failed)
        /// or its interceptor shows up in the tally after MarkLaunched.
        /// </summary>
        public void AddClaim(int team, int threat, int ship, float now, float duration = FleetDefenceRules.ClaimTime)
        {
            int pendingLaunched = 0;
            for (int i = 0; i < claims.Count; ++i)
            {
                var c = claims[i];
                if (c.Ship != ship || c.Threat != threat) continue;
                if (!c.Launched)
                {
                    c.Team = team;
                    c.Expires = now + duration;
                    claims[i] = c;
                    return;
                }
                ++pendingLaunched;
            }
            claims.Add(new Claim { Team = team, Threat = threat, Ship = ship, Expires = now + duration, Baseline = FromShip(ship, threat) + pendingLaunched });
        }

        /// <summary>The ship's launch at the threat failed (nothing left the rail): release its claim so the fleet isn't blocked. False if there was none.</summary>
        public bool ReleaseClaim(int ship, int threat)
        {
            for (int i = claims.Count - 1; i >= 0; --i)
            {
                var c = claims[i];
                if (c.Ship == ship && c.Threat == threat && !c.Launched) { claims.RemoveAt(i); return true; }
            }
            return false;
        }

        /// <summary>The ship's interceptor at the threat left the rail: keep the claim until the tally sees the interceptor (EndRebuild) or it expires.</summary>
        public void MarkLaunched(int ship, int threat)
        {
            for (int i = 0; i < claims.Count; ++i)
            {
                var c = claims[i];
                if (c.Ship == ship && c.Threat == threat && !c.Launched) { c.Launched = true; claims[i] = c; return; }
            }
        }

        /// <summary>The unexpired claims at the threat by ships of the given teams, other than the ship itself.</summary>
        public int AlliedClaims(List<int> teams, int threat, int ship, float now)
        {
            if (teams == null || claims.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < claims.Count; ++i)
            {
                var c = claims[i];
                if (c.Threat == threat && c.Ship != ship && now < c.Expires && teams.Contains(c.Team)) ++n;
            }
            return n;
        }

        /// <summary>A ship of the given teams, other than the ship itself, covering the threat (a claim first, else an interceptor in flight); 0 if none.</summary>
        public int CoveringShip(List<int> teams, int threat, int ship, float now)
        {
            if (teams == null) return 0;
            for (int i = 0; i < claims.Count; ++i)
            {
                var c = claims[i];
                if (c.Threat == threat && c.Ship != ship && now < c.Expires && teams.Contains(c.Team)) return c.Ship;
            }
            for (int i = 0; i < teams.Count; ++i)
            {
                if (!inFlight.TryGetValue(Key(teams[i], threat), out Entry entry)) continue;
                if (entry.ShipA != 0 && entry.ShipA != ship) return entry.ShipA;
                if (entry.ShipB != 0 && entry.ShipB != ship) return entry.ShipB;
            }
            return 0;
        }

        /// <summary>Drop every claim at the threat (it died or was unloaded).</summary>
        public void ReleaseThreat(int threat)
        {
            for (int i = claims.Count - 1; i >= 0; --i)
                if (claims[i].Threat == threat) claims.RemoveAt(i);
        }

        /// <summary>Drop the expired claims and the claims at threats that are gone (dead or unloaded). threatGone may be null.</summary>
        public void PruneClaims(float now, Func<int, bool> threatGone)
        {
            for (int i = claims.Count - 1; i >= 0; --i)
            {
                var c = claims[i];
                if (now >= c.Expires || (threatGone != null && threatGone(c.Threat))) claims.RemoveAt(i);
            }
        }
        #endregion

        public void Clear()
        {
            inFlight.Clear();
            fromShip.Clear();
            claims.Clear();
        }
    }
}
