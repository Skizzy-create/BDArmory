namespace BDArmory.Utils
{
    /// <summary>
    /// [Overdrive perf] Spreads periodic work over frames: with an interval of N, Tick returns true once every N calls.
    /// Each caller starts from its own phase (derived from an id, e.g. a part's persistentId) so that many callers with the same interval don't all run on the same frame.
    /// An interval of 1 or less runs on every call (the unstaggered behaviour). The phase is re-derived whenever the interval changes.
    /// </summary>
    public struct ScanStagger
    {
        int counter;
        int interval; // The interval the counter was set up for (0: not set up yet).

        /// <summary>
        /// The starting counter value for a caller, spreading callers evenly over the interval.
        /// </summary>
        public static int InitialCounter(uint id, int interval) => interval <= 1 ? 0 : (int)(id % (uint)interval);

        /// <summary>
        /// Advance and return whether the work is due on this call.
        /// </summary>
        /// <param name="id">The caller's id, for its phase.</param>
        /// <param name="interval">Run once every 'interval' calls.</param>
        public bool Tick(uint id, int interval)
        {
            if (interval <= 1)
            {
                this.interval = interval;
                counter = 0;
                return true;
            }
            if (interval != this.interval)
            {
                this.interval = interval;
                counter = InitialCounter(id, interval);
            }
            bool due = counter == 0;
            counter = (counter + 1) % interval;
            return due;
        }
    }

    /// <summary>
    /// [Overdrive perf] The detection gates of the guard mode view scan (RadarUtils.GuardScanInDirection) for incoming missiles, as pure functions.
    /// The scan only evaluates a missile (threat check, target report) if it's detected by the missile warning system (MWS), by sight, or by the RWR.
    /// Evaluating these cheap gates before the terrain line-of-sight raycast gives the same results while only casting rays for missiles that could be detected.
    /// </summary>
    public static class GuardScanGates
    {
        /// <summary>
        /// Sight detection: within the (state-dependent) sight distance and inside the scan cone.
        /// The angle is only needed when within the sight distance.
        /// </summary>
        public static bool SightDetects(bool withinSightDistance, float angle, float halfFov) => withinSightDistance && angle < halfFov;

        /// <summary>
        /// Whether the RWR rejects the missile when it wasn't seen or detected by the MWS (the 'continue' condition of the scan):
        /// only radar-guided missiles can be detected by the RWR; active radar (or LOAL searching) missiles must be within twice their active radar range and have a missile lock warning on the RWR;
        /// semi-active missiles must be guided by a radar (have a VesselRadarData).
        /// </summary>
        public static bool RwrRejects(bool radarGuided, bool activeOrLoalSearching, bool beyondTwiceActiveRange, bool rwrHasMissileLock, bool isActiveRadar, bool hasRadarData)
            => !radarGuided
            || (activeOrLoalSearching && (beyondTwiceActiveRange || !rwrHasMissileLock))
            || (!isActiveRadar && !hasRadarData);

        /// <summary>
        /// Whether the missile passes at least one detection gate (and so gets evaluated as a possible threat, once the terrain line of sight is clear).
        /// </summary>
        /// <param name="mwsDetects">The MWS check result (false when there's no omni-directional MWS).</param>
        /// <param name="sightDetects">The sight check result (SightDetects).</param>
        /// <param name="rwrEnabled">Whether there's an enabled RWR.</param>
        /// <param name="rwrRejects">The RWR rejection (RwrRejects); only used when the RWR is enabled.</param>
        public static bool MissileCouldBeDetected(bool mwsDetects, bool sightDetects, bool rwrEnabled, bool rwrRejects)
            => mwsDetects || sightDetects || (rwrEnabled && !rwrRejects);
    }
}
