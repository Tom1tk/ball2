using Godot;

/// <summary>
/// Player ball movement: reduced-gravity rolling, air strafe, charged boost,
/// left/right dodge, and jump-off point for combat/grapple/lock-on components.
/// Port of the Unity BallMovement MonoBehaviour.
/// </summary>
public partial class BallMovement : RigidBody3D
{
	[ExportGroup("Movement")]
	[Export] public float MaxVelocity = 20f;
	[Export] public float AirMaxVelocity = 50f;
	[Export] public float InputForce = 100f;
	[Export] public float DodgeForce = 25f;
	[Export] public float DodgeCooldown = 1f;
	[Export] public float BoostForce = 70f;
	[Export] public float BoostMax = 1f;
	[Export] public float MovingSpeedThreshold = 1f;
	[Export] public float AirStrafeForce = 0.5f;
	[Export] public bool DisplayDebugText = true;

	[ExportGroup("Tuning")]
	[Export] public float ChargeLiftFactor = 0.85f;

	public float BoostLevel { get; private set; }
	public float DodgeTimer { get; private set; }
	public bool Charging { get; private set; }
	public Vector2 MovementInput { get; private set; }
	public float Speed => LinearVelocity.Length();

	private RigidBody3D _body;
	private Camera3D _camera;
	private UIScript _ui;
	private GrappleSystem _grapple;
	private Health _health;
	private LockOnAdapter _lockOn;
	private CombatAdapter _combat;
	private MeshInstance3D _sphere;
	private StandardMaterial3D _material;
	private CpuParticles3D _chargeFx;
	private CpuParticles3D _speedFx;
	private CpuParticles3D _dodgeFx;
	private readonly Godot.Collections.Array<Rid> _selfExclude = new();
	private float _gravity;
	private float _speedFxTimer;

