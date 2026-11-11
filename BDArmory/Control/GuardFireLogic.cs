// BDArmory Overdrive: guard mode missile fire decisions (missile quotas, the firing-routine flag, interceptor retries, diagnostics).
// Pure logic, no Unity dependencies, so it can be unit tested outside KSP (see Overdrive/tests).
using System;
using System.Collections.Generic;

namespace BDArmory.Control
{
    /// <summary>
    /// Why guard mode didn't fire (or None when it fired or started a launch routine). Logged with DEBUG_GUARD_FIRE.
    /// </summary>
    public enum GuardFireBlock
    {
        None = 0,
        /// <summary>No target, or no hostile in range that a weapon can engage.</summary>
        NoTarget,
        /// <summary>A target, but no weapon selected for it.</summary>
        NoWeapon,
        /// <summary>Fire authority is Hold (Overdrive F3).</summary>
        AuthorityHold,
        /// <summary>A missile routine (or a guided modular missile) is holding the launch flag.</summary>
        RoutineBusy,
        /// <summary>Missiles/Tgt reached for the target (all targets, for point defence).</summary>
        QuotaFull,
        /// <summary>Max Missile Tgts already engaged and the target would be a new one.</summary>
        MaxTargetsEngaged,
        /// <summary>The launch checks failed: range/DLZ, guard angle, terrain, boresight, depth, ...</summary>
        NotAuthorized,
        /// <summary>A radar-guided missile needs a lock and no lock slot or lock is available.</summary>
        NoRadarLock,
        /// <summary>Point defence: no incoming missiles to engage.</summary>
        NoThreats,
        /// <summary>Point defence: no interceptor can engage any of the incoming missiles right now (range, seeker, lock, boresight, recent failures).</summary>
        NoInterceptor,
    }

    /// <summary>
    /// The guard mode paths that decide to fire, for rate-limiting the debug log per path.
    /// </summary>
    public enum GuardFireChannel
    {
        GuardScan = 0,
        ParallelSalvo = 1,
        PointDefence = 2,
    }

    /// <summary>
    /// What to do with MissileFire.guardFiringMissile this frame.
    /// </summary>
    public enum GuardFiringFlagAction
    {
        /// <summary>Leave the flag as it is.</summary>
        Keep = 0,
        /// <summary>A launch routine owns the flag but hasn't made progress for longer than the stall timeout (e.g. it was stopped without its clean-up): release it.</summary>
        ReleaseStalledRoutine,
        /// <summary>The flag was set outside a launch routine (a guided modular missile refreshes a hold every frame) and the hold has lapsed (the missile is gone): release it.</summary>
        ReleaseExpiredHold,
    }

    /// <summary>
    /// Result of the cheap per-interceptor checks done by point defence before the seeker/lock/boresight checks.
    /// </summary>
    public enum InterceptorPrecheck
    {
        Usable = 0,
        /// <summary>This interceptor recently failed to launch at this target (rail/clearance/lock/turret): try the other interceptors first.</summary>
        RecentlyFailed,
        /// <summary>The target is inside the interceptor's minimum engagement range.</summary>
        TooClose,
    }

    public static class GuardFireRules
    {
        /// <summary>How long an (interceptor type, target) pair that failed to launch is skipped by point defence, so the other types get a turn.</summary>
        public const float InterceptorRetryCooldown = 5f;

        /// <summary>How long an (interceptor, target) pair that failed to launch is skipped by point defence (longer than the type, so the type's other missiles are tried next).</summary>
        public const float InterceptorInstanceRetryCooldown = 15f;

        /// <summary>How long a guided modular missile's hold on the launch flag lasts without being refreshed (it is refreshed every physics frame while it guides).</summary>
        public const float ModularGuidanceHoldTime = 0.5f;

        /// <summary>The time after which stale queued launches (MML salvos / reloadable rails that never spawned) are dropped.</summary>
        public const float QueuedLaunchTimeout = 20f;

