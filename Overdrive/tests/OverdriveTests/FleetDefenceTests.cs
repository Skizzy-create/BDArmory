using System;
using System.Collections.Generic;
using BDArmory.Control;

namespace OverdriveTests
{
    /// <summary>
    /// Fleet-wide point defence (GUARD_FLEET_DEFENCE): the team-wide interceptor tally, launch claims (same-frame race, release on failure, expiry),
    /// and fleet simulations with the same pure classes MissileFire / FleetPointDefence use (FleetInterceptorTally, FleetDefenceRules, GuardFireRules).
    /// Rule: a threat gets a new interceptor only when the one(s) already chasing it, from any friendly ship, have missed or been destroyed.
    /// </summary>
    public static class FleetDefenceTests
    {
        const float Dt = 0.02f; // Physics frame.

        static List<int> Teams(params int[] teams) => [.. teams];

        // ---- The in-flight tally ----

        [Test]
        public static void Tally_CountsOwnAndAlliedInterceptors()
        {
            var tally = new FleetInterceptorTally();
            tally.BeginRebuild();
            tally.AddInterceptor(0, 100, 1); // Ship 1 (us).
            tally.AddInterceptor(0, 100, 2); // Ship 2 (ally).
            tally.AddInterceptor(0, 100, 2);
            tally.AddInterceptor(0, 200, 3);
            tally.EndRebuild(0f);
            var team = Teams(0);
            Assert.Equal(3, tally.InFlight(team, 100));
            Assert.Equal(1, tally.FromShip(1, 100));
            Assert.Equal(2, tally.AlliedInFlight(team, 100, 1), "allied = team - own");
            Assert.Equal(1, tally.AlliedInFlight(team, 200, 1));
            Assert.Equal(0, tally.AlliedInFlight(team, 300, 1), "unknown threat");
            Assert.Equal(2, tally.CoveringShip(team, 100, 1, 0f), "names an ally, not us");
            Assert.Equal(1, tally.CoveringShip(team, 100, 2, 0f));
            Assert.Equal(0, tally.CoveringShip(team, 300, 1, 0f));
        }

        [Test]
        public static void Tally_OtherTeamsInterceptorsDontCount()
        {
            var tally = new FleetInterceptorTally();
            tally.BeginRebuild();
            tally.AddInterceptor(1, 100, 7); // Team B chases the same missile (e.g. a missile aimed at both teams).
            tally.AddInterceptor(1, 100, 8);
            tally.EndRebuild(0f);
            Assert.Equal(0, tally.InFlight(Teams(0), 100));
            Assert.Equal(0, tally.AlliedInFlight(Teams(0), 100, 1));
            Assert.Equal(0, tally.CoveringShip(Teams(0), 100, 1, 0f));
            Assert.Equal(2, tally.InFlight(Teams(1), 100));
            Assert.Equal(2, tally.InFlight(Teams(0, 1), 100), "allied teams count when listed");
        }

        [Test]
        public static void Tally_UnknownSourceCountsAsAllied()
        {
            var tally = new FleetInterceptorTally();
            tally.BeginRebuild();
            tally.AddInterceptor(0, 100, 0); // No source vessel.
            tally.EndRebuild(0f);
            Assert.Equal(1, tally.AlliedInFlight(Teams(0), 100, 1));
            Assert.Equal(0, tally.FromShip(0, 100));
            Assert.Equal(0, tally.CoveringShip(Teams(0), 100, 1, 0f), "no ship to name");
        }

