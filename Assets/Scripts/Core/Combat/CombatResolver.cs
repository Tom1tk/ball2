using UnityEngine;

namespace Ball2.Core.Combat
{
    /// <summary>
    /// Inputs describing a single contact, in Ball A's frame relative to Ball B.
    /// A wall is represented by <see cref="MassB"/> = <see cref="float.PositiveInfinity"/>.
    /// </summary>
    /// <remarks>
    /// <b>Conventions</b> (binding on all callers):
    /// <list type="bullet">
    /// <item><c>RelativeVelocity</c> = vA - vB (A's velocity in B's frame).</item>
    /// <item><c>Normal</c> is the unit contact normal pointing <b>from B toward A</b>.
    /// With this convention a closing contact has <c>dot(RelativeVelocity, Normal) &lt; 0</c>,
    /// i.e. A is moving toward B (and B toward A).</item>
    /// </list>
    /// The resolver is a pure function of these inputs plus a <see cref="CombatConfig"/>:
    /// no UnityEngine.Random, no time/frame dependence, no MonoBehaviour.
    /// </remarks>
    public readonly struct ContactInput
    {
        public readonly Vector3 RelativeVelocity; // A relative to B (vA - vB)
        public readonly Vector3 Normal;           // unit contact normal, points B -> A
        public readonly float MassA;
        public readonly float MassB;              // float.PositiveInfinity for a wall

        public ContactInput(Vector3 relativeVelocity, Vector3 normal, float massA, float massB)
        {
            RelativeVelocity = relativeVelocity;
            Normal = normal;
            MassA = massA;
            MassB = massB;
        }
    }

    /// <summary>
    /// Result of resolving a contact. Damage is non-negative; impulses are momentum
    /// changes applied to A and B respectively (equal and opposite for ball-ball,
    /// zero on the wall side for ball-wall). <see cref="PerfectHit"/> flags a dead-on,
    /// high-speed strike inside the configured angle/speed window.
    /// </summary>
    public readonly struct ContactOutcome
    {
        public readonly float DamageA;
        public readonly float DamageB;
        public readonly Vector3 ImpulseA;
        public readonly Vector3 ImpulseB;
        public readonly bool PerfectHit;

        public ContactOutcome(float damageA, float damageB, Vector3 impulseA, Vector3 impulseB, bool perfectHit)
        {
            DamageA = damageA;
            DamageB = damageB;
            ImpulseA = impulseA;
            ImpulseB = impulseB;
            PerfectHit = perfectHit;
        }
    }

    /// <summary>
    /// All combat tuning lives here (spec Q12 — do not hard-code magic numbers in the resolver).
    /// Fields are public for inspector/serializer friendliness; defaults are sensible
    /// placeholders for Tom to feel out at M2.
    /// </summary>
    [System.Serializable]
    public sealed class CombatConfig
    {
        // Relative normal closing speed below this deals no damage and no impulse.
        public float DamageThresholdSpeed = 5.0f;
        // Damage per unit of excess closing speed (above the threshold).
        public float DamagePerSpeed = 1.0f;
        // Impulse magnitude per unit of excess closing speed.
        public float KnockbackPerSpeed = 1.0f;
        // A perfect hit requires at least this much closing speed.
        public float PerfectHitMinSpeed = 15.0f;
        // A perfect hit requires the approach angle to be within this many degrees of dead-on.
        public float PerfectHitMaxAngleDeg = 10.0f;
    }

