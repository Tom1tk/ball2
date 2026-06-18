using UnityEngine;
using Ball2.Core.Combat;

namespace Ball2.Gameplay
{
    /// <summary>
    /// Player-side lock-on adapter (B2-011). Gathers enemy candidates each tick, calls
    /// the pure <see cref="LockOnResolver"/>, and exposes the result (lock state, icon
    /// target, tracking position) to the rest of the gameplay layer.
    /// </summary>
    /// <remarks>
    /// <b>Contract honoured.</b>
    /// <list type="bullet">
    /// <item><see cref="LockOnResult.IconTargetId"/> + <see cref="LockOnResult.IconShouldShow"/>
    /// drive the hover icon (rendered in <c>OnGUI</c>).</item>
    /// <item><see cref="LockState.TrackingPosition"/> is the boost aim point — BallMovement
    /// reads it via <see cref="TrackingPosition"/> during boost release.</item>
    /// <item>Commit-on-charge: when <see cref="BoostChargeStarted"/> is true, the locked
    /// target cannot switch. Set from BallMovement when boost press fires.</item>
    /// </list>
    /// <b>No-lock behaviour.</b> When no lock is held, <see cref="HasLock"/> is false and
    /// <see cref="TrackingPosition"/> is undefined — BallMovement must check
    /// <see cref="HasLock"/> and fall back to its normal boost direction.
    /// </remarks>
    public sealed class LockOnAdapter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform aimSource;

        [Header("Tuning (Q12)")]
        [SerializeField] LockOnConfig config = new LockOnConfig();
        [SerializeField] string enemyTag = "Enemy";

        readonly LockOnCandidate[] _candidateBuffer = new LockOnCandidate[16];
        int _candidateCount;
        LockState _currentLock = LockState.None;
        LockOnResult _lastResult;
        bool _boostChargeStarted;

        /// <summary>True while the player is holding boost charge; commit-on-charge
        /// is enforced while this is true (set by BallMovement on boost press).</summary>
        public bool BoostChargeStarted
        {
            get => _boostChargeStarted;
            set => _boostChargeStarted = value;
        }

        /// <summary>Whether a lock is currently held this tick.</summary>
        public bool HasLock => _lastResult.NewLock.HasLock;

        /// <summary>The soft-tracked aim point for the current lock.</summary>
        public Vector3 TrackingPosition => _lastResult.NewLock.TrackingPosition;

        /// <summary>The locked enemy's world position (live, not tracked) — used for
        /// rendering the hover icon.</summary>
        public Vector3 LockedEnemyPosition { get; private set; }

        void Update()
        {
            if (aimSource == null) return;
            if (config == null) return;

            _candidateCount = GatherCandidates(_candidateBuffer);
            LockOnInput input = new LockOnInput(
                transform.position,
                aimSource.forward,
                _candidateBuffer,
                _currentLock,
                _boostChargeStarted);
            _lastResult = LockOnResolver.Resolve(in input, config);
            _currentLock = _lastResult.NewLock;

            if (_lastResult.NewLock.HasLock)
            {
                for (int i = 0; i < _candidateCount; i++)
                {
                    if (_candidateBuffer[i].Id == _lastResult.NewLock.TargetId)
                    {
                        LockedEnemyPosition = _candidateBuffer[i].Position;
                        break;
                    }
                }
            }
        }

        int GatherCandidates(LockOnCandidate[] buffer)
        {
            EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            int n = Mathf.Min(enemies.Length, buffer.Length);
            for (int i = 0; i < n; i++)
            {
                Rigidbody rb = enemies[i].GetComponent<Rigidbody>();
                buffer[i] = new LockOnCandidate(
                    enemies[i].GetInstanceID(),
                    enemies[i].transform.position,
                    rb != null ? rb.linearVelocity : Vector3.zero);
            }
            return n;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = HasLock ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 1.2f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (HasLock && _lastResult.IconShouldShow)
            {
                Vector3 enemyPos = LockedEnemyPosition;
                Vector3 trackingPos = TrackingPosition;
                Vector3 origin = transform.position + Vector3.up * 0.5f;

                Debug.DrawLine(origin, trackingPos, Color.green, dt);
                Debug.DrawLine(trackingPos, enemyPos, new Color(0.5f, 1f, 0.5f, 0.3f), dt);

                float crossSize = 0.4f;
                Debug.DrawLine(enemyPos - Vector3.right * crossSize, enemyPos + Vector3.right * crossSize, Color.green, dt);
                Debug.DrawLine(enemyPos - Vector3.up * crossSize, enemyPos + Vector3.up * crossSize, Color.green, dt);
                Debug.DrawLine(enemyPos - Vector3.forward * crossSize, enemyPos + Vector3.forward * crossSize, Color.green, dt);

                float dotSize = 0.15f;
                Debug.DrawLine(trackingPos - Vector3.right * dotSize, trackingPos + Vector3.right * dotSize, Color.yellow, dt);
                Debug.DrawLine(trackingPos - Vector3.up * dotSize, trackingPos + Vector3.up * dotSize, Color.yellow, dt);
                Debug.DrawLine(trackingPos - Vector3.forward * dotSize, trackingPos + Vector3.forward * dotSize, Color.yellow, dt);
            }
        }
    }
}
