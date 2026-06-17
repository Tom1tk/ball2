using System;
using NUnit.Framework;
using UnityEngine;
using Ball2.Core.Combat;

namespace Ball2.Tests.EditMode
{
    public class LockOnResolverTests
    {
        // A unit aim pointing down +Z.
        private static readonly Vector3 AimForward = Vector3.forward;

        private static LockOnConfig DefaultConfig()
        {
            // Fresh config each test so tests don't share mutable state.
            return new LockOnConfig
            {
                Range = 18f,
                AcquisitionAngleDeg = 20f,
                TrackingStrength = 0.35f,
                DodgeToleranceDeg = 45f,
                BreakRange = 18f,
                Epsilon = 1e-5f
            };
        }

        // ---------------------------------------------------------------------
        // 1. No candidate near reticle / out of (short) range → no lock;
        //    boost treated as normal (lock state = none).
        // ---------------------------------------------------------------------

        [Test]
        public void NoCandidates_NoLock_BoostNormal()
        {
            var cfg = DefaultConfig();
            var input = new LockOnInput(
                playerPosition: Vector3.zero,
                aimDirection: AimForward,
                candidates: Array.Empty<LockOnCandidate>(),
                currentLock: LockState.None,
                boostChargeStarted: false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "should have no lock");
            Assert.AreEqual(-1, result.NewLock.TargetId);
            Assert.IsFalse(result.IconShouldShow);
            Assert.AreEqual(-1, result.IconTargetId);
        }

        [Test]
        public void NullCandidates_NoLock()
        {
            var cfg = DefaultConfig();
            var input = new LockOnInput(
                playerPosition: Vector3.zero,
                aimDirection: AimForward,
                candidates: null,
                currentLock: LockState.None,
                boostChargeStarted: false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock);
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void EnemyOutOfRange_NoLock()
        {
            var cfg = DefaultConfig();
            // 30 units ahead — beyond the 18 unit range.
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 30), Vector3.zero);
            var input = new LockOnInput(
                Vector3.zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "out-of-range enemy must not acquire");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void EnemyOffReticle_NoLock()
        {
            var cfg = DefaultConfig();
            // Enemy close enough (10 units) but 90 degrees off the aim ray.
            var enemy = new LockOnCandidate(1, new Vector3(10, 0, 0), Vector3.zero);
            var input = new LockOnInput(
                Vector3.zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "enemy far off reticle must not acquire");
            Assert.IsFalse(result.IconShouldShow);
        }

        // ---------------------------------------------------------------------
        // 2. A valid enemy near/on the reticle within range → lock acquired;
        //    resolver reports "icon should show" for that enemy.
        // ---------------------------------------------------------------------

