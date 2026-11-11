using System;
using System.Collections.Generic;
using BDArmory.Control;

namespace OverdriveTests
{
    /// <summary>
    /// Guard mode missile fire: the launch flag, missile quotas, weapon type readiness, point defence interceptor retries and the debug log rate limit.
    /// The "one missile, then never again" repro is modelled with the same pure classes MissileFire uses (GuardFiringFlag, GuardFireRules, InterceptorAttemptMemory).
    /// </summary>
    public static class GuardFireLogicTests
    {
        const float Dt = 0.02f; // Physics frame.

        // ---- The launch flag: "one missile, then never again" (serial and parallel share the flag) ----

        [Test]
        public static void Flag_RoutineTakesAndReleases()
        {
            var owner = new GuardFiringFlag();
            bool flag = false;
            Assert.True(owner.TryBegin(ref flag, 0f, out int id));
            Assert.True(flag);
            Assert.False(owner.TryBegin(ref flag, 0.1f, out _), "second routine must wait");
            Assert.True(owner.End(ref flag, id));
            Assert.False(flag);
            Assert.True(owner.TryBegin(ref flag, 0.2f, out _), "the next launch can start");
        }

        [Test]
        public static void Repro_ModularMissileHoldLapsesAfterGuidanceEnds()
        {
            // Upstream: BDModularGuidance set guardFiringMissile every frame while guiding and nothing ever cleared it -> no scan, no point defence, no launch, forever.
            var owner = new GuardFiringFlag();
            bool flag = false;
            float t = 0f;
            Assert.True(owner.TryBegin(ref flag, t, out int id));
            Assert.True(owner.End(ref flag, id)); // The launch routine fired the modular missile and finished.
            for (; t < 10f; t += Dt) // The missile guides for 10s, refreshing its hold every frame.
            {
                owner.Hold(ref flag, t);
                Assert.Equal(GuardFiringFlagAction.Keep, owner.Check(ref flag, t, 30f));
                Assert.True(flag);
            }
            // The missile hits (or dies): no more refreshes.
            GuardFiringFlagAction action = GuardFiringFlagAction.Keep;
            float released = -1f;
            for (float u = t; u < t + 2f && released < 0f; u += Dt)
            {
                action = owner.Check(ref flag, u, 30f);
                if (action != GuardFiringFlagAction.Keep) released = u;
            }
            Assert.Equal(GuardFiringFlagAction.ReleaseExpiredHold, action);
            Assert.True(released - t <= GuardFireRules.ModularGuidanceHoldTime + 2 * Dt, $"released after {released - t}s");
            Assert.False(flag);
            Assert.True(owner.TryBegin(ref flag, released, out _), "guard mode fires again");
        }

        [Test]
        public static void Repro_ModularHoldDoesntReleaseARunningRoutine()
        {
            var owner = new GuardFiringFlag();
            bool flag = false;
            Assert.True(owner.TryBegin(ref flag, 0f, out int id));
            owner.Hold(ref flag, 0f);
            owner.ReleaseHold(ref flag); // An old modular missile resets while a new launch routine runs.
            Assert.True(flag, "the routine keeps the flag");
            Assert.Equal(GuardFiringFlagAction.Keep, owner.Check(ref flag, 5f, 30f));
            Assert.True(owner.End(ref flag, id));
            Assert.False(flag);
        }

        [Test]
        public static void Repro_StalledRoutineIsReleasedAndSurvivesGuardToggle()
        {
            // A launch routine that stops without its clean-up (stopped coroutine, a nested coroutine that threw) used to hold the flag forever.
            // Toggling guard mode doesn't touch the flag (ToggleGuardMode resets the weapon selection and targets only), so only the watchdog can recover.
            var owner = new GuardFiringFlag();
            bool flag = false;
            float stall = GuardFireRules.StallTimeout(1f);
            Assert.True(owner.TryBegin(ref flag, 0f, out int stale));
            bool guardMode = true;
            guardMode = !guardMode; guardMode = !guardMode; // Toggle off and on: the flag is untouched.
            Assert.True(guardMode && flag);
            Assert.Equal(GuardFiringFlagAction.Keep, owner.Check(ref flag, stall - 1f, stall));
            Assert.False(owner.TryBegin(ref flag, stall - 1f, out _));
            Assert.Equal(GuardFiringFlagAction.ReleaseStalledRoutine, owner.Check(ref flag, stall + 1f, stall));
            Assert.False(flag);
            Assert.True(owner.Superseded(stale), "the stalled routine stops if it ever resumes");
            Assert.True(owner.TryBegin(ref flag, stall + 1f, out int fresh));
            Assert.False(owner.End(ref flag, stale), "the stalled routine can't release the new routine's flag");
            Assert.True(flag);
            Assert.True(owner.End(ref flag, fresh));
        }

