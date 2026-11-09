using System.Numerics;

namespace Ball2.Core.Combat
{
    /// <summary>
    /// One enemy the player could lock onto.
    /// </summary>
    public readonly struct LockOnCandidate
    {
        public readonly int Id;                 // stable per-enemy id
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;

        public LockOnCandidate(int id, Vector3 position, Vector3 velocity)
        {
            Id = id;
            Position = position;
            Velocity = velocity;
        }
    }

    /// <summary>
    /// The lock state carried frame-to-frame by the caller. TargetId=int.MinValue means no lock.
    /// TrackingPosition is the soft-tracked aim point (lags the real enemy position).
    /// </summary>
    public readonly struct LockState
    {
        public readonly int TargetId;                       // int.MinValue = none
        public readonly Vector3 TrackingPosition;

        public LockState(int targetId, Vector3 trackingPosition)
        {
            TargetId = targetId;
            TrackingPosition = trackingPosition;
        }

        public bool HasLock => TargetId != int.MinValue;

        public static LockState None => new LockState(int.MinValue, Vector3.Zero);
    }

    /// <summary>
    /// Everything the resolver needs for one tick.
    /// </summary>
    public readonly struct LockOnInput
    {
        public readonly Vector3 PlayerPosition;
        public readonly Vector3 AimDirection;               // assumed unit-length
        public readonly LockOnCandidate[] Candidates;       // may be null/empty
        public readonly int CandidateCount;                 // valid count within Candidates buffer
        public readonly LockState CurrentLock;
        public readonly bool BoostChargeStarted;

        public LockOnInput(
            Vector3 playerPosition,
            Vector3 aimDirection,
            LockOnCandidate[] candidates,
            LockState currentLock,
            bool boostChargeStarted)
            : this(playerPosition, aimDirection, candidates, candidates?.Length ?? 0, currentLock, boostChargeStarted)
        { }

        public LockOnInput(
            Vector3 playerPosition,
            Vector3 aimDirection,
            LockOnCandidate[] candidates,
            int candidateCount,
            LockState currentLock,
            bool boostChargeStarted)
        {
            PlayerPosition = playerPosition;
            AimDirection = aimDirection;
            Candidates = candidates;
            CandidateCount = candidateCount;
            CurrentLock = currentLock;
            BoostChargeStarted = boostChargeStarted;
        }
    }

    /// <summary>
    /// Result of a resolve tick.
    /// </summary>
    public readonly struct LockOnResult
    {
        public readonly LockState NewLock;
        public readonly int IconTargetId;                   // -1 = no icon
        public readonly bool IconShouldShow;

        public LockOnResult(LockState newLock, int iconTargetId, bool iconShouldShow)
        {
            NewLock = newLock;
            IconTargetId = iconTargetId;
            IconShouldShow = iconShouldShow;
        }
    }

    /// <summary>
    /// All tuning for lock-on. Q12 — Tom tunes at M2.
    /// </summary>
    public sealed class LockOnConfig
    {
        /// <summary>Max distance from player at which a lock can be acquired.</summary>
        public float Range = 30f;

        /// <summary>Max angle (degrees) between the aim ray and the direction to an enemy
        /// for the enemy to qualify as a lock candidate.</summary>
        public float AcquisitionAngleDeg = 45f;

        /// <summary>Soft-tracking lerp factor in [0,1]. 0 = no tracking (frozen aim point),
        /// 1 = snaps perfectly to enemy. The boost homing stays "soft" below 1.</summary>
        public float TrackingStrength = 0.35f;

        /// <summary>Max angle (degrees) the aim may drift from a locked target before the
        /// lock breaks (the "dodge" tolerance — target slips the reticle).</summary>
        public float DodgeToleranceDeg = 90f;

        /// <summary>Distance beyond which the lock breaks. Defaults to Range so a target
        /// that leaves acquisition range also breaks the lock; raise for a wider hold band.</summary>
        public float BreakRange = 30f;

        /// <summary>Epsilon for floating-point comparisons (distances, cosines).</summary>
        public float Epsilon = 1e-5f;
    }

    /// <summary>
    /// Pure, deterministic lock-on resolver. No MonoBehaviour/Node types, no random
    /// numbers, no time/frame dependence. Given aim, candidates, current lock, and the
    /// boost-charge flag, returns the new lock state and whether the hover icon should show.
    ///
    /// Behaviour (spec §3.3 / Q13):
    ///  - No qualifying candidate in short range near the reticle → no lock; boost is normal.
    ///  - A valid enemy near/on the reticle within range → lock acquired; icon shows for it.
    ///  - Soft tracking: the tracking position lags the enemy by TrackingStrength (no snap).
    ///  - Lock breaks when the enemy leaves BreakRange or the aim drifts past DodgeToleranceDeg.
    ///  - Commit-on-charge: once BoostChargeStarted is true, the locked target cannot switch
    ///    to another candidate (break conditions still apply — a dodged/out-of-range enemy
    ///    still loses the lock; you just can't re-aim onto a different enemy mid-charge).
    /// </summary>
    public static class LockOnResolver
    {
        private const float AimEpsilonDeg = 0.0001f;