        [Test]
        public void EnemyOnReticleInRange_AcquiresLock_IconShows()
        {
            var cfg = DefaultConfig();
            // 10 units straight ahead, dead-centre.
            var enemy = new LockOnCandidate(7, new Vector3(0, 0, 10), Vector3.zero);
            var input = new LockOnInput(
                Vector3.zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsTrue(result.NewLock.HasLock, "on-reticle in-range enemy should acquire");
            Assert.AreEqual(7, result.NewLock.TargetId, "should lock the candidate's id");
            Assert.IsTrue(result.IconShouldShow, "icon should show for a lock");
            Assert.AreEqual(7, result.IconTargetId, "icon should target the locked enemy");
        }

        [Test]
        public void EnemyNearReticleWithinAcquisitionAngle_Acquires()
        {
            var cfg = DefaultConfig();
            // 12 units ahead, offset so the aim-to-enemy angle is ~14deg (< 20 threshold).
            // angle = atan2(3, 11.83) ≈ 14.2deg.
            var enemy = new LockOnCandidate(2, new Vector3(3f, 0f, 11.83f), Vector3.zero);
            var input = new LockOnInput(
                Vector3.zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsTrue(result.NewLock.HasLock);
            Assert.AreEqual(2, result.NewLock.TargetId);
            Assert.IsTrue(result.IconShouldShow);
        }

        [Test]
        public void MultipleCandidates_PicksClosestToReticleCentre()
        {
            var cfg = DefaultConfig();
            // A: 5deg off, 10 units.  B: 2deg off, 10 units. B should win (closer to centre).
            var a = new LockOnCandidate(1, new Vector3(0.87f, 0, 10f), Vector3.zero); // ~5deg
            var b = new LockOnCandidate(2, new Vector3(0.35f, 0, 10f), Vector3.zero); // ~2deg
            var input = new LockOnInput(
                Vector3.zero, AimForward, new[] { a, b }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.AreEqual(2, result.NewLock.TargetId, "should prefer the more-centred candidate");
        }

        // ---------------------------------------------------------------------
        // 3. Soft tracking: as the locked enemy moves modestly, lock is retained;
        //    tracking strength respects config (does not snap perfectly).
        // ---------------------------------------------------------------------

        [Test]
        public void SoftTracking_LockRetained_AndDoesNotSnapPerfectly()
        {
            var cfg = DefaultConfig();
            // Acquire at 10 units ahead.
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquireInput = new LockOnInput(
                Vector3.zero, AimForward, new[] { enemy }, LockState.None, false);
            var acquired = LockOnResolver.Resolve(in acquireInput, cfg);
            Assert.IsTrue(acquired.NewLock.HasLock);
            // After acquisition the tracking position is seeded at the enemy position.
            Assert.AreEqual(new Vector3(0, 0, 10), acquired.NewLock.TrackingPosition);

            // Now the enemy moves 1 unit to the right. With TrackingStrength 0.35,
            // the tracking position should move 35% of the way — NOT snap to the enemy.
            var movedEnemy = new LockOnCandidate(1, new Vector3(1, 0, 10), Vector3.zero);
            var trackInput = new LockOnInput(
                Vector3.zero, AimForward, new[] { movedEnemy }, acquired.NewLock, false);
            var tracked = LockOnResolver.Resolve(in trackInput, cfg);

            Assert.IsTrue(tracked.NewLock.HasLock, "modest enemy movement should retain lock");
            Assert.AreEqual(1, tracked.NewLock.TargetId);

            Vector3 tp = tracked.NewLock.TrackingPosition;
            // Expected: lerp from (0,0,10) toward (1,0,10) by 0.35 => (0.35, 0, 10).
            Assert.AreEqual(0.35f, tp.x, 1e-4f, "tracking should advance by TrackingStrength, not snap");
            Assert.AreEqual(10f, tp.z, 1e-4f, "unchanged axis should stay");
            Assert.AreNotEqual(movedEnemy.Position, tp, "must not snap perfectly to enemy");
        }

        [Test]
        public void SoftTracking_StrengthZero_FrozenAimPoint()
        {
            var cfg = DefaultConfig();
            cfg.TrackingStrength = 0f;
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var moved = new LockOnCandidate(1, new Vector3(5, 0, 10), Vector3.zero);
            var tracked = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { moved }, acquired.NewLock, false), cfg);

            Assert.IsTrue(tracked.NewLock.HasLock);
            Assert.AreEqual(new Vector3(0, 0, 10), tracked.NewLock.TrackingPosition,
                "TrackingStrength 0 should freeze the aim point");
        }

        [Test]
        public void SoftTracking_StrengthOne_SnapsToEnemy()
        {
            var cfg = DefaultConfig();
            cfg.TrackingStrength = 1f;
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var moved = new LockOnCandidate(1, new Vector3(5, 0, 10), Vector3.zero);
            var tracked = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { moved }, acquired.NewLock, false), cfg);

            Assert.AreEqual(new Vector3(5, 0, 10), tracked.NewLock.TrackingPosition,
                "TrackingStrength 1 should snap to enemy");
        }

        // ---------------------------------------------------------------------
        // 4. Lock breaks when the enemy leaves range OR moves outside the
        //    dodge tolerance.
        // ---------------------------------------------------------------------

