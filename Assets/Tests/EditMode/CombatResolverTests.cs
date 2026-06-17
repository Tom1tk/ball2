using NUnit.Framework;
using UnityEngine;
using Ball2.Core.Combat;

namespace Ball2.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="CombatResolver"/> — covers every acceptance criterion
    /// of ticket B2-007 (spec §3.2 momentum-differential combat model).
    ///
    /// Conventions exercised here (binding on the resolver):
    ///   - Normal points from B toward A (so A reflects along +Normal).
    ///   - RelativeVelocity = vA - vB; a closing contact has dot(RelativeVelocity, Normal) < 0.
    /// </summary>
    public class CombatResolverTests
    {
        // A config with round, obvious numbers so the maths is checkable by hand.
        private static CombatConfig MakeConfig(float threshold = 5f,
                                               float dmgPerSpeed = 1f,
                                               float kbPerSpeed = 2f,
                                               float phMinSpeed = 15f,
                                               float phMaxAngle = 10f)
        {
            return new CombatConfig
            {
                DamageThresholdSpeed = threshold,
                DamagePerSpeed = dmgPerSpeed,
                KnockbackPerSpeed = kbPerSpeed,
                PerfectHitMinSpeed = phMinSpeed,
                PerfectHitMaxAngleDeg = phMaxAngle,
            };
        }

        private const float Tol = 1e-5f;

        // ---------------------------------------------------------------------
        // 1. Glancing tap: relative normal speed below threshold -> zero everything.
        // ---------------------------------------------------------------------
        [Test]
        public void BelowThreshold_GlancingTap_DealsNoDamageAndNoImpulse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f);

            // A approaches B at 3 m/s along the normal (below the 5 threshold).
            Vector3 normal = Vector3.left; // B -> A points left (A is to the left of B)
            Vector3 relVel = -normal * 3f; // vA-vB points right (toward B) => closing
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.AreEqual(0f, o.DamageA, Tol, "DamageA must be zero below threshold");
            Assert.AreEqual(0f, o.DamageB, Tol, "DamageB must be zero below threshold");
            Assert.AreEqual(Vector3.zero, o.ImpulseA, "ImpulseA must be zero below threshold");
            Assert.AreEqual(Vector3.zero, o.ImpulseB, "ImpulseB must be zero below threshold");
        }

        [Test]
        public void SeparatingContact_DealsNoDamageAndNoImpulse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f);

            // Balls moving apart: vA-vB points along +Normal (away from B) => vn < 0.
            Vector3 normal = Vector3.left;
            Vector3 relVel = normal * 10f; // separating
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.AreEqual(0f, o.DamageA, Tol);
            Assert.AreEqual(0f, o.DamageB, Tol);
            Assert.AreEqual(Vector3.zero, o.ImpulseA);
            Assert.AreEqual(Vector3.zero, o.ImpulseB);
        }

        // ---------------------------------------------------------------------
        // 2. Head-on, equal mass, equal-and-opposite speed -> equal damage,
        //    symmetric (equal-and-opposite) impulses.
        // ---------------------------------------------------------------------
        [Test]
        public void HeadOn_EqualMass_EqualSpeed_BothTakeEqualDamage_SymmetricImpulse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f, kbPerSpeed: 2f);

            // Closing speed 20 m/s, equal masses => each carries 10 m/s.
            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 20f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            // excess = 20 - 5 = 15; equal share 0.5 each => damage = 0.5 * 15 * 1 = 7.5.
            const float expectedDamage = 0.5f * 15f * 1f;
            Assert.AreEqual(expectedDamage, o.DamageA, Tol, "DamageA must equal DamageB (symmetric)");
            Assert.AreEqual(expectedDamage, o.DamageB, Tol, "DamageB must equal DamageA (symmetric)");
            Assert.AreEqual(o.DamageA, o.DamageB, Tol, "Damage must be equal for equal mass / equal speed");

            // Impulses equal magnitude, opposite direction.
            // impulseMag = excess * kbPerSpeed = 15 * 2 = 30.
            const float expectedImpulseMag = 15f * 2f;
            Assert.AreEqual(normal * expectedImpulseMag, o.ImpulseA, "ImpulseA along +Normal");
            Assert.AreEqual(-normal * expectedImpulseMag, o.ImpulseB, "ImpulseB along -Normal");
            Assert.AreEqual(o.ImpulseA, -o.ImpulseB, "Impulses must be equal and opposite");
            Assert.AreEqual(o.ImpulseA.magnitude, o.ImpulseB.magnitude, Tol, "Impulse magnitudes equal");
        }

        // ---------------------------------------------------------------------
        // 3. Speed differential above threshold -> faster (lighter) ball deals
        //    more / takes less; damage ordering correct.
        // ---------------------------------------------------------------------
        [Test]
        public void UnequalMass_FasterLighterBall_DealsMoreAndTakesLess()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f);

            // A is heavy (mass 4), B is light (mass 1) => same closing speed, but B
            // (lighter) carries 4/5 of it and is the faster aggressor.
            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 20f; // closing speed 20
            var input = new ContactInput(relVel, normal, massA: 4f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            // shareA = Mb/(Ma+Mb) = 1/5 = 0.2 (A's slow share)
            // shareB = Ma/(Ma+Mb) = 4/5 = 0.8 (B's fast share)
            // damageA = shareB * excess * k = 0.8 * 15 = 12  (A takes a lot)
            // damageB = shareA * excess * k = 0.2 * 15 = 3   (B takes little)
            const float excess = 15f;
            float expectedDamageA = 0.8f * excess;
            float expectedDamageB = 0.2f * excess;
            Assert.AreEqual(expectedDamageA, o.DamageA, Tol, "heavy/slow A should take the larger hit");
            Assert.AreEqual(expectedDamageB, o.DamageB, Tol, "light/fast B should take the smaller hit");

            // The faster (lighter) ball deals more (A takes more than B takes) and takes less.
            Assert.Greater(o.DamageA, o.DamageB,
                "the ball struck by the faster aggressor (A here) must take more damage");
            // "deals more": damage dealt BY B to A == DamageA; damage dealt BY A to B == DamageB.
            Assert.Greater(o.DamageA, o.DamageB, "faster ball B deals more damage (to A) than it receives");
        }

        [Test]
        public void UnequalMass_OtherOrientation_FasterLighterBall_DealsMoreAndTakesLess()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f);

            // Now A is light (mass 1), B is heavy (mass 4) => A is the faster aggressor.
            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 20f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 4f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            // shareA = Mb/(Ma+Mb) = 4/5 = 0.8 (A's fast share)
            // shareB = Ma/(Ma+Mb) = 1/5 = 0.2 (B's slow share)
            // damageA = shareB * excess = 0.2 * 15 = 3  (A takes little)
            // damageB = shareA * excess = 0.8 * 15 = 12 (B takes a lot)
            const float excess = 15f;
            Assert.AreEqual(0.2f * excess, o.DamageA, Tol, "light/fast A should take the smaller hit");
            Assert.AreEqual(0.8f * excess, o.DamageB, Tol, "heavy/slow B should take the larger hit");
            Assert.Greater(o.DamageB, o.DamageA, "faster ball A deals more (to B) than it receives");
        }

        // ---------------------------------------------------------------------
        // 4. Ball-to-wall (MassB = +inf): wall takes none; ball takes damage
        //    scaled by its own normal speed; impulse reflects.
        // ---------------------------------------------------------------------
        [Test]
        public void BallToWall_WallTakesNoDamage_BallDamagedByOwnSpeed_ImpulseReflects()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f, kbPerSpeed: 2f);

            // Ball A slams a wall (B). Closing speed = ball's own speed = 20.
            Vector3 normal = Vector3.left; // wall(B) -> ball(A) points left
            Vector3 relVel = -normal * 20f; // ball moving right into the wall
            var input = new ContactInput(relVel, normal, massA: 1f, massB: float.PositiveInfinity);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            // Wall takes nothing.
            Assert.AreEqual(0f, o.DamageB, Tol, "wall must take no damage");
            Assert.AreEqual(Vector3.zero, o.ImpulseB, "wall must receive no impulse");

            // Ball takes damage scaled by its own closing speed: excess = 20 - 5 = 15.
            Assert.AreEqual(15f, o.DamageA, Tol, "ball damage = excess * DamagePerSpeed");

            // No NaN/Inf anywhere in the output.
            Assert.IsFalse(float.IsNaN(o.DamageA) || float.IsInfinity(o.DamageA), "DamageA finite");
            Assert.IsFalse(float.IsNaN(o.DamageB) || float.IsInfinity(o.DamageB), "DamageB finite");
            Assert.IsFalse(float.IsNaN(o.ImpulseA.x) || float.IsInfinity(o.ImpulseA.x), "ImpulseA finite");
            Assert.IsFalse(float.IsNaN(o.ImpulseB.x) || float.IsInfinity(o.ImpulseB.x), "ImpulseB finite");

            // Impulse reflects: A pushed back along +Normal (away from wall).
            // impulseMag = excess * kbPerSpeed = 15 * 2 = 30.
            Assert.AreEqual(normal * 30f, o.ImpulseA, "ball impulse must reflect along +Normal");
        }

        [Test]
        public void BallToWall_BelowThreshold_DealsNoDamage()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f);

            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 3f; // below threshold
            var input = new ContactInput(relVel, normal, massA: 1f, massB: float.PositiveInfinity);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.AreEqual(0f, o.DamageA, Tol);
            Assert.AreEqual(0f, o.DamageB, Tol);
            Assert.AreEqual(Vector3.zero, o.ImpulseA);
            Assert.AreEqual(Vector3.zero, o.ImpulseB);
            Assert.IsFalse(float.IsNaN(o.DamageA) || float.IsInfinity(o.DamageA));
        }

        // ---------------------------------------------------------------------
        // 5. Perfect-hit window: near-dead-on hit above PerfectHitMinSpeed and
        //    within PerfectHitMaxAngleDeg -> true; just outside -> false.
        // ---------------------------------------------------------------------
        [Test]
        public void PerfectHit_DeadOnAboveMinSpeed_IsTrue()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            // Perfectly dead-on: RelativeVelocity exactly along -Normal, speed 30 (> 15).
            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 30f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsTrue(o.PerfectHit, "dead-on hit above min speed within angle window must be perfect");
        }

        [Test]
        public void PerfectHit_JustInsideAngleWindow_IsTrue()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            // 5 degrees off dead-on — inside the 10-degree window.
            Vector3 normal = Vector3.left;
            Quaternion rot = Quaternion.AngleAxis(5f, Vector3.up);
            Vector3 approachDir = rot * (-normal); // approach dir 5deg off -Normal
            Vector3 relVel = approachDir * 30f;    // speed 30 along that approach dir
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsTrue(o.PerfectHit, "hit 5deg off dead-on (within 10deg window) above min speed must be perfect");
        }

        [Test]
        public void PerfectHit_JustOutsideAngleWindow_IsFalse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            // 20 degrees off dead-on — outside the 10-degree window.
            Vector3 normal = Vector3.left;
            Quaternion rot = Quaternion.AngleAxis(20f, Vector3.up);
            Vector3 approachDir = rot * (-normal);
            Vector3 relVel = approachDir * 30f; // still fast enough, just too angled
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsFalse(o.PerfectHit, "hit 20deg off dead-on (outside 10deg window) must NOT be perfect");
        }

        [Test]
        public void PerfectHit_DeadOnButBelowMinSpeed_IsFalse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            // Dead-on but only 10 m/s — below the 15 min-speed for a perfect hit.
            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 10f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsFalse(o.PerfectHit, "dead-on but below PerfectHitMinSpeed must NOT be perfect");
        }

        [Test]
        public void PerfectHit_WallDeadOnFast_IsTrue()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            // Ramming a wall dead-on and fast: still a perfect hit.
            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 40f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: float.PositiveInfinity);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsTrue(o.PerfectHit, "dead-on fast wall hit must be a perfect hit");
            Assert.IsFalse(float.IsNaN(o.DamageA) || float.IsInfinity(o.DamageA));
        }

        // ---------------------------------------------------------------------
        // 6. Determinism: identical inputs -> identical outputs across calls.
        // ---------------------------------------------------------------------
        [Test]
        public void Determinism_IdenticalInputs_IdenticalOutputs_AcrossRepeatedCalls()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = Vector3.left;
            Vector3 relVel = -normal * 30f;
            var input = new ContactInput(relVel, normal, massA: 2f, massB: 3f);

            ContactOutcome first = CombatResolver.Resolve(in input, cfg);
            for (int i = 0; i < 50; i++)
            {
                ContactOutcome again = CombatResolver.Resolve(in input, cfg);
                Assert.AreEqual(first.DamageA, again.DamageA, 0f, "DamageA must be bit-stable across calls");
                Assert.AreEqual(first.DamageB, again.DamageB, 0f, "DamageB must be bit-stable across calls");
                Assert.AreEqual(first.ImpulseA, again.ImpulseA, "ImpulseA must be bit-stable across calls");
                Assert.AreEqual(first.ImpulseB, again.ImpulseB, "ImpulseB must be bit-stable across calls");
                Assert.AreEqual(first.PerfectHit, again.PerfectHit, "PerfectHit must be stable across calls");
            }
        }

        [Test]
        public void Determinism_WallContact_IdenticalOutputs_AcrossRepeatedCalls()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = Vector3.forward;
            Vector3 relVel = -normal * 25f;
            var input = new ContactInput(relVel, normal, massA: 1.5f, massB: float.PositiveInfinity);

            ContactOutcome first = CombatResolver.Resolve(in input, cfg);
            for (int i = 0; i < 50; i++)
            {
                ContactOutcome again = CombatResolver.Resolve(in input, cfg);
                Assert.AreEqual(first.DamageA, again.DamageA, 0f);
                Assert.AreEqual(first.DamageB, again.DamageB, 0f);
                Assert.AreEqual(first.ImpulseA, again.ImpulseA);
                Assert.AreEqual(first.ImpulseB, again.ImpulseB);
                Assert.AreEqual(first.PerfectHit, again.PerfectHit);
                // And never any NaN/Inf leaking from the infinite-mass math.
                Assert.IsFalse(float.IsNaN(again.DamageA) || float.IsInfinity(again.DamageA));
                Assert.IsFalse(float.IsNaN(again.DamageB) || float.IsInfinity(again.DamageB));
            }
        }
    }
}
