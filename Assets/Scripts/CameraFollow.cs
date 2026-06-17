using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] float distance = 10f;
    [SerializeField] float sensitivity = 0.1f;
    [SerializeField] float verticalOffset = 1.5f;
    [SerializeField] float minPitch = -30f;
    [SerializeField] float maxPitch = 60f;
    [SerializeField] float collisionRadius = 0.3f;
    [SerializeField] float minDistance = 1.5f;
    [SerializeField] float collisionSmoothTime = 0.1f;
    [SerializeField] float lazySpeed = 8f;
    [SerializeField] float reactiveSpeed = 25f;
    [SerializeField] LayerMask blockMask = ~0;

    float yaw;
    float pitch;
    Vector3 dampVelocity;
    float currentDistance;
    float distanceVelocity;
    Rigidbody targetRb;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;

        currentDistance = distance;

        if (target != null)
        {
            blockMask &= ~(1 << target.gameObject.layer);
            targetRb = target.GetComponent<Rigidbody>();
        }
    }

    void LateUpdate()
    {
        if (Time.timeScale <= 0f) return;

        Vector2 mouseDelta = Mouse.current?.delta.ReadValue() ?? Vector2.zero;

        yaw += mouseDelta.x * sensitivity;
        pitch -= mouseDelta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 lookTarget = target.position + Vector3.up * verticalOffset;
        Vector3 dirToCamera = rotation * Vector3.back;

        float targetDist = distance;
        if (Physics.SphereCast(lookTarget, collisionRadius, dirToCamera, out RaycastHit hit, distance, blockMask))
            targetDist = Mathf.Clamp(hit.distance, minDistance, distance);

        currentDistance = Mathf.SmoothDamp(currentDistance, targetDist, ref distanceVelocity, collisionSmoothTime);

        Vector3 desiredPosition = lookTarget + dirToCamera * currentDistance;

        float speed = reactiveSpeed;
        if (targetRb != null && targetRb.linearVelocity.sqrMagnitude > 0.01f)
        {
            Vector3 camToTarget = (target.position - transform.position).normalized;
            float dot = Vector3.Dot(camToTarget, targetRb.linearVelocity.normalized);
            speed = Mathf.Lerp(reactiveSpeed, lazySpeed, Mathf.Clamp01(dot));
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref dampVelocity, 1f / speed);
        transform.rotation = rotation;
    }
}