        [Test]
        public void LockBreaks_EnemyLeavesRange()
        {
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { enemy }, LockState.None, false), cfg);
            Assert.IsTrue(acquired.NewLock.HasLock);

            // Enemy flies 30 units away — beyond break range (18). Aim still on it.
            var farEnemy = new LockOnCandidate(1, new Vector3(0, 0, 30), Vector3.zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { farEnemy }, acquired.NewLock, false), cfg);

            Assert.IsFalse(result.NewLock.HasLock, "lock must break when enemy leaves break range");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void LockBreaks_EnemyDodgesOutsideTolerance()
        {
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { enemy }, LockState.None, false), cfg);
            Assert.IsTrue(acquired.NewLock.HasLock);

            // Player keeps aiming forward, but the enemy strafes ~90deg off — well past
            // the 45deg dodge tolerance. Still in range.
            var dodged = new LockOnCandidate(1, new Vector3(10, 0, 0), Vector3.zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { dodged }, acquired.NewLock, false), cfg);

            Assert.IsFalse(result.NewLock.HasLock, "lock must break when enemy dodges past tolerance");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void LockHolds_EnemyStaysWithinDodgeTolerance()
        {
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            // Enemy shifts so the aim-to-enemy angle is ~30deg — within the 45deg tolerance.
            var shifted = new LockOnCandidate(1, new Vector3(5.77f, 0, 10f), Vector3.zero); // ~30deg
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { shifted }, acquired.NewLock, false), cfg);

            Assert.IsTrue(result.NewLock.HasLock, "lock should hold within dodge tolerance");
        }

        [Test]
        public void LockBreaks_BreakRangeLargerThanAcquisitionRange()
        {
            var cfg = DefaultConfig();
            cfg.BreakRange = 25f; // hold band wider than the 18 acquisition range

            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            // 22 units: beyond acquisition range but within break range — lock holds.
            var at22 = new LockOnCandidate(1, new Vector3(0, 0, 22), Vector3.zero);
            var hold = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { at22 }, acquired.NewLock, false), cfg);
            Assert.IsTrue(hold.NewLock.HasLock, "should hold within break range");

            // 27 units: beyond break range — lock breaks.
            var at27 = new LockOnCandidate(1, new Vector3(0, 0, 27), Vector3.zero);
            var broken = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { at27 }, acquired.NewLock, false), cfg);
            Assert.IsFalse(broken.NewLock.HasLock, "should break beyond break range");
        }

        // ---------------------------------------------------------------------
        // 5. Once boostChargeStarted == true, the locked target cannot change
        //    even if another enemy becomes a better candidate (commit-on-charge).
        // ---------------------------------------------------------------------

        [Test]
        public void CommitOnCharge_DoesNotSwitchToBetterCandidate()
        {
            var cfg = DefaultConfig();
            // Lock enemy A (5deg off, 10 units).
            var a = new LockOnCandidate(1, new Vector3(0.87f, 0, 10f), Vector3.zero); // ~5deg
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { a }, LockState.None, false), cfg);
            Assert.AreEqual(1, acquired.NewLock.TargetId);

            // Now charging, and enemy B (dead-centre, 10 units — a strictly better candidate)
            // appears. The lock must stay on A.
            var b = new LockOnCandidate(2, new Vector3(0, 0, 10), Vector3.zero);
            var both = new[] { a, b };
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, both, acquired.NewLock, boostChargeStarted: true), cfg);

            Assert.AreEqual(1, result.NewLock.TargetId, "must not switch target once charging");
            Assert.IsTrue(result.IconShouldShow);
        }

        [Test]
        public void CommitOnCharge_BreakConditionsStillApply()
        {
            var cfg = DefaultConfig();
            var a = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { a }, LockState.None, false), cfg);

            // Charging, and A leaves range. Lock breaks — and does NOT re-acquire B,
            // because we are mid-charge (commit-on-charge forbids switching).
            var aFar = new LockOnCandidate(1, new Vector3(0, 0, 30), Vector3.zero);
            var b = new LockOnCandidate(2, new Vector3(0, 0, 10), Vector3.zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { aFar, b }, acquired.NewLock, true), cfg);

            Assert.IsFalse(result.NewLock.HasLock,
                "charging lock breaks on range, and must NOT switch to B mid-charge");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void CommitOnCharge_NotCharging_CanSwitchToBetterCandidate()
        {
            var cfg = DefaultConfig();
            var a = new LockOnCandidate(1, new Vector3(0.87f, 0, 10f), Vector3.zero); // ~5deg
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { a }, LockState.None, false), cfg);

            // NOT charging: when A breaks (dodges), a better candidate B can be acquired.
            var aDodged = new LockOnCandidate(1, new Vector3(10, 0, 0), Vector3.zero); // 90deg, breaks
            var b = new LockOnCandidate(2, new Vector3(0, 0, 10), Vector3.zero);       // centred
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { aDodged, b }, acquired.NewLock, false), cfg);

            Assert.AreEqual(2, result.NewLock.TargetId,
                "when not charging and the lock breaks, a better candidate may be acquired");
        }

        // ---------------------------------------------------------------------
        // 6. Determinism: identical inputs → byte-identical outputs across
        //    repeated calls; no UnityEngine.Random, no time/frame dependence.
        // ---------------------------------------------------------------------

        [Test]
        public void Determinism_IdenticalInputsProduceIdenticalOutputs()
        {
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0.5f, -0.3f, 12f), new Vector3(0.1f, 0, 0));
            var input = new LockOnInput(
                new Vector3(1, 2, 3), AimForward, new[] { enemy }, LockState.None, false);

            LockOnResult r1 = LockOnResolver.Resolve(in input, cfg);
            LockOnResult r2 = LockOnResolver.Resolve(in input, cfg);
            LockOnResult r3 = LockOnResolver.Resolve(in input, cfg);

            Assert.IsTrue(ResultsEqual(r1, r2), "r1 == r2");
            Assert.IsTrue(ResultsEqual(r2, r3), "r2 == r3");
        }

        [Test]
        public void Determinism_TrackingRepeatedProducesIdenticalOutputs()
        {
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var moved = new LockOnCandidate(1, new Vector3(1, 0, 10), Vector3.zero);
            var input = new LockOnInput(Vector3.zero, AimForward, new[] { moved }, acquired.NewLock, false);

            LockOnResult r1 = LockOnResolver.Resolve(in input, cfg);
            LockOnResult r2 = LockOnResolver.Resolve(in input, cfg);
            LockOnResult r3 = LockOnResolver.Resolve(in input, cfg);

            Assert.IsTrue(ResultsEqual(r1, r2));
            Assert.IsTrue(ResultsEqual(r2, r3));
        }

        [Test]
        public void Determinism_NoFrameOrTimeDependence()
        {
            // Call resolve many times in a loop with the SAME inputs; the result must
            // never drift. This guards against hidden time/frame reads or RNG.
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(42, new Vector3(0, 1, 9), Vector3.zero);
            var input = new LockOnInput(
                new Vector3(0, 0, 0), AimForward, new[] { enemy }, LockState.None, false);

            LockOnResult first = LockOnResolver.Resolve(in input, cfg);
            for (int i = 0; i < 1000; i++)
            {
                LockOnResult r = LockOnResolver.Resolve(in input, cfg);
                Assert.IsTrue(ResultsEqual(first, r), $"result drifted at iteration {i}");
            }
        }

        // ---------------------------------------------------------------------
        // Edge cases that protect the invariants above.
        // ---------------------------------------------------------------------

        [Test]
        public void ZeroAimDirection_NoLock()
        {
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.zero);
            var input = new LockOnInput(
                Vector3.zero, Vector3.zero, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "degenerate aim must not acquire");
        }

        [Test]
        public void EnemyOnTopOfPlayer_NotAcquired()
        {
            var cfg = DefaultConfig();
            var enemy = new LockOnCandidate(1, Vector3.zero, Vector3.zero);
            var input = new LockOnInput(
                Vector3.zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            // Zero distance to enemy → degenerate direction; no acquisition.
            Assert.IsFalse(result.NewLock.HasLock);
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static bool ResultsEqual(LockOnResult a, LockOnResult b)
        {
            return a.NewLock.TargetId == b.NewLock.TargetId
                && a.IconTargetId == b.IconTargetId
                && a.IconShouldShow == b.IconShouldShow
                && VectorsEqual(a.NewLock.TrackingPosition, b.NewLock.TrackingPosition);
        }

        private static bool VectorsEqual(Vector3 a, Vector3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }
    }
}