        [Test]
        public static void Flag_ProgressKeepsALongRoutineAlive()
        {
            var owner = new GuardFiringFlag();
            bool flag = false;
            float stall = GuardFireRules.StallTimeout(1f);
            Assert.True(owner.TryBegin(ref flag, 0f, out int id));
            for (float t = 0f; t < 3f * stall; t += 1f) // A long bombing run that resumes every second.
            {
                owner.MarkProgress(id, t);
                Assert.Equal(GuardFiringFlagAction.Keep, owner.Check(ref flag, t + 0.5f, stall));
            }
            Assert.True(flag);
        }

        [Test]
        public static void Repro_ExceptionInRoutineReleasesFlag()
        {
            // GuardMissileRoutine had no try/finally: an exception in the routine left guardFiringMissile set.
            var owner = new GuardFiringFlag();
            bool flag = false;
            var routine = RoutineThatThrows(owner, () => flag, v => flag = v);
            Assert.Throws<InvalidOperationException>(() => { while (routine.MoveNext()) { } });
            Assert.False(flag, "released by the finally block");
            Assert.False(owner.RoutineActive);
        }

        static IEnumerator<object> RoutineThatThrows(GuardFiringFlag owner, Func<bool> get, Action<bool> set)
        {
            bool flag = get();
            if (!owner.TryBegin(ref flag, 0f, out int id)) yield break;
            set(flag);
            try
            {
                yield return null; // Waiting for a lock...
                throw new InvalidOperationException("e.g. targetParts index out of range");
            }
            finally
            {
                flag = get();
                owner.End(ref flag, id);
                set(flag);
            }
        }

        [Test] public static void StallTimeout_AtLeast30s() => Assert.Near(30f, GuardFireRules.StallTimeout(1f), 1e-6);
        [Test] public static void StallTimeout_ScalesWithLongIntervals() => Assert.Near(60f, GuardFireRules.StallTimeout(20f), 1e-6);
        [Test] public static void StallTimeout_NaNIsSafe() => Assert.Near(30f, GuardFireRules.StallTimeout(float.NaN), 1e-6);

        // ---- Missiles away, quotas and re-engagement ----

        [Test]
        public static void Repro_SerialFiresAgainOnceTheMissileResolves()
        {
            // Defaults: Missiles/Tgt 1, Max Missile Tgts 1. One missile away at the target -> wait; once it misses, explodes or its target dies -> fire again.
            Assert.Equal(GuardFireBlock.None, Decide(firedMissiles: 0, engaged: 0));
            int away = Away(targetAlive: true, hasExploded: false, hasMissed: false);
            Assert.Equal(GuardFireBlock.QuotaFull, Decide(firedMissiles: away, engaged: 1));
            foreach (var (alive, exploded, missed) in new[] { (true, false, true), (true, true, false), (false, false, false) })
            {
                away = Away(alive, exploded, missed);
                Assert.Equal(0, away, $"alive={alive} exploded={exploded} missed={missed}");
                Assert.Equal(GuardFireBlock.None, Decide(firedMissiles: away, engaged: 0), "fires again on the next scan");
            }
        }

        static int Away(bool targetAlive, bool hasExploded, bool hasMissed)
            => GuardFireRules.CountsAsMissileAway(targetAlive, true, true, hasExploded, hasMissed, false) ? 1 : 0;

        static GuardFireBlock Decide(int firedMissiles, int engaged, int maxPerTarget = 1, int maxTargets = 1, bool flag = false, bool hasMissile = true, bool hold = false, bool checks = true, bool radar = true)
            => GuardFireRules.SerialLaunchDecision(flag, true, hasMissile, hold, firedMissiles, maxPerTarget, engaged, maxTargets, checks, radar);

