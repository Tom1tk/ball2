using Godot;

/// <summary>
/// Third-person orbit camera with collision pull-in and lazy/reactive follow.
/// Port of the Unity CameraFollow MonoBehaviour.
/// </summary>
public partial class CameraFollow : Camera3D
{
    [Export] public NodePath TargetPath;
    [Export] public float Distance = 10f;
    [Export] public float Sensitivity = 0.12f;
    [Export] public float VerticalOffset = 1.5f;
    [Export] public float MinPitch = -60f;
    [Export] public float MaxPitch = 60f;
    [Export] public float CollisionRadius = 0.3f;
    [Export] public float MinDistance = 1.5f;
    [Export] public float LazySpeed = 8f;
    [Export] public float ReactiveSpeed = 25f;
    [Export(PropertyHint.Layers3DPhysics)] public uint BlockMask = 1;

    private Node3D _target;
    private RigidBody3D _targetBody;
    private float _yaw;
    private float _pitch;
    private float _currentDistance;
    private Vector3 _positionVelocity;
    private float _distanceVelocity;

    public override void _Ready()
    {
        _target = GetNodeOrNull<Node3D>(TargetPath);
        _targetBody = _target as RigidBody3D;

        _yaw = RotationDegrees.Y;
        _pitch = RotationDegrees.X;
        _currentDistance = Distance;
        Current = true;

        if (_target != null)
            GlobalPosition = ComputeDesiredPosition();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * Sensitivity;
            _pitch -= motion.Relative.Y * Sensitivity;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
        }
    }

    public override void _Process(double delta)
    {
        if (_target == null || GetTree().Paused) return;

        float dt = (float)delta;
        Vector3 lookTarget = _target.GlobalPosition + Vector3.Up * VerticalOffset;
        Basis basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0f));
        Vector3 dirToCamera = basis * Vector3.Back;

        float targetDist = Distance;
        var space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid>();
        if (_targetBody != null) exclude.Add(_targetBody.GetRid());
        var query = PhysicsRayQueryParameters3D.Create(lookTarget, lookTarget + dirToCamera * Distance,
            BlockMask, exclude);
        var hit = space.IntersectRay(query);
        if (hit.Count > 0)
        {
            Vector3 hitPoint = (Vector3)hit["position"];
            targetDist = Mathf.Clamp(lookTarget.DistanceTo(hitPoint), MinDistance, Distance);
        }

        _currentDistance = SmoothDamp(_currentDistance, targetDist, ref _distanceVelocity, 0.1f, dt);

        Vector3 desiredPosition = lookTarget + dirToCamera * _currentDistance;

        float speed = ReactiveSpeed;
        if (_targetBody != null && _targetBody.LinearVelocity.LengthSquared() > 0.01f)
        {
            Vector3 camToTarget = (_target.GlobalPosition - GlobalPosition).Normalized();
            float dot = camToTarget.Dot(_targetBody.LinearVelocity.Normalized());
            speed = Mathf.Lerp(ReactiveSpeed, LazySpeed, Mathf.Clamp(dot, 0f, 1f));
        }

        GlobalPosition = SmoothDamp(GlobalPosition, desiredPosition, ref _positionVelocity, 1f / speed, dt);
        GlobalRotation = new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0f);
    }

    private Vector3 ComputeDesiredPosition()
    {
        Basis basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0f));
        return _target.GlobalPosition + Vector3.Up * VerticalOffset + basis * Vector3.Back * Distance;
    }

    private static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime, float dt)
    {
        smoothTime = Mathf.Max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        Vector3 change = current - target;
        Vector3 temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        return target + (change + temp) * exp;
    }

    private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
    {
        smoothTime = Mathf.Max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - target;
        float temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        return target + (change + temp) * exp;
    }
}
