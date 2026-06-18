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

        [Header("Lock Indicator")]
        [SerializeField] Sprite lockIconSprite;
        [SerializeField] Color lockIconColor = Color.green;
        [SerializeField] float lockIconSize = 1.2f;
        [SerializeField] float lockIconYOffset = 1.5f;

        readonly LockOnCandidate[] _candidateBuffer = new LockOnCandidate[16];
        int _candidateCount;
        LockState _currentLock = LockState.None;
        LockOnResult _lastResult;
        bool _boostChargeStarted;
        GameObject _lockIcon;
        SpriteRenderer _lockIconRenderer;

        void Start()
        {
            _lockIcon = new GameObject("LockIndicator");
            _lockIcon.hideFlags = HideFlags.HideAndDontSave;
            _lockIconRenderer = _lockIcon.AddComponent<SpriteRenderer>();
            _lockIconRenderer.sprite = lockIconSprite;
            _lockIconRenderer.color = lockIconColor;
            _lockIcon.AddComponent<SpriteFaceCamera>();
            _lockIcon.SetActive(false);
        }

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

        int _lastLoggedCount = -1;
        float _logTimer;
        bool _hadLockLastFrame;

        void Update()
        {
            if (aimSource == null) return;
            if (config == null) return;

            _candidateCount = GatherCandidates(_candidateBuffer);

            LockOnInput input = new LockOnInput(
                transform.position,
                aimSource.forward.normalized,
                _candidateBuffer,
                _candidateCount,
                _currentLock,
                _boostChargeStarted);
            _lastResult = LockOnResolver.Resolve(in input, config);
            _currentLock = _lastResult.NewLock;

            if (Time.frameCount % 30 == 0)
                Debug.Log($"[Resolve] HasLock={_lastResult.NewLock.HasLock} TargetId={_lastResult.NewLock.TargetId} Icon={_lastResult.IconShouldShow} TrackingPos={_lastResult.NewLock.TrackingPosition} InputCount={input.CandidateCount} BoostCharging={_boostChargeStarted} PrevHadLock={_hadLockLastFrame}");

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

            if (_lastResult.NewLock.HasLock != _hadLockLastFrame)
            {
                _hadLockLastFrame = _lastResult.NewLock.HasLock;
                if (_lastResult.NewLock.HasLock)
                    Debug.Log($"[LockOn] >>> ACQUIRED lock on enemy {_lastResult.NewLock.TargetId} at {LockedEnemyPosition}");
                else
                    Debug.Log("[LockOn] LOCK LOST");
            }

            _logTimer -= Time.deltaTime;
            if (_logTimer <= 0f)
            {
                _logTimer = 1f;
                string locked = HasLock ? " LOCKED" : "";
                if (_candidateCount > 0)
                {
                    Vector3 aim = aimSource.forward;
                    float d = Vector3.Distance(transform.position, _candidateBuffer[0].Position);
                    float a = Vector3.Angle(aim, _candidateBuffer[0].Position - transform.position);
                    Debug.Log($"[LockOn]{locked} C={_candidateCount} D={d:F1}u A={a:F1}deg Aim=({aim.x:F2},{aim.y:F2},{aim.z:F2}) R={config.Range} Acq={config.AcquisitionAngleDeg} Brk={config.BreakRange} Dodge={config.DodgeToleranceDeg}");
                }
                else Debug.LogWarning("[LockOn] 0 enemies via FindObjectsByType<EnemyAI>");
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
            for (int i = 0; i < _candidateCount; i++)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_candidateBuffer[i].Position, 0.5f);
            }
            Gizmos.color = HasLock ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 1.2f);
            if (aimSource != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(transform.position, aimSource.forward * 5f);
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            for (int i = 0; i < _candidateCount; i++)
            {
                Debug.DrawLine(origin, _candidateBuffer[i].Position, Color.white * 0.3f, dt);
            }
            if (HasLock && _lastResult.IconShouldShow)
            {
                Vector3 enemyPos = LockedEnemyPosition;
                Vector3 trackingPos = TrackingPosition;

                if (lockIconSprite != null)
                {
                    Transform t = _lockIcon.transform;
                    t.position = enemyPos + Vector3.up * lockIconYOffset;
                    t.localScale = Vector3.one * lockIconSize;
                    _lockIconRenderer.color = lockIconColor;
                    _lockIcon.SetActive(true);
                }

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
            else if (_lockIcon != null)
            {
                _lockIcon.SetActive(false);
            }
        }
    }
}