        public static LockOnResult Resolve(in LockOnInput input, LockOnConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (CandidatesIsEmpty(input.Candidates))
            {
                return new LockOnResult(LockState.None, -1, false);
            }

            Vector3 aim = input.AimDirection;
            if (!IsUnitLength(aim, cfg.Epsilon))
            {
                return new LockOnResult(LockState.None, -1, false);
            }

            // --- If already locked, try to keep the lock (with soft tracking + break). ---
            if (input.CurrentLock.HasLock)
            {
                LockOnCandidate lockedEnemy;
                if (TryFindCandidate(input.Candidates, input.CandidateCount, input.CurrentLock.TargetId, out lockedEnemy))
                {
                    float dist = Vector3.Distance(input.PlayerPosition, lockedEnemy.Position);
                    Vector3 toEnemy = lockedEnemy.Position - input.PlayerPosition;
                    float aimAngleDeg = AngleDeg(aim, toEnemy);

                    // Break conditions: out of break range OR dodged outside tolerance.
                    if (dist > cfg.BreakRange + cfg.Epsilon ||
                        aimAngleDeg > cfg.DodgeToleranceDeg + AimEpsilonDeg)
                    {
                        // Lock broken. While charging we do NOT re-acquire a different target
                        // (commit-on-charge); without charge we may acquire a new best candidate.
                        if (input.BoostChargeStarted)
                            return new LockOnResult(LockState.None, -1, false);
                        return AcquireBest(in input, cfg);
                    }

                    // Lock held: advance the soft tracking position toward the enemy.
                    Vector3 newTracking = Vector3.Lerp(
                        input.CurrentLock.TrackingPosition,
                        lockedEnemy.Position,
                        cfg.TrackingStrength);
                    LockState held = new LockState(lockedEnemy.Id, newTracking);
                    return new LockOnResult(held, lockedEnemy.Id, true);
                }

                // Locked enemy is gone from the candidate list (destroyed / left).
                if (input.BoostChargeStarted)
                    return new LockOnResult(LockState.None, -1, false);
                return AcquireBest(in input, cfg);
            }

            // --- Not locked: acquire if a candidate is near the reticle in range. ---
            return AcquireBest(in input, cfg);
        }

        private static LockOnResult AcquireBest(in LockOnInput input, LockOnConfig cfg)
        {
            Vector3 aim = input.AimDirection;
            float bestScore = float.MaxValue;   // score = aim angle (deg); ties broken by distance
            float bestDist = float.MaxValue;
            LockOnCandidate best = default;
            bool found = false;

            for (int i = 0; i < input.CandidateCount; i++)
            {
                LockOnCandidate c = input.Candidates[i];
                float dist = Vector3.Distance(input.PlayerPosition, c.Position);
                if (dist > cfg.Range + cfg.Epsilon) continue;        // out of acquisition range

                Vector3 toEnemy = c.Position - input.PlayerPosition;
                if (toEnemy.LengthSquared() <= cfg.Epsilon * cfg.Epsilon) continue; // on top of player

                float angleDeg = AngleDeg(aim, toEnemy);
                if (angleDeg > cfg.AcquisitionAngleDeg + AimEpsilonDeg) continue; // off-reticle

                // Prefer smallest aim angle; tie-break with nearer distance.
                bool better = false;
                if (!found) better = true;
                else if (angleDeg < bestScore - AimEpsilonDeg) better = true;
                else if (Math.Abs(angleDeg - bestScore) <= AimEpsilonDeg && dist < bestDist) better = true;

                if (better)
                {
                    found = true;
                    bestScore = angleDeg;
                    bestDist = dist;
                    best = c;
                }
            }

            if (!found)
                return new LockOnResult(LockState.None, -1, false);

            // New acquisition: seed tracking position at the enemy's current position.
            LockState acquired = new LockState(best.Id, best.Position);
            return new LockOnResult(acquired, best.Id, true);
        }

        private static bool CandidatesIsEmpty(LockOnCandidate[] candidates)
            => candidates == null || candidates.Length == 0;

        private static bool TryFindCandidate(LockOnCandidate[] candidates, int count, int id, out LockOnCandidate found)
        {
            for (int i = 0; i < count; i++)
            {
                if (candidates[i].Id == id)
                {
                    found = candidates[i];
                    return true;
                }
            }
            found = default;
            return false;
        }

        private static bool IsUnitLength(Vector3 v, float eps)
        {
            float mag = v.Length();
            return mag > eps && Math.Abs(mag - 1f) <= 0.01f;
        }

        /// <summary>Angle in degrees between two vectors; 90 if either is degenerate.</summary>
        private static float AngleDeg(Vector3 a, Vector3 b)
        {
            float denom = MathF.Sqrt(a.LengthSquared() * b.LengthSquared());
            if (denom <= 0f) return 90f;
            float cos = Math.Clamp(Vector3.Dot(a, b) / denom, -1f, 1f);
            return MathF.Acos(cos) * (180f / MathF.PI);
        }
    }
}
