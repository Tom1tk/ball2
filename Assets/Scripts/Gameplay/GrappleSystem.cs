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
    [SerializeField] float projectileSpeed = 120f;
    [SerializeField] float returnSpeed = 120f;
    [SerializeField] float reelAcceleration = 30f;
    [SerializeField] float maxReelSpeed = 60f;
    [SerializeField] float minRopeLength = 1f;
    [SerializeField] LayerMask grappleMask = ~0;
    [SerializeField] float ropeStiffness = 800f;
    [SerializeField] float ropeDamping = 15f;
    [SerializeField] float stuckAngleThreshold = 170f;
    [SerializeField] float indicatorOffset = 1.5f;
    [SerializeField] float baseCursorScale = 0.2f;
    [SerializeField] float swingAntiGravity = 0.3f;
    [SerializeField] float releaseBoostMultiplier = 1.4f;
    [SerializeField] float antiWallForce = 5f;
    [Header("Auto-Aim")]
    [SerializeField] float manualAimRadius = 0.08f;
    [SerializeField] LayerMask autoAimMask = ~0;
    [SerializeField] bool showManualAimGizmo = true;

    public bool anyHooked { get; private set; }

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

    private Vector2 leftCursorDefaultPos, rightCursorDefaultPos;
    private Vector3 leftAimDir, rightAimDir;
    private Vector3 _debugLeftTarget, _debugRightTarget;
    private Texture2D _circleTex;

    void Awake()
    {
        leftGrapple = new InputAction("LeftGrapple", binding: "<Mouse>/leftButton");
        rightGrapple = new InputAction("RightGrapple", binding: "<Mouse>/rightButton");
        reel = new InputAction("Reel", binding: "<Keyboard>/leftShift");

        leftState = rightState = HookState.Ready;

        autoAimMask &= ~(1 << gameObject.layer);
        leftCursorDefaultPos = leftCursor.anchoredPosition;
        rightCursorDefaultPos = rightCursor.anchoredPosition;
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
        if (_circleTex != null) Destroy(_circleTex);
    }

    void Update()
    {
        updateCursorTargeting();
    }

    void FixedUpdate()
    {
        anyHooked = leftState == HookState.Hooked || rightState == HookState.Hooked;

        updateIndicatorPositions();
        processReel();
        processHook(ref leftState, ref leftProjectile, ref leftFireDir, ref leftHookPoint, ref leftHookNormal, ref leftRopeLength, ref leftDistance, leftIndicator, leftLine);
        processHook(ref rightState, ref rightProjectile, ref rightFireDir, ref rightHookPoint, ref rightHookNormal, ref rightRopeLength, ref rightDistance, rightIndicator, rightLine);
        checkStuckAngle();
        updateVisuals();

        if (anyHooked)
            rb.AddForce(Vector3.up * Physics.gravity.magnitude * swingAntiGravity, ForceMode.Acceleration);
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

        fireDir = (isLeft ? leftAimDir : rightAimDir);
        if (fireDir == Vector3.zero) fireDir = playerCamera.transform.forward;
        distance = 0f;
        projectile = Instantiate(hookProjectilePrefab, indicator.position, Quaternion.identity);
        projectile.transform.rotation = Quaternion.LookRotation(fireDir) * Quaternion.Euler(90f, 0f, 0f);
        state = HookState.Firing;
    }

    void releaseHook(bool isLeft)
    {
        ref HookState state = ref isLeft ? ref leftState : ref rightState;
        if (state == HookState.Firing || state == HookState.Hooked)
        {
            if (state == HookState.Hooked)
                rb.linearVelocity *= releaseBoostMultiplier;
            state = HookState.Returning;
        }
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
                rb.AddForce(hookNormal * antiWallForce, ForceMode.Acceleration);
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

        if (dist < 0.001f) return;
        Vector3 dir = toHook / dist;

        if (dist > ropeLength)
        {
            float overshoot = dist - ropeLength;
            float velAlong = Vector3.Dot(rb.linearVelocity, dir);
            float force = ropeStiffness * overshoot - ropeDamping * velAlong;
            rb.AddForce(dir * force, ForceMode.Acceleration);
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
            case HookState.Ready:       
                multiplier = 1.0f; 
                break;
            case HookState.Firing:      
                multiplier = 0.7f; 
                break;
            case HookState.Hooked:      
                multiplier = 1.3f; 
                break;
            case HookState.Returning:   
                multiplier = 0.5f; 
                break;
            default:                    
                multiplier = 1.0f;
                break;
        }
        cursor.localScale = Vector3.one * baseCursorScale * multiplier;
    }

    Vector3 findAutoAimTarget(bool leftSide)
    {
        Vector3 bestTarget = Vector3.zero;
        float bestScore = 0f;
        Vector3 idealDir = (playerCamera.transform.forward + Vector3.up).normalized;
        const int ySteps = 8, xSteps = 5;

        for (int yi = 0; yi < ySteps; yi++)
        {
            float vpY = (yi + 0.5f) / ySteps;

            for (int xi = 0; xi < xSteps; xi++)
            {
                float vpX = (xi + 0.5f) / (xSteps * 2f); // 0 to 0.5
                if (!leftSide) vpX = 1f - vpX;           // mirror to 1.0 to 0.5

                Ray ray = playerCamera.ViewportPointToRay(new Vector3(vpX, vpY, 0));
                if (!Physics.Raycast(ray, out RaycastHit hit, maxRange, autoAimMask))
                    continue;
                if (hit.transform.IsChildOf(transform) || hit.transform == transform)
                    continue;

                Vector3 point = hit.point;
                if (point.y <= transform.position.y + 0.5f)
                    continue;

                point += hit.normal * 0.3f;

                Vector3 toPoint = point - transform.position;
                if (toPoint.sqrMagnitude > maxRange * maxRange)
                    continue;

                float score = Vector3.Dot(toPoint, idealDir);
                if (vpY > 0.66f) score *= 2f; // top-third bonus

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = point;
                }
            }
        }

        return bestTarget;
    }

    void updateCursorTargeting()
    {
        Vector2 mousePos = Mouse.current?.position.ReadValue() ?? Vector2.zero;
        Vector2 mouseViewport = new Vector2(mousePos.x / Screen.width, mousePos.y / Screen.height);

        _debugLeftTarget = findAutoAimTarget(true);
        _debugRightTarget = findAutoAimTarget(false);

        updateCursorTarget(leftCursor, leftState, leftIndicator, leftCursorDefaultPos, true, mousePos, mouseViewport, ref leftAimDir, _debugLeftTarget);
        updateCursorTarget(rightCursor, rightState, rightIndicator, rightCursorDefaultPos, false, mousePos, mouseViewport, ref rightAimDir, _debugRightTarget);
    }

    void updateCursorTarget(RectTransform cursor, HookState state, Transform indicator, Vector2 defaultPos, bool isLeft, Vector2 mousePos, Vector2 mouseViewport, ref Vector3 aimDir, Vector3 autoTarget)
    {
        if (state != HookState.Ready)
        {
            cursor.anchoredPosition = defaultPos;
            return;
        }

        // Manual ring: raycast through mouse, check screen-space distance
        Ray mouseRay = playerCamera.ScreenPointToRay(mousePos);
        if (Physics.Raycast(mouseRay, out RaycastHit mouseHit, maxRange, grappleMask))
        {
            Vector3 hitViewport = playerCamera.WorldToViewportPoint(mouseHit.point);
            float screenDist = Vector2.Distance(new Vector2(hitViewport.x, hitViewport.y), mouseViewport);
            if (screenDist < manualAimRadius)
            {
                aimDir = (mouseHit.point - indicator.position).normalized;
                positionCursorAt(cursor, mouseHit.point);
                return;
            }
        }

        // Auto-aim
        if (autoTarget != Vector3.zero)
        {
            aimDir = (autoTarget - indicator.position).normalized;
            positionCursorAt(cursor, autoTarget);
            return;
        }

        // Default: camera-forward raycast from indicator
        aimDir = playerCamera.transform.forward;
        if (Physics.Raycast(indicator.position, aimDir, out RaycastHit hit, maxRange, grappleMask))
            positionCursorAt(cursor, hit.point);
        else
            cursor.anchoredPosition = defaultPos;
    }

    void positionCursorAt(RectTransform cursor, Vector3 worldPoint)
    {
        Vector2 screenPoint = playerCamera.WorldToScreenPoint(worldPoint);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)cursor.parent, screenPoint, null, out Vector2 localPoint);
        cursor.anchoredPosition = localPoint;
    }

    void OnDrawGizmos()
    {
        if (!showManualAimGizmo) return;
        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null) return;

        Gizmos.color = Color.yellow;
        float fovFactor = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float aspect = Screen.width > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        Vector3 center = cam.transform.position + cam.transform.forward * (maxRange * 0.5f);
        float worldRadius = manualAimRadius * maxRange * 0.5f * fovFactor * aspect;
        Gizmos.DrawWireSphere(center, worldRadius);
    }

    void OnGUI()
    {
        if (!showManualAimGizmo || playerCamera == null) return;

        if (_circleTex == null)
        {
            int s = 128;
            _circleTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            Color[] p = new Color[s * s];
            float c = s / 2f, r = s / 2f - 2f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                p[y * s + x] = new Color(1, 1, 0, 1f - Mathf.Clamp01(Mathf.Abs(d - r) / 2f));
            }
            _circleTex.Apply();
        }

        Event e = Event.current;
        Vector2 mp = e.mousePosition;
        float radius = manualAimRadius * Screen.height;
        Rect ringRect = new Rect(mp.x - radius, mp.y - radius, radius * 2f, radius * 2f);
        GUI.color = new Color(1, 1, 0, 0.3f);
        GUI.DrawTexture(ringRect, _circleTex);
        GUI.color = Color.white;

        // Debug: draw auto-aim target dots
        DrawDebugDot(_debugLeftTarget, Color.cyan);
        DrawDebugDot(_debugRightTarget, Color.magenta);
    }

    void DrawDebugDot(Vector3 worldPoint, Color color)
    {
        if (worldPoint == Vector3.zero || playerCamera == null) return;
        Vector3 vp = playerCamera.WorldToViewportPoint(worldPoint);
        if (vp.z < 0f) return;
        Vector2 sp = new Vector2(vp.x * Screen.width, (1f - vp.y) * Screen.height);
        GUI.color = color;
        GUI.DrawTexture(new Rect(sp.x - 4f, sp.y - 4f, 8f, 8f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
