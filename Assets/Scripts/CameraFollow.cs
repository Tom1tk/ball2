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
    [SerializeField] float minCameraDistance = 1f;
    [SerializeField] LayerMask blockMask = ~0;

    float yaw;
    float pitch;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
    }

    void LateUpdate()
    {
        if (Time.timeScale <= 0f) return;

        Vector2 mouseDelta = Mouse.current?.delta.ReadValue() ?? Vector2.zero;

        yaw += mouseDelta.x * sensitivity;
        pitch -= mouseDelta.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
        Vector3 lookTarget = target.position + Vector3.up * verticalOffset;

        Vector3 desiredPosition = target.position + offset;
        Vector3 toCamera = desiredPosition - target.position;
        float maxDist = toCamera.magnitude;
        Vector3 dirToCamera = toCamera / maxDist;

        if (Physics.SphereCast(target.position, collisionRadius, dirToCamera, out RaycastHit hit, maxDist, blockMask))
            desiredPosition = target.position + dirToCamera * Mathf.Max(hit.distance, minCameraDistance);

        transform.position = desiredPosition;
        transform.LookAt(lookTarget);
    }
}