        #region Missiles/Tgt and Max Missile Tgts
        /// <summary>Whether the target already has its allowance of missiles (missiles away + queued launches).</summary>
        public static bool TargetSaturated(int missilesAway, int maxPerTarget) => missilesAway >= Math.Max(1, maxPerTarget);

        /// <summary>
        /// The parallel salvo's per-target quota checks (Missiles/Tgt, then Max Missile Tgts for a target without missiles yet).
        /// </summary>
        public static GuardFireBlock ParallelTargetCheck(int missilesAway, int maxPerTarget, int engagedTargets, int maxEngagedTargets)
        {
            if (TargetSaturated(missilesAway, maxPerTarget)) return GuardFireBlock.QuotaFull;
            if (missilesAway <= 0 && engagedTargets >= Math.Max(1, maxEngagedTargets)) return GuardFireBlock.MaxTargetsEngaged;
            return GuardFireBlock.None;
        }

        /// <summary>
        /// Whether a target counts towards Max Missile Tgts (MissileFire.engagedTargets): a vessel with our own missiles away or queued.
        /// Interceptors chasing incoming missiles don't count, so point defence fire doesn't stop guard mode engaging aircraft and ships
        /// (with the default Max Missile Tgts of 1, a single interceptor in flight blocked every new vessel target).
        /// </summary>
        public static bool CountsAsEngagedTarget(bool targetIsMissile, int ownMissilesAwayOrQueued) => !targetIsMissile && ownMissilesAwayOrQueued > 0;

        /// <summary>
        /// The guard scan's single-missile launch decision (serial fire), in the order GuardMode checks it. None means start the launch routine.
        /// </summary>
        /// <param name="flagHeld">guardFiringMissile (a launch routine or a guided modular missile holds it).</param>
        /// <param name="hasTarget">A guard target was found.</param>
        /// <param name="hasMissile">A ready missile of the selected type (CurrentMissile).</param>
        /// <param name="authorityHold">Fire authority is Hold.</param>
        /// <param name="firedMissiles">Missiles away and queued at the current target.</param>
        /// <param name="maxPerTarget">Missiles/Tgt.</param>
        /// <param name="engagedTargets">Targets with our missiles away or queued (see CountsAsEngagedTarget).</param>
        /// <param name="maxEngagedTargets">Max Missile Tgts.</param>
        /// <param name="launchChecksPassed">The launch checks (range/DLZ, guard angle, terrain, depth, boresight, ...) passed.</param>
        /// <param name="radarLockAvailable">The missile doesn't need a radar lock, or one is available.</param>
        public static GuardFireBlock SerialLaunchDecision(bool flagHeld, bool hasTarget, bool hasMissile, bool authorityHold, int firedMissiles, int maxPerTarget, int engagedTargets, int maxEngagedTargets, bool launchChecksPassed, bool radarLockAvailable)
        {
            if (flagHeld) return GuardFireBlock.RoutineBusy;
            if (!hasTarget) return GuardFireBlock.NoTarget;
            if (!hasMissile) return GuardFireBlock.NoWeapon;
            if (authorityHold) return GuardFireBlock.AuthorityHold;
            if (TargetSaturated(firedMissiles, maxPerTarget)) return GuardFireBlock.QuotaFull;
            if (firedMissiles <= 0 && engagedTargets >= Math.Max(1, maxEngagedTargets)) return GuardFireBlock.MaxTargetsEngaged;
            if (!launchChecksPassed) return GuardFireBlock.NotAuthorized;
            if (!radarLockAvailable) return GuardFireBlock.NoRadarLock;
            return GuardFireBlock.None;
        }

        /// <summary>
        /// How many missiles a salvo may fire at a target: up to the salvo size, without exceeding Missiles/Tgt.
        /// </summary>
        public static int SalvoSlots(int missilesAway, int salvoSize, int maxPerTarget)
            => Math.Max(0, Math.Min(Math.Max(1, salvoSize), Math.Max(1, maxPerTarget) - Math.Max(missilesAway, 0)));