        [Test]
        public static void Interceptor_MissedAndExplodedDontCount()
        {
            Assert.True(FleetDefenceRules.CountsAsInterceptor(true, true, true, true, false, false, false));
            Assert.True(FleetDefenceRules.CountsAsInterceptor(true, true, false, true, false, false, false), "launched counts (as CountsAsMissileAway)");
            Assert.False(FleetDefenceRules.CountsAsInterceptor(true, true, true, true, false, true, false), "missed");
            Assert.False(FleetDefenceRules.CountsAsInterceptor(true, true, true, true, true, false, false), "exploded");
            Assert.False(FleetDefenceRules.CountsAsInterceptor(true, true, false, false, false, false, false), "not fired");
            Assert.False(FleetDefenceRules.CountsAsInterceptor(true, false, true, true, false, false, false), "target isn't a missile (anti-ship missile)");
            Assert.False(FleetDefenceRules.CountsAsInterceptor(false, true, true, true, false, false, false), "no team");
            // Same rule as our own missiles away.
            foreach (bool fired in new[] { false, true })
                foreach (bool exploded in new[] { false, true })
                    foreach (bool missed in new[] { false, true })
                        foreach (bool bomb in new[] { false, true })
                            Assert.Equal(GuardFireRules.CountsAsMissileAway(true, fired, false, exploded, missed, bomb), FleetDefenceRules.CountsAsInterceptor(true, true, fired, false, exploded, missed, bomb));
        }

        [Test]
        public static void Tally_RetargetedInterceptorCountsAgainstItsNewTarget()
        {
            // MISSILE_RETARGETING: the interceptor's target died and it picked another incoming missile. The tally reads each missile's current target at every rebuild.
            var tally = new FleetInterceptorTally();
            tally.BeginRebuild();
            tally.AddInterceptor(0, 100, 2);
            tally.EndRebuild(0f);
            Assert.Equal(1, tally.AlliedInFlight(Teams(0), 100, 1));
            tally.BeginRebuild(); // Next frame: threat 100 is dead, the interceptor now chases 200.
            tally.AddInterceptor(0, 200, 2);
            tally.EndRebuild(Dt);
            Assert.Equal(0, tally.AlliedInFlight(Teams(0), 100, 1));
            Assert.Equal(1, tally.AlliedInFlight(Teams(0), 200, 1));
        }

        // ---- Rules ----

        [Test]
        public static void Rules_SettingOffIsOwnOnly()
        {
            Assert.Equal(1, FleetDefenceRules.TeamInterceptors(1, 3, 2, false));
            Assert.Equal(0, FleetDefenceRules.TeamInterceptors(0, 3, 2, false));
            Assert.Equal(6, FleetDefenceRules.TeamInterceptors(1, 3, 2, true));
            Assert.Equal(0, FleetDefenceRules.TeamInterceptors(-1, -1, -1, true), "never negative");
            for (int own = 0; own < 4; ++own)
                for (int allied = 0; allied < 4; ++allied)
                    Assert.Equal(GuardFireRules.TargetSaturated(own, 2), GuardFireRules.TargetSaturated(FleetDefenceRules.TeamInterceptors(own, allied, allied, false), 2), "off = per-ship decision");
        }

        [Test]
        public static void Rules_CoveredByAllies()
        {
            Assert.True(FleetDefenceRules.CoveredByAllies(0, 1, 1));
            Assert.False(FleetDefenceRules.CoveredByAllies(1, 2, 1), "our own fill the quota");
            Assert.False(FleetDefenceRules.CoveredByAllies(0, 0, 1), "not covered");
            Assert.True(FleetDefenceRules.CoveredByAllies(1, 2, 2), "Missiles/Tgt 2: one ours, one allied");
            Assert.False(FleetDefenceRules.CoveredByAllies(0, 1, 2), "Missiles/Tgt 2: one allied isn't enough");
        }

        // ---- Launch claims ----

        [Test]
        public static void Claim_CountsForAlliesNotTheClaimantOrOtherTeams()
        {
            var tally = new FleetInterceptorTally();
            tally.AddClaim(0, 100, 2, 0f);
            Assert.Equal(1, tally.AlliedClaims(Teams(0), 100, 1, 0.1f));
            Assert.Equal(0, tally.AlliedClaims(Teams(0), 100, 2, 0.1f), "the claimant counts its own launch itself");
            Assert.Equal(0, tally.AlliedClaims(Teams(1), 100, 5, 0.1f), "other team");
            Assert.Equal(0, tally.AlliedClaims(Teams(0), 200, 1, 0.1f), "other threat");
            Assert.Equal(2, tally.CoveringShip(Teams(0), 100, 1, 0.1f));
            tally.AddClaim(0, 100, 2, 0.5f); // The same ship again (refresh, not a second claim).
            Assert.Equal(1, tally.ClaimCount);
        }