        [Test]
        public static void TargetDiesBeforeMissilesArrive_QuotaReleased()
        {
            Assert.False(GuardFireRules.CountsAsMissileAway(false, true, true, false, false, false));
            Assert.Equal(GuardFireBlock.None, Decide(firedMissiles: 0, engaged: 0));
        }

        [Test]
        public static void MissileMissesOrSelfDestructs_QuotaReleased()
        {
            Assert.False(GuardFireRules.CountsAsMissileAway(true, true, true, false, true, false), "missed");
            Assert.False(GuardFireRules.CountsAsMissileAway(true, true, true, true, false, false), "exploded/self-destructed");
            Assert.True(GuardFireRules.CountsAsMissileAway(true, true, false, false, true, true), "bombs keep counting until they explode");
            Assert.False(GuardFireRules.CountsAsMissileAway(true, false, false, false, false, false), "not fired");
        }

        [Test]
        public static void InterceptorsInFlightDontBlockVesselTargets()
        {
            // With Max Missile Tgts 1, one interceptor chasing an incoming missile used to count as an engaged target and stop all fire at aircraft and ships.
            var away = new List<(bool isMissile, int own)> { (true, 1), (true, 2) };
            int engaged = 0;
            foreach (var (isMissile, own) in away) if (GuardFireRules.CountsAsEngagedTarget(isMissile, own)) ++engaged;
            Assert.Equal(0, engaged);
            Assert.Equal(GuardFireBlock.None, Decide(firedMissiles: 0, engaged: engaged));
            Assert.True(GuardFireRules.CountsAsEngagedTarget(false, 1));
            Assert.False(GuardFireRules.CountsAsEngagedTarget(false, 0), "missiles we only guide for others don't count");
        }

        [Test]
        public static void MaxMissileTargets_NewTargetWaits_SameTargetDoesnt()
        {
            Assert.Equal(GuardFireBlock.MaxTargetsEngaged, Decide(firedMissiles: 0, engaged: 2, maxPerTarget: 4, maxTargets: 2));
            Assert.Equal(GuardFireBlock.None, Decide(firedMissiles: 1, engaged: 2, maxPerTarget: 4, maxTargets: 2), "more missiles at an engaged target");
            Assert.Equal(GuardFireBlock.None, Decide(firedMissiles: 0, engaged: 1, maxPerTarget: 4, maxTargets: 2));
        }

        [Test]
        public static void SerialDecision_ReasonOrder()
        {
            Assert.Equal(GuardFireBlock.RoutineBusy, Decide(0, 0, flag: true, hasMissile: false, hold: true));
            Assert.Equal(GuardFireBlock.NoTarget, GuardFireRules.SerialLaunchDecision(false, false, true, false, 0, 1, 0, 1, true, true));
            Assert.Equal(GuardFireBlock.NoWeapon, Decide(0, 0, hasMissile: false, hold: true));
            Assert.Equal(GuardFireBlock.AuthorityHold, Decide(1, 1, hold: true));
            Assert.Equal(GuardFireBlock.QuotaFull, Decide(1, 1, checks: false));
            Assert.Equal(GuardFireBlock.NotAuthorized, Decide(0, 0, checks: false, radar: false));
            Assert.Equal(GuardFireBlock.NoRadarLock, Decide(0, 0, radar: false));
        }

        [Test]
        public static void FireAuthorityFree_NoExternalFilter_NeverBlocks()
        {
            // F3 defaults to Free and no ExternalTargetFilter is set: nothing from the hooks can stop guard fire.
            foreach (var missile in new[] { false, true })
            foreach (var assigned in new[] { false, true })
            {
                Assert.True(FireAuthorityRules.PermitsFire(FireAuthority.Free, missile, assigned, false, false));
                Assert.False(FireAuthorityRules.ExcludedFromSelection(FireAuthority.Free, missile, assigned, false, false));
            }
            Assert.Equal(GuardFireBlock.None, Decide(0, 0, hold: false));
        }

