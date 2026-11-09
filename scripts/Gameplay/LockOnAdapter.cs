using Godot;
using Ball2.Core.Combat;
using NVector3 = System.Numerics.Vector3;

/// <summary>
/// Player-side lock-on adapter (B2-011). Gathers enemy candidates each physics tick,
/// calls the pure LockOnResolver, and exposes the result (lock state, icon target,
/// tracking position) to the rest of the gameplay layer.
/// </summary>
public partial class LockOnAdapter : Node3D
{
    [ExportGroup("Tuning (Q12)")]
    [Export] public float Range = 20f;
    [Export] public float AcquisitionAngleDeg = 60f;
    [Export] public float TrackingStrength = 0.35f;
    [Export] public float DodgeToleranceDeg = 45f;
    [Export] public float BreakRange = 30f;

    [ExportGroup("Lock Indicator")]
    [Export] public float EnemyRadius = 2f;
    [Export] public float IconSurfaceOffset = 0.1f;
    [Export] public float IconSize = 1.2f;

    public bool BoostChargeStarted { get; set; }
    public bool HasLock { get; private set; }
    public Vector3 TrackingPosition { get; private set; }
    public Vector3 LockedEnemyPosition { get; private set; }

    private RigidBody3D _body;
    private Camera3D _camera;
    private Sprite3D _lockIcon;
    private LockOnConfig _config;
    private LockState _currentLock = LockState.None;

    public override void _Ready()
    {
        _body = GetParent<RigidBody3D>();
        _camera = GetViewport().GetCamera3D();
        _lockIcon = GetNode<Sprite3D>("LockIcon");
        _lockIcon.Visible = false;

        _config = new LockOnConfig
        {
            Range = Range,
            AcquisitionAngleDeg = AcquisitionAngleDeg,
            TrackingStrength = TrackingStrength,
            DodgeToleranceDeg = DodgeToleranceDeg,
            BreakRange = BreakRange,
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_camera == null) _camera = GetViewport().GetCamera3D();
        if (_camera == null) return;

        Godot.Collections.Array<Node> enemies = GetTree().GetNodesInGroup("enemies");
        var buffer = new LockOnCandidate[enemies.Count];
        int count = 0;

        foreach (Node node in enemies)
        {
            if (node is not RigidBody3D enemy || enemy.IsQueuedForDeletion()) continue;
            int id = unchecked((int)enemy.GetInstanceId());
            buffer[count++] = new LockOnCandidate(id, ToNumerics(enemy.GlobalPosition),
                ToNumerics(enemy.LinearVelocity));
        }

        Vector3 aim = -_camera.GlobalTransform.Basis.Z;
        var input = new LockOnInput(ToNumerics(_body.GlobalPosition), ToNumerics(aim), buffer, count,
            _currentLock, BoostChargeStarted);
        LockOnResult result = LockOnResolver.Resolve(in input, _config);
        _currentLock = result.NewLock;

        HasLock = result.NewLock.HasLock;
        TrackingPosition = ToGodot(result.NewLock.TrackingPosition);

        if (HasLock)
        {
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].Id != result.NewLock.TargetId) continue;
                LockedEnemyPosition = ToGodot(buffer[i].Position);
                break;
            }
        }

        UpdateIcon(result);
    }

    private void UpdateIcon(LockOnResult result)
    {
        if (_lockIcon == null) return;

        if (!HasLock || !result.IconShouldShow)
        {
            _lockIcon.Visible = false;
            return;
        }

        Vector3 cameraPosition = _camera.GlobalPosition;
        Vector3 toCamera = (cameraPosition - LockedEnemyPosition);
        if (toCamera.LengthSquared() > 1e-6f)
            toCamera = toCamera.Normalized();

        _lockIcon.GlobalPosition = LockedEnemyPosition + toCamera * (EnemyRadius + IconSurfaceOffset);
        _lockIcon.Scale = Vector3.One * IconSize;
        _lockIcon.Visible = true;
    }

    private static NVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 ToGodot(NVector3 v) => new(v.X, v.Y, v.Z);
}