        /// <summary>
        /// How many missiles one multi-missile launcher (VLS, pod) fires in a parallel salvo: an even share of what the target still needs over the launchers of the type
        /// that are left (rounded up), within the tubes it has ready. 0 if it has none ready (or nothing is needed).
        /// </summary>
        public static int LauncherSalvoShare(int remaining, int launchersLeft, int readyTubes)
        {
            if (remaining <= 0 || readyTubes <= 0) return 0;
            int launchers = Math.Max(1, launchersLeft);
            return Math.Min((remaining + launchers - 1) / launchers, readyTubes);
        }

        /// <summary>
        /// Whether a fired missile counts towards its target's missiles away (MissileFire.CalculateMissilesAway).
        /// A missile stops counting when its target is gone, when it has exploded, or when it has missed (bombs keep counting until they explode).
        /// </summary>
        public static bool CountsAsMissileAway(bool targetAlive, bool hasFired, bool launched, bool hasExploded, bool hasMissed, bool isBomb)
            => targetAlive && (hasFired || launched) && !hasExploded && (!hasMissed || isBomb);

        /// <summary>
        /// Whether a weapon type is ready to fire (MissileFire.CheckEngagementEnvelope): the instance itself is unfired (on the weapon channel), or another unfired instance of the same type is on board.
        /// (Upstream accepted a fired instance when an unfired missile of a *different* type was on board, so an empty type kept being selected and nothing fired.)
        /// </summary>
        public static bool MissileTypeReady(bool onWeaponChannel, bool launched, bool sameTypeUnfiredOnBoard) => (onWeaponChannel && !launched) || sameTypeUnfiredOnBoard;

        /// <summary>Whether the queued launches are stale (nothing has been queued for the timeout) and should be cleared.</summary>
        public static bool QueuedLaunchesStale(bool requireClearing, float now, float lastAddition, float timeout = QueuedLaunchTimeout)
            => requireClearing && now - lastAddition > timeout;

        /// <summary>
        /// Whether target selection should stop skipping the targets that have their allowance of missiles ("everything is saturated, track the best target").
        /// With vessel targets, only vessels count (so aircraft keep tracking the best vessel while missiles are around).
        /// With only missiles left (e.g. after the launch platforms died), keep skipping the saturated missiles while some missile still has room.
        /// </summary>
        public static bool ResetSaturatedExclusions(int vesselTargets, int saturatedVesselTargets, int missileTargets, int saturatedMissileTargets)
        {
            if (vesselTargets > 0) return saturatedVesselTargets >= vesselTargets;
            return saturatedMissileTargets >= missileTargets;
        }

        /// <summary>
        /// The first target at or after start (wrapping round) that isn't saturated, or -1 if all are (point defence target rotation).
        /// </summary>
        public static int NextUnsaturatedIndex(int count, int start, Func<int, bool> saturated)
        {
            if (count <= 0 || saturated == null) return -1;
            if (start < 0 || start >= count) start = 0;
            for (int i = 0; i < count; ++i)
            {
                int index = start + i;
                if (index >= count) index -= count;
                if (!saturated(index)) return index;
            }
            return -1;
        }
        #endregion

        #region The launch flag (guardFiringMissile)
        /// <summary>
        /// How long a launch routine may go without making progress before the watchdog releases the launch flag.
        /// The longest single wait in the routines is a turret slew or targeting pod slew of up to 0.75 x the firing interval.
        /// </summary>
        public static float StallTimeout(float targetScanInterval)
            => float.IsNaN(targetScanInterval) ? 30f : Math.Max(30f, 3f * targetScanInterval);

        /// <summary>
        /// What to do with the launch flag this frame.
        /// </summary>
        /// <param name="flag">guardFiringMissile.</param>
        /// <param name="routineActive">A launch routine (missile, salvo or bomb) owns the flag.</param>
        /// <param name="now">The current time.</param>
        /// <param name="lastProgress">When the owning routine last resumed.</param>
        /// <param name="stallTimeout">See StallTimeout.</param>
        /// <param name="holdUntil">When the last modular missile guidance hold lapses.</param>
        public static GuardFiringFlagAction FiringFlagAction(bool flag, bool routineActive, float now, float lastProgress, float stallTimeout, float holdUntil)
        {
            if (!flag) return GuardFiringFlagAction.Keep;
            if (routineActive) return now - lastProgress > stallTimeout ? GuardFiringFlagAction.ReleaseStalledRoutine : GuardFiringFlagAction.Keep;
            return now > holdUntil ? GuardFiringFlagAction.ReleaseExpiredHold : GuardFiringFlagAction.Keep;
        }
        #endregion