        [Test]
        public static void Parallel_QuotaAndSlots()
        {
            Assert.Equal(GuardFireBlock.None, GuardFireRules.ParallelTargetCheck(0, 2, 0, 1));
            Assert.Equal(GuardFireBlock.QuotaFull, GuardFireRules.ParallelTargetCheck(2, 2, 1, 1));
            Assert.Equal(GuardFireBlock.MaxTargetsEngaged, GuardFireRules.ParallelTargetCheck(0, 2, 1, 1));
            Assert.Equal(GuardFireBlock.None, GuardFireRules.ParallelTargetCheck(1, 2, 1, 1), "top up an engaged target");
            Assert.Equal(2, GuardFireRules.SalvoSlots(0, 4, 2));
            Assert.Equal(1, GuardFireRules.SalvoSlots(1, 4, 2));
            Assert.Equal(0, GuardFireRules.SalvoSlots(2, 4, 2));
            Assert.Equal(1, GuardFireRules.SalvoSlots(-3, 1, 1), "negative counts are clamped");
        }

        [Test]
        public static void Repro_ParallelSalvoFiresAgainAfterMiss()
        {
            int away = 2; // A salvo of 2 at the target (Missiles/Tgt 2).
            Assert.Equal(GuardFireBlock.QuotaFull, GuardFireRules.ParallelTargetCheck(away, 2, 1, 1));
            away -= Away(true, false, true) == 0 ? 1 : 0; // One misses.
            Assert.Equal(GuardFireBlock.None, GuardFireRules.ParallelTargetCheck(away, 2, 1, 1));
            Assert.Equal(1, GuardFireRules.SalvoSlots(away, 2, 2));
        }

        // ---- Parallel salvo size (Salvo Size/Tgt) ----

        // The parallel salvo's launch loop for one target, reduced to the counting: salvo size clamped to Missiles/Tgt, slots from missiles away,
        // then launchers picked in turn; a multi-missile launcher fires its share (and is busy until its salvo has rippled out), a single rail fires one.
        static List<int> SimulateSalvo(int salvoSize, int missilesPerTarget, int away, int[] launcherTubes, bool multiLaunchers)
        {
            int maxPerTarget = Math.Max(1, missilesPerTarget);
            int size = Math.Clamp(salvoSize, 1, maxPerTarget);
            int slots = GuardFireRules.SalvoSlots(away, size, maxPerTarget);
            var shots = new List<int>();
            var ready = new List<int>(launcherTubes);
            int fired = 0;
            while (fired < slots && ready.Count > 0)
            {
                int tubes = ready[0];
                ready.RemoveAt(0); // Fired (or empty): no longer a candidate in this salvo.
                int share = multiLaunchers ? GuardFireRules.LauncherSalvoShare(slots - fired, ready.Count + 1, tubes) : (tubes > 0 ? 1 : 0);
                if (share == 0) continue;
                shots.Add(share);
                fired += share;
            }
            return shots;
        }

        static int Sum(List<int> shots) { int s = 0; foreach (var x in shots) s += x; return s; }

        [Test]
        public static void Salvo_SingleVlsFiresMinOfSalvoCellsAndQuota()
        {
            // Was capped at one missile per launcher: one VLS fired one missile per target whatever the salvo size.
            for (int n = 1; n <= 6; ++n)
                for (int cells = 1; cells <= 8; ++cells)
                    for (int quota = 1; quota <= 6; ++quota)
                    {
                        var shots = SimulateSalvo(n, quota, 0, [cells], multiLaunchers: true);
                        int expected = Math.Min(n, Math.Min(cells, quota));
                        Assert.Equal(expected, Sum(shots), $"salvo {n}, cells {cells}, Missiles/Tgt {quota}");
                        Assert.Equal(1, shots.Count, "one launch of the whole share");
                    }
        }

        [Test]
        public static void Salvo_SpreadsOverSeveralLaunchers()
        {
            Assert.Equal("2,2,2", string.Join(",", SimulateSalvo(6, 6, 0, [8, 8, 8], true)));
            Assert.Equal("3,2,2", string.Join(",", SimulateSalvo(7, 8, 0, [8, 8, 8], true)));
            Assert.Equal("1,1", string.Join(",", SimulateSalvo(2, 2, 0, [8, 8, 8], true)), "fewer missiles than launchers");
            Assert.Equal("1,3", string.Join(",", SimulateSalvo(4, 4, 0, [1, 8], true)), "an almost empty launcher fires what it has, the next one makes up the rest");
            Assert.Equal("4", string.Join(",", SimulateSalvo(4, 4, 0, [0, 8], true)), "an empty launcher is skipped");
        }