        [Test]
        public static void Claim_Expires()
        {
            var tally = new FleetInterceptorTally();
            tally.AddClaim(0, 100, 2, 0f);
            Assert.Equal(1, tally.AlliedClaims(Teams(0), 100, 1, FleetDefenceRules.ClaimTime - 0.01f));
            Assert.Equal(0, tally.AlliedClaims(Teams(0), 100, 1, FleetDefenceRules.ClaimTime), "expired claims don't count even before pruning");
            Assert.Equal(0, tally.CoveringShip(Teams(0), 100, 1, FleetDefenceRules.ClaimTime));
            tally.PruneClaims(FleetDefenceRules.ClaimTime, null);
            Assert.Equal(0, tally.ClaimCount, "pruned");
            tally.AddClaim(0, 100, 2, 10f);
            tally.BeginRebuild();
            tally.EndRebuild(10f + FleetDefenceRules.ClaimTime);
            Assert.Equal(0, tally.ClaimCount, "the rebuild drops expired claims too");
            Assert.True(FleetDefenceRules.ClaimTime >= 2f && FleetDefenceRules.ClaimTime <= 3f);
        }

        [Test]
        public static void Claim_ReleasedWhenTheLaunchFails()
        {
            var tally = new FleetInterceptorTally();
            tally.AddClaim(0, 100, 2, 0f);
            Assert.True(tally.ReleaseClaim(2, 100));
            Assert.Equal(0, tally.AlliedClaims(Teams(0), 100, 1, 0.1f), "a failed launch never blocks the fleet");
            Assert.False(tally.ReleaseClaim(2, 100), "nothing left to release");
            Assert.False(tally.ReleaseClaim(3, 100));
        }

        [Test]
        public static void Claim_LaunchedCountsExactlyOnceUntilTheTallySeesIt()
        {
            var tally = new FleetInterceptorTally();
            var team = Teams(0);
            tally.BeginRebuild();
            tally.EndRebuild(0f);
            tally.AddClaim(0, 100, 2, 0f);
            Assert.Equal(1, tally.AlliedInFlight(team, 100, 1) + tally.AlliedClaims(team, 100, 1, 0f));
            tally.MarkLaunched(2, 100); // Left the rail mid-frame; the tally (built at the start of the frame) doesn't have it yet.
            Assert.False(tally.ReleaseClaim(2, 100), "a launched claim isn't released as a failure");
            Assert.Equal(1, tally.AlliedInFlight(team, 100, 1) + tally.AlliedClaims(team, 100, 1, 0f), "still covered for the rest of the frame");
            tally.BeginRebuild(); // Next frame: the interceptor is in flight.
            tally.AddInterceptor(0, 100, 2);
            tally.EndRebuild(Dt);
            Assert.Equal(0, tally.ClaimCount, "the claim gives way to the interceptor");
            Assert.Equal(1, tally.AlliedInFlight(team, 100, 1) + tally.AlliedClaims(team, 100, 1, Dt));
        }

        [Test]
        public static void Claim_LaunchedWithInterceptorsAlreadyThere()
        {
            // Missiles/Tgt 2: ship 2 already has one interceptor at the threat; its second launch claim gives way only when a second one shows up.
            var tally = new FleetInterceptorTally();
            var team = Teams(0);
            tally.BeginRebuild();
            tally.AddInterceptor(0, 100, 2);
            tally.EndRebuild(0f);
            tally.AddClaim(0, 100, 2, 0f);
            tally.MarkLaunched(2, 100);
            tally.BeginRebuild(); // The new interceptor isn't in FiredMissiles yet (a VLS spawns it a little later).
            tally.AddInterceptor(0, 100, 2);
            tally.EndRebuild(Dt);
            Assert.Equal(1, tally.ClaimCount);
            Assert.Equal(2, tally.AlliedInFlight(team, 100, 1) + tally.AlliedClaims(team, 100, 1, Dt));
            tally.BeginRebuild();
            tally.AddInterceptor(0, 100, 2);
            tally.AddInterceptor(0, 100, 2);
            tally.EndRebuild(2 * Dt);
            Assert.Equal(0, tally.ClaimCount);
            Assert.Equal(2, tally.AlliedInFlight(team, 100, 1));
        }

