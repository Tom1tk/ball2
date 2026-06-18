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
        [SerializeField] Camera aimCamera;

        [Header("Tuning (Q12)")]
        [SerializeField] LockOnConfig config = new LockOnConfig();
        [SerializeField] string enemyTag = "Enemy";

        [Header("Icon")]
        [SerializeField] bool showIcon = true;
        [SerializeField] float iconSize = 24f;
        [SerializeField] Color iconColor = new Color(1f, 0.85f, 0.2f, 0.9f);

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
            GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTag);
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

        void OnGUI()
        {
            if (!showIcon) return;
            if (!_lastResult.IconShouldShow) return;
            if (aimCamera == null) return;

            Vector3 screenPos = aimCamera.WorldToScreenPoint(LockedEnemyPosition);
            if (screenPos.z <= 0f) return;

            float y = Screen.height - screenPos.y;
            Rect r = new Rect(screenPos.x - iconSize * 0.5f, y - iconSize * 0.5f, iconSize, iconSize);
            Color prev = GUI.color;
            GUI.color = iconColor;
            GUI.Box(r, GUIContent.none);
            GUI.color = prev;
        }
    }
}