        [Test]
        public static void Salvo_SeparateCellsOneEach()
        {
            Assert.Equal(4, Sum(SimulateSalvo(4, 4, 0, [1, 1, 1, 1, 1, 1], false)));
            Assert.Equal(3, Sum(SimulateSalvo(4, 4, 0, [1, 1, 1], false)), "only 3 cells");
            Assert.Equal(2, Sum(SimulateSalvo(4, 4, 2, [1, 1, 1, 1], false)), "2 already away");
            Assert.Equal(1, Sum(SimulateSalvo(4, 1, 0, [1, 1, 1, 1], false)), "Missiles/Tgt 1 caps the salvo");
        }

        [Test]
        public static void LauncherSalvoShare_Edges()
        {
            Assert.Equal(0, GuardFireRules.LauncherSalvoShare(3, 1, 0));
            Assert.Equal(0, GuardFireRules.LauncherSalvoShare(0, 1, 8));
            Assert.Equal(3, GuardFireRules.LauncherSalvoShare(3, 0, 8), "no other launchers counted");
            Assert.Equal(2, GuardFireRules.LauncherSalvoShare(5, 3, 8));
            Assert.Equal(1, GuardFireRules.LauncherSalvoShare(5, 3, 1));
        }

        [Test]
        public static void QueuedLaunches_ClearedAfterTimeoutOnly()
        {
            Assert.False(GuardFireRules.QueuedLaunchesStale(true, 10f, 0f));
            Assert.True(GuardFireRules.QueuedLaunchesStale(true, 20.5f, 0f));
            Assert.False(GuardFireRules.QueuedLaunchesStale(false, 100f, 0f), "nothing to clear");
        }

        // ---- Weapon type readiness (switching types) ----

        [Test]
        public static void ExhaustedType_NotReady_EvenWithOtherTypesLeft()
        {
            // Upstream accepted a fired launcher (VLS cell, multi-launcher, reloadable rail) when an unfired missile of a *different* type was on board,
            // so the empty type kept being selected (CurrentMissile null or fired) and guard mode never switched to the type that had missiles.
            Assert.False(GuardFireRules.MissileTypeReady(true, launched: true, sameTypeUnfiredOnBoard: false));
            Assert.True(GuardFireRules.MissileTypeReady(true, launched: true, sameTypeUnfiredOnBoard: true), "another cell of the same type");
            Assert.True(GuardFireRules.MissileTypeReady(true, launched: false, sameTypeUnfiredOnBoard: false));
            Assert.False(GuardFireRules.MissileTypeReady(false, launched: false, sameTypeUnfiredOnBoard: false), "other weapon channel");
        }

        [Test]
        public static void MixedInventory_EmptyTypeFallsThroughToNextType()
        {
            // Two SAM types; the preferred one is empty: the pick must land on the other one.
            var inventory = new[] { new Sam("ESSM", 1000, 30000, unfired: 0, preference: 2), new Sam("SM-2", 3000, 90000, unfired: 8, preference: 1) };
            Assert.Equal("SM-2", Pick(inventory, 20000));
        }

        [Test]
        public static void TargetBeyondOneTypeButInsideAnother()
        {
            var inventory = new[] { new Sam("RAM", 500, 9000, 21, preference: 3), new Sam("ESSM", 1000, 30000, 16, preference: 2), new Sam("SM-2", 3000, 90000, 8, preference: 1) };
            Assert.Equal("RAM", Pick(inventory, 5000));
            Assert.Equal("ESSM", Pick(inventory, 20000), "beyond RAM, inside ESSM");
            Assert.Equal("SM-2", Pick(inventory, 60000), "only SM-2 reaches");
            Assert.Equal(null, Pick(inventory, 200000), "nothing reaches");
        }

        sealed class Sam(string name, float min, float max, int unfired, int preference)
        {
            public readonly string Name = name;
            public readonly float Min = min, Max = max;
            public readonly int Unfired = unfired, Preference = preference;
        }