        [Test]
        public static void Claim_TwoLaunchesBeforeEitherIsSeen()
        {
            var tally = new FleetInterceptorTally();
            var team = Teams(0);
            tally.BeginRebuild();
            tally.EndRebuild(0f);
            tally.AddClaim(0, 100, 2, 0f);
            tally.MarkLaunched(2, 100);
            tally.AddClaim(0, 100, 2, Dt); // A second launch at the same threat before the first is in the tally.
            tally.MarkLaunched(2, 100);
            Assert.Equal(2, tally.AlliedClaims(team, 100, 1, Dt));
            tally.BeginRebuild();
            tally.AddInterceptor(0, 100, 2); // Only the first is seen.
            tally.EndRebuild(2 * Dt);
            Assert.Equal(2, tally.AlliedInFlight(team, 100, 1) + tally.AlliedClaims(team, 100, 1, 2 * Dt), "each launch counted once");
        }

        [Test]
        public static void Claim_LaunchedButNeverSeenExpires()
        {
            var tally = new FleetInterceptorTally();
            tally.AddClaim(0, 100, 2, 0f);
            tally.MarkLaunched(2, 100); // E.g. destroyed on the rail.
            for (float t = Dt; t < FleetDefenceRules.ClaimTime + 2 * Dt; t += Dt) { tally.BeginRebuild(); tally.EndRebuild(t); }
            Assert.Equal(0, tally.ClaimCount);
        }

        [Test]
        public static void Claim_DroppedForDeadOrUnloadedThreats()
        {
            var tally = new FleetInterceptorTally();
            tally.AddClaim(0, 100, 2, 0f);
            tally.AddClaim(0, 200, 3, 0f);
            tally.AddClaim(0, 300, 4, 0f);
            var gone = new HashSet<int> { 200 };
            tally.PruneClaims(0.1f, t => gone.Contains(t));
            Assert.Equal(2, tally.ClaimCount);
            Assert.Equal(0, tally.AlliedClaims(Teams(0), 200, 1, 0.1f));
            tally.ReleaseThreat(300);
            Assert.Equal(1, tally.ClaimCount);
            Assert.Equal(1, tally.AlliedClaims(Teams(0), 100, 1, 0.1f));
            tally.Clear();
            Assert.Equal(0, tally.ClaimCount);
        }

        [Test]
        public static void ReasonLog_WouldLogDoesntRecord()
        {
            var log = new GuardFireReasonLog();
            Assert.True(log.WouldLog(GuardFireChannel.FleetCover, GuardFireBlock.CoveredByAllies, 0f));
            Assert.True(log.WouldLog(GuardFireChannel.FleetCover, GuardFireBlock.CoveredByAllies, 0.1f), "peeking doesn't record");
            Assert.True(log.ShouldLog(GuardFireChannel.FleetCover, GuardFireBlock.CoveredByAllies, 0.2f));
            Assert.False(log.WouldLog(GuardFireChannel.FleetCover, GuardFireBlock.CoveredByAllies, 1f));
            Assert.True(log.WouldLog(GuardFireChannel.FleetCover, GuardFireBlock.CoveredByAllies, 5.3f), "repeats after 5s");
            Assert.True(log.ShouldLog(GuardFireChannel.PointDefence, GuardFireBlock.None, 1f), "point defence channel separate");
            Assert.False(log.WouldLog(GuardFireChannel.FleetCover, GuardFireBlock.CoveredByAllies, 1.1f), "fleet cover not reset by point defence");
        }

