using System;
using System.Numerics;
using NUnit.Framework;
using Ball2.Core.Combat;

namespace Ball2.Tests.EditMode
{
    /// <summary>
    /// Tests for <see cref="CombatResolver"/> — covers every acceptance criterion
    /// of ticket B2-007 (spec §3.2 momentum-differential combat model).
    ///
    /// Conventions exercised here (binding on the resolver):
    ///   - Normal points from B toward A (so A reflects along +Normal).
    ///   - RelativeVelocity = vA - vB; a closing contact has dot(RelativeVelocity, Normal) &lt; 0.
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

        [Test]
        public void BelowThreshold_GlancingTap_DealsNoDamageAndNoImpulse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f);

            Vector3 normal = -Vector3.UnitX; // B -> A points left (A is to the left of B)
            Vector3 relVel = -normal * 3f;   // vA-vB points right (toward B) => closing
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.AreEqual(0f, o.DamageA, Tol, "DamageA must be zero below threshold");
            Assert.AreEqual(0f, o.DamageB, Tol, "DamageB must be zero below threshold");
            Assert.AreEqual(Vector3.Zero, o.ImpulseA, "ImpulseA must be zero below threshold");
            Assert.AreEqual(Vector3.Zero, o.ImpulseB, "ImpulseB must be zero below threshold");
        }

        [Test]
        public void SeparatingContact_DealsNoDamageAndNoImpulse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = normal * 10f; // separating
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.AreEqual(0f, o.DamageA, Tol);
            Assert.AreEqual(0f, o.DamageB, Tol);
            Assert.AreEqual(Vector3.Zero, o.ImpulseA);
            Assert.AreEqual(Vector3.Zero, o.ImpulseB);
        }

        [Test]
        public void HeadOn_EqualMass_EqualSpeed_BothTakeEqualDamage_SymmetricImpulse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f, kbPerSpeed: 2f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 20f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            const float expectedDamage = 0.5f * 15f * 1f;
            Assert.AreEqual(expectedDamage, o.DamageA, Tol, "DamageA must equal DamageB (symmetric)");
            Assert.AreEqual(expectedDamage, o.DamageB, Tol, "DamageB must equal DamageA (symmetric)");
            Assert.AreEqual(o.DamageA, o.DamageB, Tol, "Damage must be equal for equal mass / equal speed");

            const float expectedImpulseMag = 15f * 2f;
            Assert.AreEqual(normal * expectedImpulseMag, o.ImpulseA, "ImpulseA along +Normal");
            Assert.AreEqual(-normal * expectedImpulseMag, o.ImpulseB, "ImpulseB along -Normal");
            Assert.AreEqual(o.ImpulseA, -o.ImpulseB, "Impulses must be equal and opposite");
            Assert.AreEqual(o.ImpulseA.Length(), o.ImpulseB.Length(), Tol, "Impulse magnitudes equal");
        }

        [Test]
        public void UnequalMass_FasterLighterBall_DealsMoreAndTakesLess()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 20f;
            var input = new ContactInput(relVel, normal, massA: 4f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            const float excess = 15f;
            float expectedDamageA = 0.8f * excess;
            float expectedDamageB = 0.2f * excess;
            Assert.AreEqual(expectedDamageA, o.DamageA, Tol, "heavy/slow A should take the larger hit");
            Assert.AreEqual(expectedDamageB, o.DamageB, Tol, "light/fast B should take the smaller hit");
            Assert.Greater(o.DamageA, o.DamageB,
                "the ball struck by the faster aggressor (A here) must take more damage");
            Assert.Greater(o.DamageA, o.DamageB, "faster ball B deals more damage (to A) than it receives");
        }

        [Test]
        public void UnequalMass_OtherOrientation_FasterLighterBall_DealsMoreAndTakesLess()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 20f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 4f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            const float excess = 15f;
            Assert.AreEqual(0.2f * excess, o.DamageA, Tol, "light/fast A should take the smaller hit");
            Assert.AreEqual(0.8f * excess, o.DamageB, Tol, "heavy/slow B should take the larger hit");
            Assert.Greater(o.DamageB, o.DamageA, "faster ball A deals more (to B) than it receives");
        }

        [Test]
        public void BallToWall_WallTakesNoDamage_BallDamagedByOwnSpeed_ImpulseReflects()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, dmgPerSpeed: 1f, kbPerSpeed: 2f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 20f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: float.PositiveInfinity);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.AreEqual(0f, o.DamageB, Tol, "wall must take no damage");
            Assert.AreEqual(Vector3.Zero, o.ImpulseB, "wall must receive no impulse");
            Assert.AreEqual(15f, o.DamageA, Tol, "ball damage = excess * DamagePerSpeed");

            Assert.IsFalse(float.IsNaN(o.DamageA) || float.IsInfinity(o.DamageA), "DamageA finite");
            Assert.IsFalse(float.IsNaN(o.DamageB) || float.IsInfinity(o.DamageB), "DamageB finite");
            Assert.IsFalse(float.IsNaN(o.ImpulseA.X) || float.IsInfinity(o.ImpulseA.X), "ImpulseA finite");
            Assert.IsFalse(float.IsNaN(o.ImpulseB.X) || float.IsInfinity(o.ImpulseB.X), "ImpulseB finite");

            Assert.AreEqual(normal * 30f, o.ImpulseA, "ball impulse must reflect along +Normal");
        }

        [Test]
        public void BallToWall_BelowThreshold_DealsNoDamage()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 3f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: float.PositiveInfinity);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.AreEqual(0f, o.DamageA, Tol);
            Assert.AreEqual(0f, o.DamageB, Tol);
            Assert.AreEqual(Vector3.Zero, o.ImpulseA);
            Assert.AreEqual(Vector3.Zero, o.ImpulseB);
            Assert.IsFalse(float.IsNaN(o.DamageA) || float.IsInfinity(o.DamageA));
        }

        [Test]
        public void PerfectHit_DeadOnAboveMinSpeed_IsTrue()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 30f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsTrue(o.PerfectHit, "dead-on hit above min speed within angle window must be perfect");
        }

        [Test]
        public void PerfectHit_JustInsideAngleWindow_IsTrue()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = -Vector3.UnitX;
            Quaternion rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 5f * MathF.PI / 180f);
            Vector3 approachDir = Vector3.Transform(-normal, rot);
            Vector3 relVel = approachDir * 30f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsTrue(o.PerfectHit, "hit 5deg off dead-on (within 10deg window) above min speed must be perfect");
        }

        [Test]
        public void PerfectHit_JustOutsideAngleWindow_IsFalse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = -Vector3.UnitX;
            Quaternion rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 20f * MathF.PI / 180f);
            Vector3 approachDir = Vector3.Transform(-normal, rot);
            Vector3 relVel = approachDir * 30f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsFalse(o.PerfectHit, "hit 20deg off dead-on (outside 10deg window) must NOT be perfect");
        }

        [Test]
        public void PerfectHit_DeadOnButBelowMinSpeed_IsFalse()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 10f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: 1f);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsFalse(o.PerfectHit, "dead-on but below PerfectHitMinSpeed must NOT be perfect");
        }

        [Test]
        public void PerfectHit_WallDeadOnFast_IsTrue()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = -Vector3.UnitX;
            Vector3 relVel = -normal * 40f;
            var input = new ContactInput(relVel, normal, massA: 1f, massB: float.PositiveInfinity);

            ContactOutcome o = CombatResolver.Resolve(in input, cfg);

            Assert.IsTrue(o.PerfectHit, "dead-on fast wall hit must be a perfect hit");
            Assert.IsFalse(float.IsNaN(o.DamageA) || float.IsInfinity(o.DamageA));
        }

        [Test]
        public void Determinism_IdenticalInputs_IdenticalOutputs_AcrossRepeatedCalls()
        {
            CombatConfig cfg = MakeConfig(threshold: 5f, phMinSpeed: 15f, phMaxAngle: 10f);

            Vector3 normal = -Vector3.UnitX;
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

            Vector3 normal = Vector3.UnitZ;
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
                Assert.IsFalse(float.IsNaN(again.DamageA) || float.IsInfinity(again.DamageA));
                Assert.IsFalse(float.IsNaN(again.DamageB) || float.IsInfinity(again.DamageB));
            }
        }
    }
}