        // A model of the guard weapon pick for missiles: types that are ready and in range, highest preference first.
        static string Pick(Sam[] inventory, float distance)
        {
            Sam best = null;
            foreach (var sam in inventory)
            {
                // Each type has a fired launcher still on board (VLS): readiness depends on unfired missiles of the same type only.
                if (!GuardFireRules.MissileTypeReady(true, launched: true, sameTypeUnfiredOnBoard: sam.Unfired > 0)) continue;
                if (distance < sam.Min || distance > sam.Max) continue;
                if (best == null || sam.Preference > best.Preference) best = sam;
            }
            return best?.Name;
        }

        // ---- Point defence: interceptor retries, missile targets, 100+ incoming ----

        [Test]
        public static void PD_FailedInterceptorDoesntBlockTheOthers()
        {
            // Point defence picks the first usable interceptor every 0.1s. One that passed the quick checks but couldn't launch (clearance, rail, turret, lock) was picked again forever.
            var mem = new InterceptorAttemptMemory();
            var interceptors = new[] { (id: 1, type: 100, min: 500f), (id: 2, type: 100, min: 500f), (id: 3, type: 200, min: 1000f) };
            const int target = 42;
            float t = 0f;
            int first = PickInterceptor(mem, interceptors, target, 5000f, t, out _);
            Assert.Equal(1, first);
            mem.RecordFailure(1, target, t, GuardFireRules.InterceptorInstanceRetryCooldown); // Nothing left the rail.
            mem.RecordFailure(-100, target, t, GuardFireRules.InterceptorRetryCooldown); // Its type (types use a separate memory in game).
            t += 0.1f;
            Assert.Equal(3, PickInterceptor(mem, interceptors, target, 5000f, t, out int skipped), "the other type gets a turn");
            Assert.Equal(2, skipped);
            t = GuardFireRules.InterceptorRetryCooldown + 0.1f;
            Assert.Equal(2, PickInterceptor(mem, interceptors, target, 5000f, t, out _), "after the type cooldown, the type's other missile");
            mem.Forget(1, target);
            Assert.Equal(1, PickInterceptor(mem, interceptors, target, 5000f, t, out _), "forgotten once it launches");
        }

        static int PickInterceptor(InterceptorAttemptMemory mem, (int id, int type, float min)[] interceptors, int target, float distance, float now, out int skipped)
        {
            skipped = 0;
            foreach (var (id, type, min) in interceptors)
            {
                bool failed = mem.IsBlocked(id, target, now) || mem.IsBlocked(-type, target, now);
                switch (GuardFireRules.CheckInterceptor(failed, distance, min))
                {
                    case InterceptorPrecheck.RecentlyFailed: ++skipped; continue;
                    case InterceptorPrecheck.TooClose: continue;
                }
                return id;
            }
            return -1;
        }

        [Test]
        public static void PD_TooCloseForMinRange()
        {
            Assert.Equal(InterceptorPrecheck.TooClose, GuardFireRules.CheckInterceptor(false, 400f, 500f));
            Assert.Equal(InterceptorPrecheck.Usable, GuardFireRules.CheckInterceptor(false, 600f, 500f));
            Assert.Equal(InterceptorPrecheck.RecentlyFailed, GuardFireRules.CheckInterceptor(true, 600f, 500f));
        }

        [Test]
        public static void PD_HundredsOfIncomingMissiles_RotationNeverStalls()
        {
            const int n = 150;
            var away = new int[n];
            int max = 1, start = 0, fired = 0;
            // Each 0.1s scan fires one interceptor at the next missile without one (Missiles/Tgt 1), wrapping round.
            for (int scan = 0; scan < n; ++scan)
            {
                int next = GuardFireRules.NextUnsaturatedIndex(n, start, i => GuardFireRules.TargetSaturated(away[i], max));
                Assert.True(next >= 0, $"scan {scan}");
                ++away[next];
                ++fired;
                start = next + 1;
            }
            Assert.Equal(n, fired);
            Assert.Equal(-1, GuardFireRules.NextUnsaturatedIndex(n, start, i => GuardFireRules.TargetSaturated(away[i], max)), "all engaged");
            away[73] = 0; // One interceptor misses.
            Assert.Equal(73, GuardFireRules.NextUnsaturatedIndex(n, 140, i => GuardFireRules.TargetSaturated(away[i], max)), "re-engaged at once");
        }