        // ---- Fleet simulation ----

        /// <summary>
        /// Ships running point defence every physics frame (more often than the game's 0.1s, so races are more likely), as PointDefenseTurretFiring + GuardMissileRoutine:
        /// count own (missiles away, up to date at once) + allied in flight (tally, rebuilt once per frame) + allied claims; claim on starting a launch routine;
        /// the interceptor leaves the rail when the routine ends (MarkLaunched) and shows up in FiredMissiles at once (the tally sees it next frame), or the launch fails (ReleaseClaim).
        /// </summary>
        sealed class Fleet
        {
            public sealed class Ship
            {
                public int Key, Team;
                public int BusyFrames, RoutineThreat;
                public bool Claimed, FailNext, Stall, Holding;
            }

            public sealed class Interceptor
            {
                public int Team, Ship, Threat;
                public bool Missed, Exploded;
            }

            public readonly FleetInterceptorTally Tally = new();
            public readonly List<Ship> Ships = [];
            public readonly List<int> Threats = [];
            public readonly List<Interceptor> Interceptors = []; // BDATargetManager.FiredMissiles.
            public bool FleetDefence = true;
            public int MissilesPerTarget = 1;
            public int LaunchFrames = 5; // Launch routine length (lock, turret slew, rail).
            public float Now;
            public int Launches;
            public Func<int, int, bool> Friendly = (a, b) => a == b;
            readonly List<int> friendly = [];
            readonly Func<int, bool> threatGone;

            public Fleet() { threatGone = t => !Threats.Contains(t); }

            public Ship AddShip(int team) { var s = new Ship { Key = Ships.Count + 1, Team = team }; Ships.Add(s); return s; }

            public void AddThreats(int n) { for (int i = 0; i < n; ++i) Threats.Add(1000 + Threats.Count); }

            public int LiveAt(int threat, int ship = 0)
            {
                int n = 0;
                foreach (var i in Interceptors) if (i.Threat == threat && !i.Missed && !i.Exploded && (ship == 0 || i.Ship == ship)) ++n;
                return n;
            }

            public int LaunchedAt(int threat)
            {
                int n = 0;
                foreach (var i in Interceptors) if (i.Threat == threat) ++n;
                return n;
            }

            public void Frame()
            {
                Now += Dt;
                if (FleetDefence) // FleetPointDefence.EnsureFresh (once per physics frame).
                {
                    Tally.BeginRebuild();
                    foreach (var i in Interceptors)
                        if (Threats.Contains(i.Threat) && FleetDefenceRules.CountsAsInterceptor(true, true, true, true, i.Exploded, i.Missed, false))
                            Tally.AddInterceptor(i.Team, i.Threat, i.Ship);
                    Tally.EndRebuild(Now);
                    Tally.PruneClaims(Now, threatGone);
                }
                foreach (var ship in Ships) Step(ship);
            }

            public void Run(float seconds) { for (float t = 0f; t < seconds; t += Dt) Frame(); }

            void Step(Ship s)
            {
                if (s.Holding) return;
                if (s.BusyFrames > 0)
                {
                    if (--s.BusyFrames > 0) return;
                    if (s.Stall) { s.Holding = true; return; } // The coroutine was stopped: its finally never runs (the launch flag watchdog frees the ship later; the claim must expire).
                    if (s.FailNext)
                    {
                        s.FailNext = false;
                        if (s.Claimed) Tally.ReleaseClaim(s.Key, s.RoutineThreat);
                    }
                    else
                    {
                        Interceptors.Add(new Interceptor { Team = s.Team, Ship = s.Key, Threat = s.RoutineThreat });
                        ++Launches;
                        if (s.Claimed) Tally.MarkLaunched(s.Key, s.RoutineThreat);
                    }
                    s.Claimed = false;
                    return;
                }
                friendly.Clear(); // FleetPointDefence.FriendlyTeams.
                for (int t = 0; t < 8; ++t) if (Friendly(s.Team, t)) friendly.Add(t);
                foreach (int threat in Threats)
                {
                    int own = LiveAt(threat, s.Key);
                    int allied = FleetDefence ? Tally.AlliedInFlight(friendly, threat, s.Key) : 0;
                    int claims = FleetDefence ? Tally.AlliedClaims(friendly, threat, s.Key, Now) : 0;
                    if (GuardFireRules.TargetSaturated(FleetDefenceRules.TeamInterceptors(own, allied, claims, FleetDefence), MissilesPerTarget)) continue;
                    s.BusyFrames = LaunchFrames;
                    s.RoutineThreat = threat;
                    s.Claimed = FleetDefence;
                    if (FleetDefence) Tally.AddClaim(s.Team, threat, s.Key, Now);
                    return;
                }
            }
        }

