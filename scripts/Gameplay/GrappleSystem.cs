using Godot;

/// <summary>
/// Dual independent grapple hooks with a spring/damper rope, reel, auto-aim and
/// per-hook cursor targeting. Port of the Unity GrappleSystem MonoBehaviour.
/// State machine: Ready -> Firing -> Hooked -> Returning -> Ready.
/// </summary>
public partial class GrappleSystem : Node3D
{
    public enum HookState { Ready, Firing, Hooked, Returning }

    private sealed class HookRuntime
    {
        public HookState State = HookState.Ready;
        public Node3D Projectile;
        public Vector3 FireDir;
        public Vector3 HookPoint;
        public Vector3 HookNormal;
        public float RopeLength;
        public float Distance;
        public Node3D Indicator;
        public MeshInstance3D Line;
        public Vector2 DefaultCursorPos;
        public Vector3 AimDir;
        public Vector3 AutoTarget;
    }

    [ExportGroup("Settings")]
    [Export] public float MaxRange = 80f;
    [Export] public float ProjectileSpeed = 300f;
    [Export] public float ReturnSpeed = 300f;
    [Export] public float ReelAcceleration = 30f;
    [Export] public float MaxReelSpeed = 60f;
    [Export] public float MinRopeLength = 1f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GrappleMask = 0xFFFFFFFF;
    [Export] public float RopeStiffness = 800f;
    [Export] public float RopeDamping = 15f;
    [Export] public float StuckAngleThreshold = 170f;
    [Export] public float IndicatorOffset = 1.5f;
    [Export] public float BaseCursorScale = 0.2f;
    [Export] public float SwingAntiGravity = 0.3f;
    [Export] public float ReleaseBoostMultiplier = 1.2f;
    [Export] public float AntiWallForce = 5f;

    [ExportGroup("Auto-Aim")]
    [Export] public float ManualAimRadius = 0.08f;
    [Export(PropertyHint.Layers3DPhysics)] public uint AutoAimMask = 0xFFFFFFFF;
    [Export] public float TopThirdBonusY = 0.66f;

    public bool AnyHooked { get; private set; }

    private RigidBody3D _body;
    private Camera3D _camera;
    private UIScript _ui;
    private PackedScene _hookScene;
    private float _gravity;

    private readonly HookRuntime _left = new();
    private readonly HookRuntime _right = new();

    private bool _isReeling;
    private float _reelVelocity;
    private float _aimScanTimer;
    private Vector2 _virtualMouse;
    private readonly Godot.Collections.Array<Rid> _selfExclude = new();

    public override void _Ready()
    {
        _body = GetParent<RigidBody3D>();
        _camera = GetViewport().GetCamera3D();
        _ui = GetTree().GetFirstNodeInGroup("ui") as UIScript;
        _hookScene = GD.Load<PackedScene>("res://scenes/hook_projectile.tscn");
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
        _selfExclude.Add(_body.GetRid());

        _left.Indicator = GetNode<Node3D>("LeftIndicator");
        _right.Indicator = GetNode<Node3D>("RightIndicator");
        _left.Line = CreateRopeLine();
        _right.Line = CreateRopeLine();

        Vector2 size = GetViewport().GetVisibleRect().Size;
        _left.DefaultCursorPos = new Vector2(size.X * 0.5f - 64f, size.Y * 0.72f);
        _right.DefaultCursorPos = new Vector2(size.X * 0.5f + 64f, size.Y * 0.72f);
        _virtualMouse = size * 0.5f;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion) return;
        if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