        [Test]
        public static void PD_NextUnsaturatedIndex_Edges()
        {
            Assert.Equal(-1, GuardFireRules.NextUnsaturatedIndex(0, 0, _ => false));
            Assert.Equal(0, GuardFireRules.NextUnsaturatedIndex(3, 7, _ => false), "out of range start wraps to 0");
            Assert.Equal(1, GuardFireRules.NextUnsaturatedIndex(3, 2, i => i != 1));
            Assert.Equal(-1, GuardFireRules.NextUnsaturatedIndex(3, 0, null));
        }

        [Test]
        public static void PD_MemoryWithManyTargets_PrunesExpired()
        {
            var mem = new InterceptorAttemptMemory();
            for (int target = 0; target < 120; ++target)
                for (int interceptor = 0; interceptor < 3; ++interceptor)
                    mem.RecordFailure(interceptor, target, 0f, 5f);
            Assert.Equal(360, mem.Count);
            Assert.True(mem.IsBlocked(2, 119, 4.9f));
            mem.PruneIfDue(4f);
            Assert.Equal(360, mem.Count, "not expired yet");
            mem.PruneIfDue(4.5f);
            Assert.Equal(360, mem.Count, "pruned at most once per second");
            mem.PruneIfDue(5.1f);
            Assert.Equal(0, mem.Count);
            Assert.False(mem.IsBlocked(2, 119, 5.1f));
        }

        [Test]
        public static void PD_MemoryKeysDontCollide()
        {
            var mem = new InterceptorAttemptMemory();
            mem.RecordFailure(1, 2, 0f);
            Assert.True(mem.IsBlocked(1, 2, 1f));
            Assert.False(mem.IsBlocked(2, 1, 1f));
            Assert.False(mem.IsBlocked(1, -2, 1f));
            mem.RecordFailure(-5, int.MinValue, 0f);
            Assert.True(mem.IsBlocked(-5, int.MinValue, 1f));
            Assert.False(mem.IsBlocked(-5, int.MaxValue, 1f));
        }

        [Test]
        public static void MissileTargetsOnly_KeepSkippingSaturatedMissiles()
        {
            // After the launch platforms die, only incoming missiles are left: keep skipping the saturated ones while some missile has room.
            Assert.False(GuardFireRules.ResetSaturatedExclusions(0, 0, 10, 4));
            Assert.True(GuardFireRules.ResetSaturatedExclusions(0, 0, 10, 10));
            Assert.True(GuardFireRules.ResetSaturatedExclusions(3, 3, 10, 0), "with vessels, only vessels count");
            Assert.False(GuardFireRules.ResetSaturatedExclusions(3, 2, 10, 10));
        }

        // ---- Debug log rate limit ----

        [Test]
        public static void ReasonLog_LogsChangesAndRepeatsSlowly()
        {
            var log = new GuardFireReasonLog();
            Assert.True(log.ShouldLog(GuardFireChannel.GuardScan, GuardFireBlock.QuotaFull, 0f));
            Assert.False(log.ShouldLog(GuardFireChannel.GuardScan, GuardFireBlock.QuotaFull, 1f));
            Assert.True(log.ShouldLog(GuardFireChannel.GuardScan, GuardFireBlock.NoRadarLock, 1.1f), "reason changed");
            Assert.True(log.ShouldLog(GuardFireChannel.PointDefence, GuardFireBlock.NoRadarLock, 1.2f), "channels are separate");
            Assert.True(log.ShouldLog(GuardFireChannel.GuardScan, GuardFireBlock.NoRadarLock, 6.2f), "repeated after 5s");
            log.Reset(GuardFireChannel.GuardScan);
            Assert.True(log.ShouldLog(GuardFireChannel.GuardScan, GuardFireBlock.NoRadarLock, 6.3f));
        }

        [Test]
        public static void Describe_EveryReasonHasText()
        {
            foreach (GuardFireBlock reason in Enum.GetValues(typeof(GuardFireBlock)))
                Assert.True(!string.IsNullOrEmpty(GuardFireRules.Describe(reason)) && GuardFireRules.Describe(reason) != reason.ToString(), reason.ToString());
        }
    }
}