    /// <summary>
    /// Pure, deterministic momentum-differential combat resolver (spec §3.2).
    /// Resolves a single ball-ball or ball-wall contact into damage + knockback.
    /// </summary>
    /// <remarks>
    /// <b>Model.</b> The relative normal closing speed <c>vn = -dot(RelativeVelocity, Normal)</c>
    /// gates everything: below <c>DamageThresholdSpeed</c> the contact is a glancing tap
    /// (zero damage, zero impulse). Above it, the excess speed <c>vn - threshold</c> drives
    /// both damage and impulse.
    /// <para>
    /// For ball-ball, each ball's share of the closing speed follows the inverse-mass
    /// decomposition: the lighter ball carries more of the closing speed and is therefore
    /// the faster aggressor. Damage dealt <i>to</i> a ball is proportional to the
    /// <i>opponent's</i> incoming normal speed share — so the faster (lighter) ball deals
    /// more damage and takes less, matching pillar 1 ("momentum is the weapon") and §3.2's
    /// "winner keeps more of its speed". Equal mass + equal-and-opposite speed is the
    /// symmetric midpoint: equal damage, equal-and-opposite impulses.
    /// </para>
    /// A wall is <c>MassB = +inf</c>. The wall takes no damage and receives no impulse;
    /// the ball takes damage scaled by its own closing speed (= vn, since the wall does not
    /// move) and its impulse reflects along the normal.
    /// </para>
    /// </remarks>
    public static class CombatResolver
    {
        /// <summary>
        /// Resolve <paramref name="c"/> under <paramref name="cfg"/> into damage + knockback.
        /// Pure: identical inputs yield byte-identical outputs across repeated calls.
        /// </summary>
        public static ContactOutcome Resolve(in ContactInput c, CombatConfig cfg)
        {
            // Normalised normal; degenerate (zero-length) normals are treated as no contact.
            Vector3 normal = c.Normal.normalized;
            if (normal.sqrMagnitude < 1e-12f)
                return new ContactOutcome(0f, 0f, Vector3.zero, Vector3.zero, false);

            // Closing speed along the normal. With Normal pointing B -> A and
            // RelativeVelocity = vA - vB, a closing contact has dot < 0; negate for a
            // non-negative closing speed. Separating contacts (vn <= 0) do nothing.
            float vn = -Vector3.Dot(c.RelativeVelocity, normal);

            bool isWall = float.IsInfinity(c.MassA) || float.IsInfinity(c.MassB);
            bool perfectHit = ComputePerfectHit(c.RelativeVelocity, normal, vn, cfg);

            // Below threshold (or separating): glancing tap — zero damage, zero impulse.
            // PerfectHit is still reported independently so callers can play FX, but a
            // sub-threshold contact never deals damage or knockback.
            if (vn <= cfg.DamageThresholdSpeed)
            {
                return new ContactOutcome(0f, 0f, Vector3.zero, Vector3.zero, perfectHit);
            }

            float excess = vn - cfg.DamageThresholdSpeed;

            // --- Wall / infinite-mass path -----------------------------------
            // The wall takes no damage and no impulse. The ball takes damage scaled by
            // its own closing speed (= vn, since the wall is stationary) and its impulse
            // reflects along the normal. This branch also keeps the Ma+Mb / Mb ratios
            // (which would be inf/inf = NaN) out of the ball-ball math below.
            if (isWall)
            {
                // The finite-mass ball takes damage scaled by its own closing speed and
                // reflects; the infinite-mass wall takes no damage and no impulse. Route
                // to whichever side is finite. Normal points B -> A, so "away from the
                // wall" is +Normal when the wall is B (ball is A) and -Normal when the
                // wall is A (ball is B).
                float wallDamage = excess * cfg.DamagePerSpeed;
                float wallImpulseMag = excess * cfg.KnockbackPerSpeed;
                if (float.IsInfinity(c.MassB))
                {
                    Vector3 imp = normal * wallImpulseMag; // A reflects along +Normal
                    return new ContactOutcome(wallDamage, 0f, imp, Vector3.zero, perfectHit);
                }
                else // wall is A; ball is B
                {
                    Vector3 imp = -normal * wallImpulseMag; // B reflects along -Normal
                    return new ContactOutcome(0f, wallDamage, Vector3.zero, imp, perfectHit);
                }
            }

            // --- Ball-ball path ---------------------------------------------
            // Inverse-mass decomposition of the closing speed: the lighter ball carries
            // a larger share. invSum = 1/(Ma+Mb); shareA = Mb/(Ma+Mb) = invA_eff...
            // We compute shares directly from masses to stay NaN-free for finite masses.
            float sum = c.MassA + c.MassB;
            // Defensive: both masses finite here (wall handled above), but guard zero/neg.
            if (sum <= 0f)
            {
                return new ContactOutcome(0f, 0f, Vector3.zero, Vector3.zero, perfectHit);
            }

            float shareA = c.MassB / sum; // A's fraction of the closing speed
            float shareB = c.MassA / sum; // B's fraction of the closing speed
            // shareA + shareB == 1. A's own incoming normal speed = vn*shareA;
            // the speed striking A is B's incoming share = vn*shareB, and vice versa.

            // Damage to each ball is proportional to the opponent's incoming speed share
            // times the excess closing speed. Faster (lighter, larger own-share) ball
            // deals more (opponent's strike term uses the larger share) and takes less.
            float damageA = shareB * excess * cfg.DamagePerSpeed;
            float damageB = shareA * excess * cfg.DamagePerSpeed;

            // Impulse: equal and opposite (Newton's third law), along the normal.
            // Magnitude scales with excess closing speed; A is pushed along +Normal
            // (away from B), B along -Normal.
            float impulseMag = excess * cfg.KnockbackPerSpeed;
            Vector3 impulseA = normal * impulseMag;
            Vector3 impulseB = -normal * impulseMag;

            return new ContactOutcome(damageA, damageB, impulseA, impulseB, perfectHit);
        }

        /// <summary>
        /// A perfect hit is a near-dead-on, high-speed strike: closing speed at least
        /// <see cref="CombatConfig.PerfectHitMinSpeed"/> and the approach angle (between
        /// RelativeVelocity and the approach direction -Normal) within
        /// <see cref="CombatConfig.PerfectHitMaxAngleDeg"/> of dead-on.
        /// </summary>
        private static bool ComputePerfectHit(Vector3 relativeVelocity, Vector3 normal,
                                              float vn, CombatConfig cfg)
        {
            if (vn < cfg.PerfectHitMinSpeed)
            {
                return false;
            }

            float relMag = relativeVelocity.magnitude;
            if (relMag < 1e-6f)
            {
                return false; // no relative motion -> no hit at all
            }

            // Approach direction = -Normal (A moving toward B). A perfectly dead-on hit
            // has RelativeVelocity parallel to -Normal, i.e. angle 0.
            Vector3 approachDir = -normal;
            float cosA = Vector3.Dot(relativeVelocity, approachDir) / relMag;
            cosA = Mathf.Clamp(cosA, -1f, 1f);
            float angleDeg = Mathf.Acos(cosA) * Mathf.Rad2Deg;

            return angleDeg <= cfg.PerfectHitMaxAngleDeg;
        }
    }
}