        [Test]
        public static void Fleet_SameFrameRace_OneInterceptor()
        {
            var fleet = new Fleet();
            fleet.AddShip(0);
            fleet.AddShip(0);
            fleet.AddThreats(1);
            fleet.Frame(); // Both ships decide in the same frame.
            Assert.True(fleet.Ships[0].BusyFrames > 0, "first ship launches");
            Assert.Equal(0, fleet.Ships[1].BusyFrames, "second ship sees the claim");
            fleet.Run(3f);
            Assert.Equal(1, fleet.Launches);
        }

        [Test]
        public static void Fleet_ManyThreatsAndShips_OneInterceptorPerThreat()
        {
            var fleet = new Fleet();
            for (int i = 0; i < 20; ++i) fleet.AddShip(0);
            fleet.AddThreats(50);
            fleet.Run(5f);
            Assert.Equal(50, fleet.Launches);
            foreach (int threat in fleet.Threats) Assert.Equal(1, fleet.LaunchedAt(threat), $"threat {threat}");
        }

        [Test]
        public static void Fleet_MissilesPerTarget2_TwoPerThreatAcrossTheFleet()
        {
            var fleet = new Fleet { MissilesPerTarget = 2 };
            for (int i = 0; i < 3; ++i) fleet.AddShip(0);
            fleet.AddThreats(10);
            fleet.Run(5f);
            Assert.Equal(20, fleet.Launches);
            foreach (int threat in fleet.Threats) Assert.Equal(2, fleet.LaunchedAt(threat), $"threat {threat}");
        }

        [Test]
        public static void Fleet_RefireOnlyAfterAMissOrKill()
        {
            var fleet = new Fleet();
            for (int i = 0; i < 3; ++i) fleet.AddShip(0);
            fleet.AddThreats(2);
            fleet.Run(2f);
            Assert.Equal(2, fleet.Launches);
            fleet.Run(5f);
            Assert.Equal(2, fleet.Launches, "no refire while the interceptors chase");
            fleet.Interceptors[0].Missed = true;
            fleet.Run(2f);
            Assert.Equal(3, fleet.Launches, "one refire after the miss");
            Assert.Equal(1, fleet.LiveAt(fleet.Interceptors[0].Threat));
            fleet.Interceptors[1].Exploded = true; // Destroyed (shot down / detonated short) without killing its threat.
            fleet.Run(2f);
            Assert.Equal(4, fleet.Launches, "one refire after the interceptor is destroyed");
        }

        [Test]
        public static void Fleet_FailedLaunchDoesntBlockTheFleet()
        {
            var fleet = new Fleet();
            var a = fleet.AddShip(0);
            fleet.AddShip(0);
            fleet.AddThreats(1);
            a.FailNext = true;
            fleet.Frame();
            Assert.True(a.Claimed);
            int frames = 0;
            while (fleet.Launches == 0 && frames++ < 100) fleet.Frame();
            Assert.Equal(1, fleet.Launches);
            Assert.True(frames * Dt < 0.5f, $"an ally fired right after the failure ({frames * Dt:F2}s), not after the claim expiry");
            fleet.Run(3f);
            Assert.Equal(1, fleet.Launches);
        }

