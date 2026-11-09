using System;
using System.Numerics;
using NUnit.Framework;
using Ball2.Core.Combat;

namespace Ball2.Tests.EditMode
{
    public class LockOnResolverTests
    {
        // A unit aim pointing down +Z.
        private static readonly Vector3 AimForward = Vector3.UnitZ;

        [Test]
        public void NoCandidates_NoLock_BoostNormal()
        {
            var cfg = new LockOnConfig();
            var input = new LockOnInput(
                playerPosition: Vector3.Zero,
                aimDirection: AimForward,
                candidates: Array.Empty<LockOnCandidate>(),
                currentLock: LockState.None,
                boostChargeStarted: false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "should have no lock");
            Assert.AreEqual(int.MinValue, result.NewLock.TargetId);
            Assert.IsFalse(result.IconShouldShow);
            Assert.AreEqual(-1, result.IconTargetId);
        }

        [Test]
        public void NullCandidates_NoLock()
        {
            var cfg = new LockOnConfig();
            var input = new LockOnInput(
                playerPosition: Vector3.Zero,
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
            var cfg = new LockOnConfig { Range = 15f };
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 30), Vector3.Zero);
            var input = new LockOnInput(
                Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "out-of-range enemy must not acquire");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void EnemyOffReticle_NoLock()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(1, new Vector3(10, 0, 0), Vector3.Zero);
            var input = new LockOnInput(
                Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "enemy far off reticle must not acquire");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void EnemyOnReticleInRange_AcquiresLock_IconShows()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(7, new Vector3(0, 0, 10), Vector3.Zero);
            var input = new LockOnInput(
                Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsTrue(result.NewLock.HasLock, "on-reticle in-range enemy should acquire");
            Assert.AreEqual(7, result.NewLock.TargetId, "should lock the candidate's id");
            Assert.IsTrue(result.IconShouldShow, "icon should show for a lock");
            Assert.AreEqual(7, result.IconTargetId, "icon should target the locked enemy");
        }

        [Test]
        public void EnemyNearReticleWithinAcquisitionAngle_Acquires()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(2, new Vector3(3f, 0f, 11.83f), Vector3.Zero);
            var input = new LockOnInput(
                Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsTrue(result.NewLock.HasLock);
            Assert.AreEqual(2, result.NewLock.TargetId);
            Assert.IsTrue(result.IconShouldShow);
        }

        [Test]
        public void MultipleCandidates_PicksClosestToReticleCentre()
        {
            var cfg = new LockOnConfig();
            var a = new LockOnCandidate(1, new Vector3(0.87f, 0, 10f), Vector3.Zero);
            var b = new LockOnCandidate(2, new Vector3(0.35f, 0, 10f), Vector3.Zero);
            var input = new LockOnInput(
                Vector3.Zero, AimForward, new[] { a, b }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.AreEqual(2, result.NewLock.TargetId, "should prefer the more-centred candidate");
        }

        [Test]
        public void SoftTracking_LockRetained_AndDoesNotSnapPerfectly()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquireInput = new LockOnInput(
                Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false);
            var acquired = LockOnResolver.Resolve(in acquireInput, cfg);
            Assert.IsTrue(acquired.NewLock.HasLock);
            Assert.AreEqual(new Vector3(0, 0, 10), acquired.NewLock.TrackingPosition);

            var movedEnemy = new LockOnCandidate(1, new Vector3(1, 0, 10), Vector3.Zero);
            var trackInput = new LockOnInput(
                Vector3.Zero, AimForward, new[] { movedEnemy }, acquired.NewLock, false);
            var tracked = LockOnResolver.Resolve(in trackInput, cfg);

            Assert.IsTrue(tracked.NewLock.HasLock, "modest enemy movement should retain lock");
            Assert.AreEqual(1, tracked.NewLock.TargetId);

            Vector3 tp = tracked.NewLock.TrackingPosition;
            Assert.AreEqual(0.35f, tp.X, 1e-4f, "tracking should advance by TrackingStrength, not snap");
            Assert.AreEqual(10f, tp.Z, 1e-4f, "unchanged axis should stay");
            Assert.AreNotEqual(movedEnemy.Position, tp, "must not snap perfectly to enemy");
        }

