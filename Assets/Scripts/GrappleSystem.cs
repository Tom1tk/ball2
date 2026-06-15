using UnityEngine;
using UnityEngine.InputSystem;

public class GrappleSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Rigidbody rb;
    [SerializeField] Camera playerCamera;
    [SerializeField] GameObject hookProjectilePrefab;
    [SerializeField] Transform leftIndicator;
    [SerializeField] Transform rightIndicator;
    [SerializeField] LineRenderer leftLine;
    [SerializeField] LineRenderer rightLine;
    [SerializeField] RectTransform leftCursor;
    [SerializeField] RectTransform rightCursor;

    [Header("Settings")]
    [SerializeField] float maxRange = 30f;
    [SerializeField] float projectileSpeed = 80f;
    [SerializeField] float returnSpeed = 80f;
    [SerializeField] float reelAcceleration = 30f;
    [SerializeField] float maxReelSpeed = 60f;
    [SerializeField] float minRopeLength = 1f;
    [SerializeField] LayerMask grappleMask = ~0;
    [SerializeField] float stuckAngleThreshold = 170f;
    [SerializeField] float indicatorOffset = 1.5f;
    [SerializeField] float baseCursorScale = 0.2f;

    private enum HookState { Ready, Firing, Hooked, Returning }

    private HookState leftState, rightState;

    private GameObject leftProjectile, rightProjectile;
    private Vector3 leftFireDir, rightFireDir;
    private Vector3 leftHookPoint, rightHookPoint;
    private Vector3 leftHookNormal, rightHookNormal;
    private float leftRopeLength, rightRopeLength;
    private float leftDistance, rightDistance;

    private bool isReeling;
    private float reelVelocity;

    private InputAction leftGrapple;
    private InputAction rightGrapple;
    private InputAction reel;

    void Awake()
    {
        leftGrapple = new InputAction("LeftGrapple", binding: "<Mouse>/leftButton");
        rightGrapple = new InputAction("RightGrapple", binding: "<Mouse>/rightButton");
        reel = new InputAction("Reel", binding: "<Keyboard>/leftShift");

        leftState = rightState = HookState.Ready;
    }

    void OnEnable()
    {
        leftGrapple.Enable();
        rightGrapple.Enable();
        reel.Enable();

        leftGrapple.started += _ => fireHook(true);
        leftGrapple.canceled += _ => releaseHook(true);
        rightGrapple.started += _ => fireHook(false);
        rightGrapple.canceled += _ => releaseHook(false);
        reel.started += _ => isReeling = true;
        reel.canceled += _ => isReeling = false;
    }

    void OnDisable()
    {
        leftGrapple.Disable();
        rightGrapple.Disable();
        reel.Disable();

        leftGrapple.started -= _ => fireHook(true);
        leftGrapple.canceled -= _ => releaseHook(true);
        rightGrapple.started -= _ => fireHook(false);
        rightGrapple.canceled -= _ => releaseHook(false);
        reel.started -= _ => isReeling = true;
        reel.canceled -= _ => isReeling = false;
    }

    void OnDestroy()
    {
        if (leftProjectile != null) Destroy(leftProjectile);
        if (rightProjectile != null) Destroy(rightProjectile);
        leftGrapple?.Dispose();
        rightGrapple?.Dispose();
        reel?.Dispose();
    }

    void FixedUpdate()
    {
        updateIndicatorPositions();
        processReel();
        processHook(ref leftState, ref leftProjectile, ref leftFireDir, ref leftHookPoint, ref leftHookNormal, ref leftRopeLength, ref leftDistance, leftIndicator, leftLine);
        processHook(ref rightState, ref rightProjectile, ref rightFireDir, ref rightHookPoint, ref rightHookNormal, ref rightRopeLength, ref rightDistance, rightIndicator, rightLine);
        checkStuckAngle();
        updateVisuals();
    }

    void updateIndicatorPositions()
    {
        Vector3 camRight = playerCamera.transform.right;
        Vector3 camForward = playerCamera.transform.forward;

        leftIndicator.position = transform.position - camRight * indicatorOffset;
        rightIndicator.position = transform.position + camRight * indicatorOffset;

        leftIndicator.rotation = Quaternion.LookRotation(camForward);
        rightIndicator.rotation = Quaternion.LookRotation(camForward);
    }

    void fireHook(bool isLeft)
    {
        ref HookState state = ref isLeft ? ref leftState : ref rightState;
        if (state != HookState.Ready) return;

        ref GameObject projectile = ref isLeft ? ref leftProjectile : ref rightProjectile;
        ref Vector3 fireDir = ref isLeft ? ref leftFireDir : ref rightFireDir;
        ref float distance = ref isLeft ? ref leftDistance : ref rightDistance;
        Transform indicator = isLeft ? leftIndicator : rightIndicator;

        fireDir = playerCamera.transform.forward;
        distance = 0f;
        projectile = Instantiate(hookProjectilePrefab, indicator.position, Quaternion.identity);
        projectile.transform.rotation = Quaternion.LookRotation(fireDir) * Quaternion.Euler(90f, 0f, 0f);
        state = HookState.Firing;
    }

    void releaseHook(bool isLeft)
    {
        ref HookState state = ref isLeft ? ref leftState : ref rightState;
        if (state == HookState.Firing || state == HookState.Hooked)
            state = HookState.Returning;
    }

    void processHook(ref HookState state, ref GameObject projectile, ref Vector3 fireDir, ref Vector3 hookPoint, ref Vector3 hookNormal, ref float ropeLength, ref float distance, Transform indicator, LineRenderer line)
    {
        switch (state)
        {
            case HookState.Firing:
                float step = projectileSpeed * Time.fixedDeltaTime;
                Vector3 prevPos = projectile.transform.position;

                projectile.transform.position += fireDir * step;
                distance += step;

                if (Physics.Linecast(prevPos, projectile.transform.position, out RaycastHit hit, grappleMask))
                {
                    hookPoint = hit.point;
                    hookNormal = hit.normal;
                    ropeLength = Vector3.Distance(indicator.position, hit.point);
                    projectile.transform.position = hit.point;
                    state = HookState.Hooked;
                }
                else if (distance >= maxRange)
                {
                    state = HookState.Returning;
                }
                break;

            case HookState.Hooked:
                applyRopeConstraint(hookPoint, ref ropeLength);
                break;

            case HookState.Returning:
                Vector3 returnTarget = indicator.position;
                Vector3 toTarget = returnTarget - projectile.transform.position;
                float returnStep = returnSpeed * Time.fixedDeltaTime;

                if (toTarget.magnitude <= returnStep)
                {
                    Destroy(projectile);
                    projectile = null;
                    state = HookState.Ready;
                }
                else
                {
                    projectile.transform.position += toTarget.normalized * returnStep;
                }
                break;
        }
    }

    void applyRopeConstraint(Vector3 hookPoint, ref float ropeLength)
    {
        Vector3 toHook = hookPoint - rb.position;
        float dist = toHook.magnitude;

        if (dist > ropeLength && dist > 0.001f)
        {
            Vector3 dir = toHook / dist;
            rb.position = hookPoint - dir * ropeLength;

            float velAlong = Vector3.Dot(rb.linearVelocity, dir);
            if (velAlong > 0f)
                rb.linearVelocity -= dir * velAlong;
        }
    }

    void processReel()
    {
        bool leftHooked = leftState == HookState.Hooked;
        bool rightHooked = rightState == HookState.Hooked;

        if (!isReeling || (!leftHooked && !rightHooked))
        {
            reelVelocity = Mathf.Max(reelVelocity - reelAcceleration * 2f * Time.fixedDeltaTime, 0f);
            return;
        }

        reelVelocity = Mathf.Min(reelVelocity + reelAcceleration * Time.fixedDeltaTime, maxReelSpeed);

        Vector3 reelDir = Vector3.zero;
        int hookedCount = 0;

        if (leftHooked)
        {
            reelDir += (leftHookPoint - rb.position).normalized;
            hookedCount++;
            leftRopeLength -= reelVelocity * Time.fixedDeltaTime;
            if (leftRopeLength <= minRopeLength)
                leftState = HookState.Returning;
        }
        if (rightHooked)
        {
            reelDir += (rightHookPoint - rb.position).normalized;
            hookedCount++;
            rightRopeLength -= reelVelocity * Time.fixedDeltaTime;
            if (rightRopeLength <= minRopeLength)
                rightState = HookState.Returning;
        }

        if (hookedCount > 0)
        {
            reelDir /= hookedCount;
            rb.AddForce(reelDir * reelVelocity * 5f, ForceMode.Acceleration);
        }
    }

    void checkStuckAngle()
    {
        if (leftState != HookState.Hooked || rightState != HookState.Hooked) return;

        Vector3 dirL = (leftHookPoint - rb.position).normalized;
        Vector3 dirR = (rightHookPoint - rb.position).normalized;

        if (Vector3.Angle(dirL, dirR) >= stuckAngleThreshold)
        {
            leftState = HookState.Returning;
            rightState = HookState.Returning;
        }
    }

    void updateVisuals()
    {
        if (leftProjectile != null)
        {
            leftLine.enabled = true;
            leftLine.SetPosition(0, leftIndicator.position);
            leftLine.SetPosition(1, leftState == HookState.Hooked ? leftHookPoint : leftProjectile.transform.position);
        }
        else
        {
            leftLine.enabled = false;
        }

        if (rightProjectile != null)
        {
            rightLine.enabled = true;
            rightLine.SetPosition(0, rightIndicator.position);
            rightLine.SetPosition(1, rightState == HookState.Hooked ? rightHookPoint : rightProjectile.transform.position);
        }
        else
        {
            rightLine.enabled = false;
        }

        updateCursorVisual(leftCursor, leftState);
        updateCursorVisual(rightCursor, rightState);
    }

    void updateCursorVisual(RectTransform cursor, HookState state)
    {
        float multiplier;
        switch (state)
        {
            case HookState.Ready:       multiplier = 1.0f; break;
            case HookState.Firing:      multiplier = 0.7f; break;
            case HookState.Hooked:      multiplier = 1.3f; break;
            case HookState.Returning:   multiplier = 0.5f; break;
            default:                    multiplier = 1.0f; break;
        }
        cursor.localScale = Vector3.one * baseCursorScale * multiplier;
    }
}