        [Test]
        public static void Fleet_StoppedRoutineClaimExpires()
        {
            var fleet = new Fleet();
            var a = fleet.AddShip(0);
            fleet.AddShip(0);
            fleet.AddThreats(1);
            a.Stall = true;
            fleet.Run(FleetDefenceRules.ClaimTime - 0.2f);
            Assert.Equal(0, fleet.Launches, "the claim holds while it's live");
            fleet.Run(0.5f);
            Assert.Equal(1, fleet.Launches, "the ally fires once the claim expires");
            Assert.Equal(0, fleet.Tally.ClaimCount, "no leaked claims");
        }

        [Test]
        public static void Fleet_SettingOff_EveryShipFires()
        {
            // Old behaviour: each ship counts only its own interceptors.
            var fleet = new Fleet { FleetDefence = false };
            for (int i = 0; i < 3; ++i) fleet.AddShip(0);
            fleet.AddThreats(4);
            fleet.Run(3f);
            Assert.Equal(12, fleet.Launches);
            foreach (int threat in fleet.Threats) Assert.Equal(3, fleet.LaunchedAt(threat));
            Assert.Equal(0, fleet.Tally.ClaimCount, "no claims with the setting off");
        }

        [Test]
        public static void Fleet_OtherTeamsFireTheirOwn()
        {
            var fleet = new Fleet();
            fleet.AddShip(0);
            fleet.AddShip(0);
            fleet.AddShip(1);
            fleet.AddShip(1);
            fleet.AddThreats(3); // Missiles both teams engage (e.g. a third team's).
            fleet.Run(3f);
            foreach (int threat in fleet.Threats) Assert.Equal(2, fleet.LaunchedAt(threat), "one per team");
            fleet.Friendly = (a, b) => a == b || (a <= 1 && b <= 1); // Allied teams.
            fleet.AddThreats(3);
            fleet.Run(3f);
            for (int i = 3; i < 6; ++i) Assert.Equal(1, fleet.LaunchedAt(fleet.Threats[i]), "allied teams share");
        }

        [Test]
        public static void Fleet_DeadThreatClaimsDontLeak()
        {
            var fleet = new Fleet { LaunchFrames = 50 };
            for (int i = 0; i < 4; ++i) fleet.AddShip(0);
            fleet.AddThreats(4);
            fleet.Frame();
            Assert.Equal(4, fleet.Tally.ClaimCount);
            fleet.Threats.Clear(); // All killed by guns.
            fleet.Frame();
            Assert.Equal(0, fleet.Tally.ClaimCount);
        }

        [Test]
        public static void Tally_NoAllocationsOnceWarm()
        {
            // 20 ships x 50 threats, 100 interceptors in flight: rebuild every frame and query every threat for every ship.
            var tally = new FleetInterceptorTally();
            var team = Teams(0, 2);
            Func<int, bool> gone = t => false;
            void Frame(float now)
            {
                tally.BeginRebuild();
                for (int i = 0; i < 100; ++i) tally.AddInterceptor(i % 3, 1000 + i % 50, 1 + i % 20);
                tally.EndRebuild(now);
                tally.PruneClaims(now, gone);
                int sum = 0;
                for (int ship = 1; ship <= 20; ++ship)
                    for (int threat = 1000; threat < 1050; ++threat)
                        sum += tally.AlliedInFlight(team, threat, ship) + tally.AlliedClaims(team, threat, ship, now) + tally.CoveringShip(team, threat, ship, now);
                for (int ship = 1; ship <= 20; ++ship) { tally.AddClaim(0, 1000 + ship, ship, now); if ((ship & 1) == 1) tally.ReleaseClaim(ship, 1000 + ship); } // Half refresh their claim, half release it.
                if (sum < 0) throw new Exception();
            }
            for (int f = 0; f < 10; ++f) Frame(f * Dt);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 10; f < 200; ++f) Frame(f * Dt);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated, "bytes allocated per 190 frames");
        }
    }
}
