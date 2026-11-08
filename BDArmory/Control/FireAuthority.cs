// BDArmory Overdrive: integration hooks (F1, F3). Pure logic, no Unity dependencies, so it can be unit tested outside KSP.
namespace BDArmory.Control
{
    /// <summary>
    /// Fire authority of a weapon manager (Overdrive hook F3).
    /// Free is the stock behaviour and the default. Tight fires only at authorised targets (external assignments, targets passing
    /// the ExternalTargetFilter, and missiles as self-defence). Hold keeps scanning, targeting and locking, but never fires.
    /// </summary>
    public enum FireAuthority
    {
        Free = 0,
        Tight = 1,
        Hold = 2,
    }

    /// <summary>
    /// Why an external target assignment (MissileFire.SetOverrideTarget(target, durationS, sticky)) ended (Overdrive hook F1).
    /// </summary>
    public enum OverrideLostReason
    {
        None = 0,
        /// <summary>The assignment's duration ran out.</summary>
        Expired,
        /// <summary>ClearExternalOverride was called (or SetOverrideTarget with a null target).</summary>
        Cleared,
        /// <summary>A new assignment (external or via the stock one-argument SetOverrideTarget) replaced it.</summary>
        Replaced,
        /// <summary>The target vessel died or its TargetInfo was destroyed.</summary>
        TargetDestroyed,
        /// <summary>Non-sticky assignment: no weapon could engage the target at a target scan.</summary>
        NotEngageable,
        /// <summary>The ExternalTargetFilter rejected the assigned target.</summary>
        Filtered,
        /// <summary>The target is on this weapon manager's team, an allied team or a neutral team (for example after a team change), so it would never be engaged.</summary>
        NotHostile,
    }

    /// <summary>
    /// What a target scan does with the current override target.
    /// </summary>
    public enum OverrideScanOutcome
    {
        /// <summary>A weapon can engage it: engage it.</summary>
        Engage,
        /// <summary>Sticky external assignment that no weapon can engage right now: keep it as the target (the AI keeps closing) but select no weapon.</summary>
        HoldNotEngageable,
        /// <summary>Drop the override and run the normal target selection.</summary>
        Drop,
    }

    /// <summary>
    /// The decisions behind fire authority, target filtering and external override targets.
    /// </summary>
    public static class FireAuthorityRules
    {
        /// <summary>
        /// Whether a target should be skipped by target selection (the BDATargetManager queries and the secondary target lists).
        /// Missiles are never filtered: point defence and self-defence always work.
        /// </summary>
        /// <param name="mode">The weapon manager's fire authority.</param>
        /// <param name="isMissile">The target is a missile.</param>
        /// <param name="isAssigned">The target is the external override target or in the external secondary target list.</param>
        /// <param name="hasFilter">An ExternalTargetFilter is set.</param>
        /// <param name="passesFilter">The filter accepts the target (ignored when hasFilter is false).</param>
        public static bool ExcludedFromSelection(FireAuthority mode, bool isMissile, bool isAssigned, bool hasFilter, bool passesFilter)
        {
            if (isMissile) return false;
            if (hasFilter) return !passesFilter;
            return mode == FireAuthority.Tight && !isAssigned;
        }

        /// <summary>
        /// Whether the weapon manager may fire at a target.
        /// Free: always (stock). Hold: never. Tight: missiles and projectiles (self-defence), and vessels that are assigned or pass the filter.
        /// A filter that rejects a vessel wins over an assignment.
        /// </summary>
        public static bool PermitsFire(FireAuthority mode, bool isMissileOrProjectile, bool isAssigned, bool hasFilter, bool passesFilter)
        {
            switch (mode)
            {
                case FireAuthority.Free: return true;
                case FireAuthority.Hold: return false;
                default:
                    if (isMissileOrProjectile) return true;
                    if (hasFilter && !passesFilter) return false;
                    return isAssigned || hasFilter;
            }
        }

        /// <summary>
        /// What a target scan does with the override target.
        /// </summary>
        /// <param name="external">The override was set by an external consumer (SetOverrideTarget with a duration).</param>
        /// <param name="sticky">The external assignment is sticky.</param>
        /// <param name="filteredOut">The ExternalTargetFilter rejects the target.</param>
        /// <param name="engageable">A weapon can engage the target (SmartPickWeapon_EngagementEnvelope succeeded).</param>
        /// <param name="reason">Why the override is dropped, when the outcome is Drop and the override was external. None otherwise.</param>
        public static OverrideScanOutcome EvaluateOverrideScan(bool external, bool sticky, bool filteredOut, bool engageable, out OverrideLostReason reason)
        {
            reason = OverrideLostReason.None;
            if (external && filteredOut)
            {
                reason = OverrideLostReason.Filtered;
                return OverrideScanOutcome.Drop;
            }
            if (engageable) return OverrideScanOutcome.Engage;
            if (external && sticky) return OverrideScanOutcome.HoldNotEngageable;
            if (external) reason = OverrideLostReason.NotEngageable;
            return OverrideScanOutcome.Drop;
        }

        /// <summary>
        /// The override timer for an external assignment: the duration in seconds, or infinity (until cleared) for a duration of zero or less.
        /// </summary>
        public static float ExternalOverrideTimer(float durationS) => durationS > 0f && !float.IsNaN(durationS) ? durationS : float.PositiveInfinity;
    }
}
