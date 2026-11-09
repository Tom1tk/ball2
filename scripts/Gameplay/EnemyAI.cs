using Godot;

/// <summary>
/// Enemy ball AI: detect, chase, wander and charge-boost at the player.
/// The Unity original used a NavMeshAgent for path corners; this port uses direct
/// steering against an open arena (see MIGRATION_PLAN.md for the NavigationAgent3D path).
/// </summary>
public partial class EnemyAI : RigidBody3D
{
    [Export] public bool AiEnabled = true;
    [Export] public float MaxVelocity = 15f;
    [Export] public float InputForce = 100f;
    [Export] public float DetectionRange = 15f;
    [Export] public float BoostAttackRange = 7f;
    [Export] public float BoostChargeTime = 1.5f;
    [Export] public float BoostCooldown = 2f;
    [Export] public float BoostForce = 50f;
    [Export] public float WanderRadius = 40f;
    [Export] public float WanderIntervalMin = 1f;
    [Export] public float WanderIntervalMax = 5f;

    public bool CanSeePlayer { get; private set; }
    public bool CanAttackPlayer { get; private set; }
    public bool Charging { get; private set; }

    private Node3D _player;
    private CombatAdapter _combat;
    private CpuParticles3D _chargeFx;
    private float _chargeTimer;
    private float _cooldownTimer;
    private float _wanderTimer;
    private Vector3 _wanderPoint;
    private bool _hasWanderPoint;
    private float _gravity;
    private readonly Godot.Collections.Array<Rid> _selfExclude = new();
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        AddToGroup("enemies");
        _combat = GetNode<CombatAdapter>("CombatAdapter");
        _chargeFx = GetNodeOrNull<CpuParticles3D>("ChargeFX");
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
        _selfExclude.Add(GetRid());
        _rng.Randomize();

        if (_chargeFx != null) _chargeFx.Emitting = false;
        _player = GetTree().GetFirstNodeInGroup("player") as Node3D;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!AiEnabled) return;

        float dt = (float)delta;

        if (_player == null)
        {
            _player = GetTree().GetFirstNodeInGroup("player") as Node3D;
            if (_player == null) return;
        }

        float distance = GlobalPosition.DistanceTo(_player.GlobalPosition);
        CanSeePlayer = distance <= DetectionRange;
        CanAttackPlayer = distance <= BoostAttackRange;

        if (_cooldownTimer > 0f) _cooldownTimer -= dt;

        if (Charging)
        {
            _chargeTimer += dt;
            if (_chargeTimer >= BoostChargeTime)
                FinishBoostAttack();
            return;
        }

        bool grounded = IsGrounded();

        if (CanSeePlayer && CanAttackPlayer)
        {
            StartBoostAttack();
            return;
        }

        if (CanSeePlayer && grounded)
        {
            ChasePlayer(dt);
        }
        else if (!CanSeePlayer && grounded)
        {
            Wander(dt);
        }

        // Keep the enemy readable and prevent runaway acceleration.
        if (LinearVelocity.Length() > MaxVelocity)
            LinearVelocity = LinearVelocity.Normalized() * MaxVelocity;
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        _combat?.ProcessContacts(state);
    }

    public void DisableAi()
    {
        AiEnabled = false;
        Charging = false;
        SetPhysicsProcess(false);
        if (_chargeFx != null) _chargeFx.Emitting = false;
    }

    private bool IsGrounded()
    {
        Vector3 from = GlobalPosition + Vector3.Down * 0.95f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 0.35f, 1, _selfExclude);
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }

    private void ChasePlayer(float dt)
    {
        Vector3 direction = _player.GlobalPosition - GlobalPosition;
        direction.Y = 0f;
        if (direction.LengthSquared() < 1e-6f) return;
        ApplyCentralForce(direction.Normalized() * InputForce * Mass);
    }

    private void Wander(float dt)
    {
        _wanderTimer -= dt;
        if (!_hasWanderPoint || _wanderTimer <= 0f)
            PickWanderPoint();

        Vector3 direction = _wanderPoint - GlobalPosition;
        direction.Y = 0f;
        if (direction.LengthSquared() < 1f)
        {
            _hasWanderPoint = false;
            return;
        }

        ApplyCentralForce(direction.Normalized() * (InputForce * 0.5f) * Mass);
    }

    private void PickWanderPoint()
    {
        Vector2 randomCircle = _rng.RandfRange(0f, 1f) * Vector2.Right.Rotated(_rng.RandfRange(0f, Mathf.Tau));
        _wanderPoint = new Vector3(
            GlobalPosition.X + randomCircle.X * WanderRadius,
            GlobalPosition.Y,
            GlobalPosition.Z + randomCircle.Y * WanderRadius);
        _wanderTimer = _rng.RandfRange(WanderIntervalMin, WanderIntervalMax);
        _hasWanderPoint = true;
    }

    private void StartBoostAttack()
    {
        Charging = true;
        _chargeTimer = 0f;
        LinearVelocity = new Vector3(0f, LinearVelocity.Y, 0f);

        if (_chargeFx != null)
        {
            _chargeFx.Restart();
            _chargeFx.Emitting = true;
        }
    }

    private void FinishBoostAttack()
    {
        Charging = false;
        _cooldownTimer = BoostCooldown;
        if (_chargeFx != null) _chargeFx.Emitting = false;

        Vector3 direction = (_player.GlobalPosition - GlobalPosition);
        if (direction.LengthSquared() < 1e-6f) return;
        direction = direction.Normalized();

        FaceDirection(direction);
        ApplyCentralImpulse(direction * BoostForce);
    }

    private void FaceDirection(Vector3 direction)
    {
        Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
        LookAt(GlobalPosition + direction, up);
    }
}