        #region Point defence
        /// <summary>
        /// The cheap checks for an unfired interceptor against the current point defence target, before the seeker, lock and boresight checks.
        /// </summary>
        public static InterceptorPrecheck CheckInterceptor(bool recentlyFailed, float targetDistance, float engageRangeMin)
        {
            if (recentlyFailed) return InterceptorPrecheck.RecentlyFailed;
            if (targetDistance >= 0f && targetDistance < engageRangeMin) return InterceptorPrecheck.TooClose;
            return InterceptorPrecheck.Usable;
        }

        #endregion

        #region Diagnostics
        /// <summary>A short explanation of the reason, for the DEBUG_GUARD_FIRE log.</summary>
        public static string Describe(GuardFireBlock reason) => reason switch
        {
            GuardFireBlock.None => "firing",
            GuardFireBlock.NoTarget => "no target",
            GuardFireBlock.NoWeapon => "no weapon for the target",
            GuardFireBlock.AuthorityHold => "fire authority hold",
            GuardFireBlock.RoutineBusy => "launch routine busy",
            GuardFireBlock.QuotaFull => "missiles/target quota full",
            GuardFireBlock.MaxTargetsEngaged => "max missile targets engaged",
            GuardFireBlock.NotAuthorized => "no weapon in range/launch checks failed",
            GuardFireBlock.NoRadarLock => "no radar lock",
            GuardFireBlock.NoThreats => "no incoming missiles",
            GuardFireBlock.NoInterceptor => "no interceptor can engage",
            _ => reason.ToString()
        };
        #endregion
    }

    /// <summary>
    /// [Overdrive] Layered defence for guard mode point defence (BDArmorySettings.GUARD_LAYERED_DEFENCE): serve the incoming missile closest to impact first,
    /// and try the interceptors from the longest to the shortest range, so threats are engaged as far out as possible and the shorter ranges keep time for follow-up shots.
    /// A type that's out of envelope, empty, without a lock or that failed to launch falls through to the next type (in the same scan, or via InterceptorAttemptMemory).
    /// Shoot-look-shoot comes from the missiles-away count being recomputed every frame: a missile that misses, explodes, loses its seeker (its target is cleared)
    /// or whose target dies stops counting at once, and point defence fires again on its next scan.
    /// </summary>
    public static class LayeredDefence
    {
        /// <summary>Time to impact used for missiles that aren't closing (they go after every closing missile, nearest first).</summary>
        public const float NoClosingTime = 1e6f;

        /// <summary>How long an incoming missile that no interceptor could engage is skipped, so the next most urgent one gets a turn.</summary>
        public const float TargetSkipTime = 1f;

        /// <summary>Seconds until the missile reaches us at its current closing speed (m/s). Missiles that aren't closing get NoClosingTime plus their distance.</summary>
        public static float TimeToImpact(float distance, float closingSpeed)
        {
            if (float.IsNaN(distance) || float.IsNaN(closingSpeed)) return 2f * NoClosingTime;
            return closingSpeed > 1f ? Math.Max(0f, distance) / closingSpeed : NoClosingTime + Math.Max(0f, distance);
        }

        /// <summary>Interceptors allowed in flight at an incoming missile: Missiles/Tgt, or at least 2 (shoot-shoot) within the shoot-shoot time (0: off).</summary>
        public static int PerTargetQuota(int missilesPerTarget, float timeToImpact, float shootShootTime)
        {
            int quota = Math.Max(1, missilesPerTarget);
            return shootShootTime > 0f && timeToImpact <= shootShootTime ? Math.Max(2, quota) : quota;
        }