        Vector2 size = GetViewport().GetVisibleRect().Size;
        _virtualMouse += motion.Relative;
        _virtualMouse = new Vector2(
            Mathf.Clamp(_virtualMouse.X, 0f, size.X),
            Mathf.Clamp(_virtualMouse.Y, 0f, size.Y));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_camera == null) _camera = GetViewport().GetCamera3D();
        if (_camera == null) return;
        if (_ui == null) _ui = GetTree().GetFirstNodeInGroup("ui") as UIScript;

        float dt = (float)delta;

        AnyHooked = _left.State == HookState.Hooked || _right.State == HookState.Hooked;

        // Auto-aim sweeps 40 raycasts per hook, so refresh the cached target a few
        // times per second instead of on every physics tick.
        _aimScanTimer -= dt;
        bool scanAim = _aimScanTimer <= 0f;
        if (scanAim) _aimScanTimer = 0.12f;

        HandleInput();
        UpdateIndicatorPositions();
        ProcessReel(dt);
        ProcessHook(_left, dt);
        ProcessHook(_right, dt);
        CheckStuckAngle();
        UpdateCursorTargeting(_left, true, scanAim);
        UpdateCursorTargeting(_right, false, scanAim);
        UpdateVisuals(_left);
        UpdateVisuals(_right);

        if (AnyHooked)
            _body.ApplyCentralForce(Vector3.Up * _gravity * SwingAntiGravity * _body.Mass);
    }

    private void HandleInput()
    {
        if (Input.IsActionJustPressed("grapple_left")) FireHook(_left);
        if (Input.IsActionJustReleased("grapple_left")) ReleaseHook(_left);
        if (Input.IsActionJustPressed("grapple_right")) FireHook(_right);
        if (Input.IsActionJustReleased("grapple_right")) ReleaseHook(_right);
        _isReeling = Input.IsActionPressed("reel");
    }

    private MeshInstance3D CreateRopeLine()
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.95f, 0.95f, 1f),
        };
        var line = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            TopLevel = true,
        };
        AddChild(line);
        return line;
    }

    private void UpdateIndicatorPositions()
    {
        Vector3 camRight = _camera.GlobalTransform.Basis.X;
        Vector3 camForward = -_camera.GlobalTransform.Basis.Z;

        _left.Indicator.GlobalPosition = _body.GlobalPosition - camRight * IndicatorOffset;
        _right.Indicator.GlobalPosition = _body.GlobalPosition + camRight * IndicatorOffset;
        FaceDirection(_left.Indicator, camForward);
        FaceDirection(_right.Indicator, camForward);
    }

    private void FireHook(HookRuntime hook)
    {
        if (hook.State != HookState.Ready || _hookScene == null) return;

        hook.FireDir = hook.AimDir;
        if (hook.FireDir.LengthSquared() < 1e-6f)
            hook.FireDir = -_camera.GlobalTransform.Basis.Z;
        hook.Distance = 0f;

        hook.Projectile = _hookScene.Instantiate<Node3D>();
        GetTree().CurrentScene.AddChild(hook.Projectile);
        hook.Projectile.GlobalPosition = hook.Indicator.GlobalPosition;
        FaceDirection(hook.Projectile, hook.FireDir);
        hook.State = HookState.Firing;
        AudioManager.Instance?.Play2D("grapple_fire", -12f);
    }

    private void ReleaseHook(HookRuntime hook)
    {
        if (hook.State is HookState.Firing or HookState.Hooked)
        {
            if (hook.State == HookState.Hooked)
                _body.LinearVelocity *= ReleaseBoostMultiplier;
            hook.State = HookState.Returning;
        }
    }

    private void ProcessHook(HookRuntime hook, float dt)
    {
        switch (hook.State)
        {
            case HookState.Firing:
            {
                float step = ProjectileSpeed * dt;
                Vector3 previous = hook.Projectile.GlobalPosition;
                Vector3 next = previous + hook.FireDir * step;
                var hit = Raycast(previous, next, GrappleMask);

                if (hit.Count > 0)
                {
                    hook.HookPoint = hit["position"].AsVector3();
                    hook.HookNormal = hit["normal"].AsVector3();
                    hook.RopeLength = hook.Indicator.GlobalPosition.DistanceTo(hook.HookPoint);
                    hook.Projectile.GlobalPosition = hook.HookPoint;
                    hook.State = HookState.Hooked;
                    AudioManager.Instance?.Play3D("grapple_hook", hook.HookPoint, -8f);
                }
                else
                {
                    hook.Projectile.GlobalPosition = next;
                    hook.Distance += step;
                    if (hook.Distance >= MaxRange)
                        hook.State = HookState.Returning;
                }
                break;
            }

            case HookState.Hooked:
                ApplyRopeConstraint(hook);
                _body.ApplyCentralForce(hook.HookNormal * AntiWallForce * _body.Mass);
                break;

            case HookState.Returning:
            {
                if (hook.Projectile == null)
                {
                    hook.State = HookState.Ready;
                    break;
                }

                Vector3 toTarget = hook.Indicator.GlobalPosition - hook.Projectile.GlobalPosition;
                float returnStep = ReturnSpeed * dt;
                if (toTarget.Length() <= returnStep)
                {
                    hook.Projectile.QueueFree();
                    hook.Projectile = null;
                    hook.State = HookState.Ready;
                }
                else
                {
                    hook.Projectile.GlobalPosition += toTarget.Normalized() * returnStep;
                }
                break;
            }
        }
    }

    private void ApplyRopeConstraint(HookRuntime hook)
    {
        Vector3 toHook = hook.HookPoint - _body.GlobalPosition;
        float distance = toHook.Length();
        if (distance < 0.001f) return;

        Vector3 direction = toHook / distance;
        if (distance <= hook.RopeLength) return;

        float overshoot = distance - hook.RopeLength;
        float velocityAlong = _body.LinearVelocity.Dot(direction);
        float force = RopeStiffness * overshoot - RopeDamping * velocityAlong;
        _body.ApplyCentralForce(direction * force * _body.Mass);
    }

    private void ProcessReel(float dt)
    {
        bool leftHooked = _left.State == HookState.Hooked;
        bool rightHooked = _right.State == HookState.Hooked;

        if (!_isReeling || (!leftHooked && !rightHooked))
        {
            _reelVelocity = Mathf.Max(_reelVelocity - ReelAcceleration * 2f * dt, 0f);
            return;
        }

        _reelVelocity = Mathf.Min(_reelVelocity + ReelAcceleration * dt, MaxReelSpeed);

        Vector3 reelDirection = Vector3.Zero;
        int hookedCount = 0;

        if (leftHooked)
        {
            reelDirection += (_left.HookPoint - _body.GlobalPosition).Normalized();
            hookedCount++;
            _left.RopeLength -= _reelVelocity * dt;
            if (_left.RopeLength <= MinRopeLength) _left.State = HookState.Returning;
        }

        if (rightHooked)
        {
            reelDirection += (_right.HookPoint - _body.GlobalPosition).Normalized();
            hookedCount++;
            _right.RopeLength -= _reelVelocity * dt;
            if (_right.RopeLength <= MinRopeLength) _right.State = HookState.Returning;
        }

        if (hookedCount > 0)
        {
            reelDirection /= hookedCount;
            _body.ApplyCentralForce(reelDirection * _reelVelocity * 5f * _body.Mass);
        }
    }

    private void CheckStuckAngle()
    {
        if (_left.State != HookState.Hooked || _right.State != HookState.Hooked) return;

        Vector3 dirLeft = (_left.HookPoint - _body.GlobalPosition).Normalized();
        Vector3 dirRight = (_right.HookPoint - _body.GlobalPosition).Normalized();
        if (Mathf.RadToDeg(dirLeft.AngleTo(dirRight)) >= StuckAngleThreshold)
        {
            _left.State = HookState.Returning;
            _right.State = HookState.Returning;
        }
    }

    private void UpdateCursorTargeting(HookRuntime hook, bool isLeft, bool scanAim)
    {
        if (_ui == null) return;

        if (hook.State != HookState.Ready)
        {
            _ui.SetCrosshair(isLeft, hook.DefaultCursorPos, true, StateScale(hook.State));
            return;
        }

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2 mouseViewport = new(_virtualMouse.X / viewportSize.X, _virtualMouse.Y / viewportSize.Y);

        if (scanAim)
            hook.AutoTarget = FindAutoAimTarget(isLeft);

        Vector3 aimDir = Vector3.Zero;
        Vector3 worldTarget = Vector3.Zero;
        bool hasWorldTarget = false;

        // Manual ring: raycast through the virtual cursor, compare in screen space.
        Vector3 mouseOrigin = _camera.ProjectRayOrigin(mouseViewport);
        Vector3 mouseDirection = _camera.ProjectRayNormal(mouseViewport);
        var mouseHit = Raycast(mouseOrigin, mouseOrigin + mouseDirection * MaxRange, GrappleMask);
        if (mouseHit.Count > 0)
        {
            Vector3 point = mouseHit["position"].AsVector3();
            Vector2 hitScreen = _camera.UnprojectPosition(point);
            Vector2 hitViewport = new(hitScreen.X / viewportSize.X, hitScreen.Y / viewportSize.Y);
            if (hitViewport.DistanceTo(mouseViewport) < ManualAimRadius)
            {
                aimDir = (point - hook.Indicator.GlobalPosition).Normalized();
                worldTarget = point;
                hasWorldTarget = true;
            }
        }

        // Auto-aim.
        if (!hasWorldTarget && hook.AutoTarget != Vector3.Zero)
        {
            aimDir = (hook.AutoTarget - hook.Indicator.GlobalPosition).Normalized();
            worldTarget = hook.AutoTarget;
            hasWorldTarget = true;
        }

        // Default: camera-forward raycast from the indicator.
        if (!hasWorldTarget)
        {
            aimDir = -_camera.GlobalTransform.Basis.Z;
            var hit = Raycast(hook.Indicator.GlobalPosition,
                hook.Indicator.GlobalPosition + aimDir * MaxRange, GrappleMask);
            if (hit.Count > 0)
            {
                worldTarget = hit["position"].AsVector3();
                hasWorldTarget = true;
            }
        }

        hook.AimDir = aimDir;

        Vector2 screenPos = hasWorldTarget ? _camera.UnprojectPosition(worldTarget) : hook.DefaultCursorPos;
        _ui.SetCrosshair(isLeft, screenPos, true, 1f);
    }

    private Vector3 FindAutoAimTarget(bool leftSide)
    {
        Vector3 bestTarget = Vector3.Zero;
        float bestScore = 0f;
        Vector3 idealDirection = (-_camera.GlobalTransform.Basis.Z + Vector3.Up).Normalized();

        const int ySteps = 8;
        const int xSteps = 5;

        for (int yi = 0; yi < ySteps; yi++)
        {
            float viewportY = (yi + 0.5f) / ySteps;

            for (int xi = 0; xi < xSteps; xi++)
            {
                float viewportX = (xi + 0.5f) / (xSteps * 2f);
                if (!leftSide) viewportX = 1f - viewportX;

                Vector3 origin = _camera.ProjectRayOrigin(new Vector2(viewportX, viewportY));
                Vector3 direction = _camera.ProjectRayNormal(new Vector2(viewportX, viewportY));
                var hit = Raycast(origin, origin + direction * MaxRange, AutoAimMask);
                if (hit.Count == 0) continue;

                GodotObject collider = hit["collider"].AsGodotObject();
                if (collider is Node3D hitNode && (hitNode == _body || hitNode.IsAncestorOf(_body)))
                    continue;

                Vector3 point = hit["position"].AsVector3();
                if (point.Y <= _body.GlobalPosition.Y + 0.5f) continue;

                point += hit["normal"].AsVector3() * 0.3f;
                Vector3 toPoint = point - _body.GlobalPosition;
                if (toPoint.LengthSquared() > MaxRange * MaxRange) continue;

                float score = toPoint.Dot(idealDirection);
                if (viewportY > TopThirdBonusY) score *= 2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = point;
                }
            }
        }

        return bestTarget;
    }

    private void UpdateVisuals(HookRuntime hook)
    {
        var mesh = (ImmediateMesh)hook.Line.Mesh;
        mesh.ClearSurfaces();

        if (hook.Projectile == null) return;

        Vector3 end = hook.State == HookState.Hooked ? hook.HookPoint : hook.Projectile.GlobalPosition;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        mesh.SurfaceAddVertex(hook.Indicator.GlobalPosition);
        mesh.SurfaceAddVertex(end);
        mesh.SurfaceEnd();
    }

    private Godot.Collections.Dictionary Raycast(Vector3 from, Vector3 to, uint mask)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask, _selfExclude);
        return GetWorld3D().DirectSpaceState.IntersectRay(query);
    }

    private static float StateScale(HookState state) => state switch
    {
        HookState.Ready => 1.0f,
        HookState.Firing => 0.7f,
        HookState.Hooked => 1.3f,
        HookState.Returning => 0.5f,
        _ => 1.0f,
    };

    private static void FaceDirection(Node3D node, Vector3 direction)
    {
        Vector3 forward = direction.Normalized();
        Vector3 up = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
        node.LookAt(node.GlobalPosition + forward, up);
    }
}