        [Test]
        public void SoftTracking_StrengthZero_FrozenAimPoint()
        {
            var cfg = new LockOnConfig();
            cfg.TrackingStrength = 0f;
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var moved = new LockOnCandidate(1, new Vector3(5, 0, 10), Vector3.Zero);
            var tracked = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { moved }, acquired.NewLock, false), cfg);

            Assert.IsTrue(tracked.NewLock.HasLock);
            Assert.AreEqual(new Vector3(0, 0, 10), tracked.NewLock.TrackingPosition,
                "TrackingStrength 0 should freeze the aim point");
        }

        [Test]
        public void SoftTracking_StrengthOne_SnapsToEnemy()
        {
            var cfg = new LockOnConfig();
            cfg.TrackingStrength = 1f;
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var moved = new LockOnCandidate(1, new Vector3(5, 0, 10), Vector3.Zero);
            var tracked = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { moved }, acquired.NewLock, false), cfg);

            Assert.AreEqual(new Vector3(5, 0, 10), tracked.NewLock.TrackingPosition,
                "TrackingStrength 1 should snap to enemy");
        }

        [Test]
        public void LockBreaks_EnemyLeavesRange()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false), cfg);
            Assert.IsTrue(acquired.NewLock.HasLock);

            var cfg2 = new LockOnConfig { Range = 20f, BreakRange = 15f };
            var farEnemy = new LockOnCandidate(1, new Vector3(0, 0, 30), Vector3.Zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { farEnemy }, acquired.NewLock, false), cfg2);

            Assert.IsFalse(result.NewLock.HasLock, "lock must break when enemy leaves break range");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void LockBreaks_EnemyDodgesOutsideTolerance()
        {
            var cfg = new LockOnConfig { DodgeToleranceDeg = 45f };
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false), cfg);
            Assert.IsTrue(acquired.NewLock.HasLock);

            var dodged = new LockOnCandidate(1, new Vector3(10, 0, 0), Vector3.Zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { dodged }, acquired.NewLock, false), cfg);

            Assert.IsFalse(result.NewLock.HasLock, "lock must break when enemy dodges past tolerance");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void LockHolds_EnemyStaysWithinDodgeTolerance()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var shifted = new LockOnCandidate(1, new Vector3(5.77f, 0, 10f), Vector3.Zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { shifted }, acquired.NewLock, false), cfg);

            Assert.IsTrue(result.NewLock.HasLock, "lock should hold within dodge tolerance");
        }

        [Test]
        public void LockBreaks_BreakRangeLargerThanAcquisitionRange()
        {
            var cfg = new LockOnConfig();
            cfg.Range = 20f;
            cfg.BreakRange = 25f;

            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var at22 = new LockOnCandidate(1, new Vector3(0, 0, 22), Vector3.Zero);
            var hold = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { at22 }, acquired.NewLock, false), cfg);
            Assert.IsTrue(hold.NewLock.HasLock, "should hold within break range");

            var at27 = new LockOnCandidate(1, new Vector3(0, 0, 27), Vector3.Zero);
            var broken = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { at27 }, acquired.NewLock, false), cfg);
            Assert.IsFalse(broken.NewLock.HasLock, "should break beyond break range");
        }

        [Test]
        public void CommitOnCharge_DoesNotSwitchToBetterCandidate()
        {
            var cfg = new LockOnConfig();
            var a = new LockOnCandidate(1, new Vector3(0.87f, 0, 10f), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { a }, LockState.None, false), cfg);
            Assert.AreEqual(1, acquired.NewLock.TargetId);

            var b = new LockOnCandidate(2, new Vector3(0, 0, 10), Vector3.Zero);
            var both = new[] { a, b };
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, both, acquired.NewLock, boostChargeStarted: true), cfg);

            Assert.AreEqual(1, result.NewLock.TargetId, "must not switch target once charging");
            Assert.IsTrue(result.IconShouldShow);
        }

        [Test]
        public void CommitOnCharge_BreakConditionsStillApply()
        {
            var cfg = new LockOnConfig { BreakRange = 15f };
            var a = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { a }, LockState.None, false), cfg);

            var aFar = new LockOnCandidate(1, new Vector3(0, 0, 30), Vector3.Zero);
            var b = new LockOnCandidate(2, new Vector3(0, 0, 10), Vector3.Zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { aFar, b }, acquired.NewLock, true), cfg);

            Assert.IsFalse(result.NewLock.HasLock,
                "charging lock breaks on range, and must NOT switch to B mid-charge");
            Assert.IsFalse(result.IconShouldShow);
        }

        [Test]
        public void CommitOnCharge_NotCharging_CanSwitchToBetterCandidate()
        {
            var cfg = new LockOnConfig { DodgeToleranceDeg = 45f };
            var a = new LockOnCandidate(1, new Vector3(0.87f, 0, 10f), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { a }, LockState.None, false), cfg);

            var aDodged = new LockOnCandidate(1, new Vector3(10, 0, 0), Vector3.Zero);
            var b = new LockOnCandidate(2, new Vector3(0, 0, 10), Vector3.Zero);
            var result = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { aDodged, b }, acquired.NewLock, false), cfg);

            Assert.AreEqual(2, result.NewLock.TargetId,
                "when not charging and the lock breaks, a better candidate may be acquired");
        }

        [Test]
        public void Determinism_IdenticalInputsProduceIdenticalOutputs()
        {
            var cfg = new LockOnConfig();
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
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var acquired = LockOnResolver.Resolve(
                new LockOnInput(Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false), cfg);

            var moved = new LockOnCandidate(1, new Vector3(1, 0, 10), Vector3.Zero);
            var input = new LockOnInput(Vector3.Zero, AimForward, new[] { moved }, acquired.NewLock, false);

            LockOnResult r1 = LockOnResolver.Resolve(in input, cfg);
            LockOnResult r2 = LockOnResolver.Resolve(in input, cfg);
            LockOnResult r3 = LockOnResolver.Resolve(in input, cfg);

            Assert.IsTrue(ResultsEqual(r1, r2));
            Assert.IsTrue(ResultsEqual(r2, r3));
        }

        [Test]
        public void Determinism_NoFrameOrTimeDependence()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(42, new Vector3(0, 1, 9), Vector3.Zero);
            var input = new LockOnInput(
                new Vector3(0, 0, 0), AimForward, new[] { enemy }, LockState.None, false);

            LockOnResult first = LockOnResolver.Resolve(in input, cfg);
            for (int i = 0; i < 1000; i++)
            {
                LockOnResult r = LockOnResolver.Resolve(in input, cfg);
                Assert.IsTrue(ResultsEqual(first, r), $"result drifted at iteration {i}");
            }
        }

        [Test]
        public void ZeroAimDirection_NoLock()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(1, new Vector3(0, 0, 10), Vector3.Zero);
            var input = new LockOnInput(
                Vector3.Zero, Vector3.Zero, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock, "degenerate aim must not acquire");
        }

        [Test]
        public void EnemyOnTopOfPlayer_NotAcquired()
        {
            var cfg = new LockOnConfig();
            var enemy = new LockOnCandidate(1, Vector3.Zero, Vector3.Zero);
            var input = new LockOnInput(
                Vector3.Zero, AimForward, new[] { enemy }, LockState.None, false);

            var result = LockOnResolver.Resolve(in input, cfg);

            Assert.IsFalse(result.NewLock.HasLock);
        }

        private static bool ResultsEqual(LockOnResult a, LockOnResult b)
        {
            return a.NewLock.TargetId == b.NewLock.TargetId
                && a.IconTargetId == b.IconTargetId
                && a.IconShouldShow == b.IconShouldShow
                && VectorsEqual(a.NewLock.TrackingPosition, b.NewLock.TrackingPosition);
        }

        private static bool VectorsEqual(Vector3 a, Vector3 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }
    }
}