	public override void _Ready()
	{
		AddToGroup("player");

		_body = this;
		_sphere = GetNode<MeshInstance3D>("Sphere");
		_grapple = GetNode<GrappleSystem>("GrappleSystem");
		_health = GetNode<Health>("Health");
		_lockOn = GetNode<LockOnAdapter>("LockOnAdapter");
		_combat = GetNode<CombatAdapter>("CombatAdapter");
		_chargeFx = GetNode<CpuParticles3D>("ChargeFX");
		_speedFx = GetNode<CpuParticles3D>("SpeedFX");
		_dodgeFx = GetNode<CpuParticles3D>("DodgeFX");

		_camera = GetViewport().GetCamera3D();
		_ui = GetTree().GetFirstNodeInGroup("ui") as UIScript;

		_material = (StandardMaterial3D)_sphere.MaterialOverride.Duplicate();
		_sphere.MaterialOverride = _material;

		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
		DodgeTimer = DodgeCooldown;
		BoostLevel = 0f;
		Charging = false;
		_selfExclude.Add(GetRid());
		_chargeFx.Emitting = false;
		_speedFx.Emitting = false;
		_dodgeFx.Emitting = false;

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		if (_camera == null) _camera = GetViewport().GetCamera3D();
		if (_ui == null) _ui = GetTree().GetFirstNodeInGroup("ui") as UIScript;

		if (Input.IsActionJustPressed("pause"))
			_ui?.TogglePause();

		if (Input.IsActionJustPressed("restart"))
			GetTree().ReloadCurrentScene();

		if (Input.IsActionJustPressed("boost")) BoostPress();
		if (Input.IsActionJustReleased("boost")) BoostRelease();
		if (Input.IsActionJustPressed("dodge_left")) Dodge(-1f);
		if (Input.IsActionJustPressed("dodge_right")) Dodge(1f);

		MovementInput = Input.GetVector("move_left", "move_right", "move_back", "move_forward");
		Vector3 directionInput = new(MovementInput.X, 0f, MovementInput.Y);

		Vector3 camForward = -_camera.GlobalTransform.Basis.Z;
		camForward.Y = 0f;
		camForward = camForward.Normalized();
		Vector3 camRight = _camera.GlobalTransform.Basis.X;
		camRight.Y = 0f;
		camRight = camRight.Normalized();

		Vector3 relativeDirection = directionInput.X * camRight + directionInput.Z * camForward;
		if (relativeDirection.LengthSquared() > 0f)
			relativeDirection = relativeDirection.Normalized();

		bool hooked = _grapple != null && _grapple.AnyHooked;

		if ((LinearVelocity.Length() < MaxVelocity && !Charging) || hooked)
		{
			ApplyCentralForce(relativeDirection * InputForce * (hooked ? AirStrafeForce : 1f) * Mass);
		}

		if (Charging && BoostLevel < BoostMax)
		{
			BoostLevel += dt;
			ApplyCentralForce(Vector3.Up * _gravity * ChargeLiftFactor * Mass);
		}

		if (DodgeTimer < DodgeCooldown)
			DodgeTimer += dt;
		else
			_ui?.HideDodgeUi();

		bool grounded = IsGrounded();
		float cap = grounded ? MaxVelocity : AirMaxVelocity;
		if (LinearVelocity.Length() > cap)
			LinearVelocity = LinearVelocity.Normalized() * cap;

		UpdateSphereColour(LinearVelocity.Length());

		if (_speedFxTimer > 0f)
		{
			_speedFxTimer -= dt;
			if (_speedFxTimer <= 0f) _speedFx.Emitting = false;
		}

		if (DisplayDebugText && _ui != null)
		{
			_ui.SetDebugText(
				$" player input= {MovementInput}\n" +
				$" player speed= {LinearVelocity.Length():F2}\n" +
				$" boost level= {BoostLevel:F2}\n" +
				$" dodge timer= {DodgeTimer:F2}\n" +
				$" hooked= {hooked}\n" +
				$" grounded= {grounded}");
		}
	}

	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		_combat?.ProcessContacts(state);
	}

	private bool IsGrounded()
	{
		var space = GetWorld3D().DirectSpaceState;
		Vector3 from = GlobalPosition + Vector3.Down * 0.95f;
		Vector3 to = from + Vector3.Down * 0.35f;
		var query = PhysicsRayQueryParameters3D.Create(from, to, 1, _selfExclude);
		return space.IntersectRay(query).Count > 0;
	}

	private void BoostPress()
	{
		LinearVelocity *= 0.3f;
		_ui?.ShowBoostUi();
		BoostLevel = 0.1f;
		Charging = true;
		_chargeFx.Restart();
		_chargeFx.Emitting = true;
		AudioManager.Instance?.Play2D("boost_charge", -10f);
		if (_lockOn != null) _lockOn.BoostChargeStarted = true;
	}

	private void BoostRelease()
	{
		_chargeFx.Emitting = false;
		_speedFx.Emitting = false;

		Vector3 boostDirection;
		if (_lockOn != null && _lockOn.HasLock && _lockOn.TrackingPosition.DistanceSquaredTo(GlobalPosition) > 1e-4f)
			boostDirection = (_lockOn.TrackingPosition - GlobalPosition).Normalized();
		else
			boostDirection = -_camera.GlobalTransform.Basis.Z;

		ApplyCentralImpulse(boostDirection * (BoostLevel * BoostForce));

		if (BoostLevel > 0.15f)
			AudioManager.Instance?.Play2D("boost_release", -5f, 0.9f + BoostLevel * 0.2f);

		_speedFx.Lifetime = Mathf.Max(0.15f, (BoostLevel * 3f) / 10f);
		_speedFx.Restart();
		_speedFx.Emitting = true;
		_speedFxTimer = (float)_speedFx.Lifetime;

		BoostLevel = 0f;
		Charging = false;
		if (_lockOn != null) _lockOn.BoostChargeStarted = false;
		UpdateSphereColour(LinearVelocity.Length());
		_ui?.HideBoostUi();
	}

	private void Dodge(float side)
	{
		if (DodgeTimer < DodgeCooldown) return;

		Vector3 camRight = _camera.GlobalTransform.Basis.X;
		camRight.Y = 0f;
		camRight = camRight.Normalized();

		_dodgeFx.Restart();
		_dodgeFx.Emitting = true;
		_ui?.ShowDodgeUi();
		AudioManager.Instance?.Play2D("dodge", -8f, side > 0f ? 1.05f : 0.95f);
		ApplyCentralImpulse(camRight * side * DodgeForce);
		DodgeTimer = 0f;
	}

	private void UpdateSphereColour(float speed)
	{
		if (_material == null) return;

		if (Charging)
			_material.AlbedoColor = new Color(1f, 0.86f, 0.25f, 0.55f);
		else if (speed > MovingSpeedThreshold)
			_material.AlbedoColor = new Color(1f, 0.62f, 0.3f, 0.55f);
		else
			_material.AlbedoColor = new Color(0.55f, 0.75f, 1f, 0.55f);
	}
}