        /// <summary>The index of the missile with the lowest time to impact that isn't excluded (saturated or skipped); -1 if none. Ties go to the lower index.</summary>
        public static int MostUrgentIndex(int count, Func<int, float> timeToImpact, Func<int, bool> excluded)
        {
            if (count <= 0 || timeToImpact == null) return -1;
            int best = -1;
            float bestTime = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                if (excluded != null && excluded(i)) continue;
                float time = timeToImpact(i);
                if (float.IsNaN(time)) continue;
                if (best < 0 || time < bestTime) { best = i; bestTime = time; }
            }
            return best;
        }

        /// <summary>Whether an incoming missile is within the interceptors' reach: between the smallest minimum range and the longest maximum range.</summary>
        public static bool InReach(float distance, float minRange, float maxRange) => maxRange > 0f && distance >= minRange && distance <= maxRange;

        /// <summary>
        /// The k-th interceptor to try, with the interceptors sorted from the shortest to the longest engagement range:
        /// longest first with layered defence, shortest first (stock) without.
        /// </summary>
        public static int InterceptorIndex(int k, int count, bool layered) => layered ? count - 1 - k : k;
    }

    /// <summary>
    /// Ownership of MissileFire.guardFiringMissile (the launch flag), which blocks target scans, point defence and new launch routines while it's set.
    /// A launch routine takes it with TryBegin and gives it back with End (in a finally block); the watchdog (Check, every physics frame) releases it if the owning routine
    /// stalls (a stopped coroutine never runs its finally) and supersedes that routine. Guided modular missiles hold it with a hold that lapses unless refreshed.
    /// The flag itself stays a public field on MissileFire (other code reads it), so it's passed by reference.
    /// </summary>
    public sealed class GuardFiringFlag
    {
        /// <summary>A launch routine owns the flag.</summary>
        public bool RoutineActive { get; private set; }
        /// <summary>The generation of the owning routine (bumped when the watchdog releases a stalled routine, so that routine no longer touches the flag).</summary>
        public int RoutineId { get; private set; }
        /// <summary>When the owning routine last resumed.</summary>
        public float LastProgress { get; private set; }
        /// <summary>When the last modular missile guidance hold lapses (-1: none).</summary>
        public float HoldUntil { get; private set; } = -1f;

        /// <summary>Take the flag for a launch routine. False if it's held (another routine, a guided modular missile or other code).</summary>
        public bool TryBegin(ref bool flag, float now, out int routineId)
        {
            if (flag) { routineId = 0; return false; }
            flag = true;
            RoutineActive = true;
            routineId = ++RoutineId;
            LastProgress = now;
            return true;
        }

        /// <summary>The routine was released by the watchdog (it should stop).</summary>
        public bool Superseded(int routineId) => routineId != RoutineId;

        public void MarkProgress(int routineId, float now)
        {
            if (routineId == RoutineId) LastProgress = now;
        }

        /// <summary>Give the flag back at the end of a launch routine. False (and nothing changes) if the watchdog already released it.</summary>
        public bool End(ref bool flag, int routineId)
        {
            if (routineId != RoutineId || !RoutineActive) return false;
            RoutineActive = false;
            flag = false;
            return true;
        }

        /// <summary>A guided modular missile holds the flag (refreshed every physics frame while it guides).</summary>
        public void Hold(ref bool flag, float now, float holdTime = GuardFireRules.ModularGuidanceHoldTime)
        {
            HoldUntil = now + holdTime;
            flag = true;
        }

        /// <summary>A modular missile releases its hold. A launch routine's ownership is kept.</summary>
        public void ReleaseHold(ref bool flag)
        {
            HoldUntil = -1f;
            if (!RoutineActive) flag = false;
        }

        /// <summary>The watchdog: release the flag if the owning routine has stalled, or if it was set outside a routine and the hold has lapsed.</summary>
        public GuardFiringFlagAction Check(ref bool flag, float now, float stallTimeout)
        {
            var action = GuardFireRules.FiringFlagAction(flag, RoutineActive, now, LastProgress, stallTimeout, HoldUntil);
            switch (action)
            {
                case GuardFiringFlagAction.ReleaseStalledRoutine:
                    ++RoutineId;
                    RoutineActive = false;
                    flag = false;
                    break;
                case GuardFiringFlagAction.ReleaseExpiredHold:
                    HoldUntil = -1f;
                    flag = false;
                    break;
            }
            return action;
        }
    }

    /// <summary>
    /// Point defence memory of interceptors that failed to launch at a target (the launch routine ran but nothing left the rail).
    /// Point defence tries the interceptors from the shortest to the longest range and used to pick the same one again every scan,
    /// so a single interceptor that passed the quick checks but couldn't actually launch (clearance, rail, turret, lock) blocked every other interceptor and missile type.
    /// Keys are opaque ints (Unity instance IDs in game).
    /// </summary>
    public sealed class InterceptorAttemptMemory
    {
        readonly Dictionary<long, float> blockedUntil = new();
        readonly List<long> expired = new();
        float nextPrune = float.NegativeInfinity;
        const float PruneInterval = 1f;

        static long Key(int missileKey, int targetKey) => ((long)missileKey << 32) | (uint)targetKey;

        /// <summary>The number of remembered failures (including expired ones not yet pruned).</summary>
        public int Count => blockedUntil.Count;

        /// <summary>Remember that the interceptor failed to launch at the target.</summary>
        public void RecordFailure(int missileKey, int targetKey, float now, float cooldown = GuardFireRules.InterceptorRetryCooldown)
        {
            if (!(cooldown > 0f)) return;
            blockedUntil[Key(missileKey, targetKey)] = now + cooldown;
        }

        /// <summary>Forget a failure (e.g. the interceptor has since launched).</summary>
        public void Forget(int missileKey, int targetKey) => blockedUntil.Remove(Key(missileKey, targetKey));

        /// <summary>Whether the interceptor recently failed to launch at the target.</summary>
        public bool IsBlocked(int missileKey, int targetKey, float now)
            => blockedUntil.Count > 0 && blockedUntil.TryGetValue(Key(missileKey, targetKey), out float until) && now < until;

        /// <summary>Drop the expired failures (at most once per second).</summary>
        public void PruneIfDue(float now)
        {
            if (now < nextPrune) return;
            nextPrune = now + PruneInterval;
            Prune(now);
        }

        /// <summary>Drop the expired failures.</summary>
        public void Prune(float now)
        {
            if (blockedUntil.Count == 0) return;
            expired.Clear();
            foreach (var kvp in blockedUntil)
                if (now >= kvp.Value) expired.Add(kvp.Key);
            for (int i = 0; i < expired.Count; ++i) blockedUntil.Remove(expired[i]);
            expired.Clear();
        }

        public void Clear()
        {
            blockedUntil.Clear();
            expired.Clear();
        }
    }

    /// <summary>
    /// Rate limiting for the DEBUG_GUARD_FIRE log: log a channel's reason when it changes, or again after the repeat interval.
    /// </summary>
    public sealed class GuardFireReasonLog
    {
        readonly GuardFireBlock[] lastReason = new GuardFireBlock[3];
        readonly float[] lastTime = [float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity];
        readonly bool[] logged = new bool[3];

        /// <summary>Whether to log the reason for the channel now (and remember it if so).</summary>
        public bool ShouldLog(GuardFireChannel channel, GuardFireBlock reason, float now, float repeatInterval = 5f)
        {
            int c = (int)channel;
            if (c < 0 || c >= lastReason.Length) return false;
            if (logged[c] && lastReason[c] == reason && now - lastTime[c] < repeatInterval) return false;
            logged[c] = true;
            lastReason[c] = reason;
            lastTime[c] = now;
            return true;
        }

        /// <summary>Forget the channel's last reason, so the next one is logged.</summary>
        public void Reset(GuardFireChannel channel)
        {
            int c = (int)channel;
            if (c < 0 || c >= lastReason.Length) return;
            logged[c] = false;
        }
    }
}
