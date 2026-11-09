using Godot;

/// <summary>
/// Integer health with the five ball-damage visual states from the Unity project.
/// Player death hides the ball; enemy death disables AI and removes the ball after 1.5 s.
/// </summary>
public partial class Health : Node
{
    [Export] public int MaxHealth = 4;

    public int CurrentHealth { get; private set; }
    public bool Died { get; private set; }
    public bool InCombat { get; private set; }

    private RigidBody3D _body;
    private MeshInstance3D _mesh;
    private EnemyAI _enemyAi;
    private UIScript _ui;
    private CpuParticles3D _chargeFx;
    private Texture2D[] _textures;

    private static readonly string[] DamageTexturePaths =
    {
        "res://assets/textures/BallTex3DMG2.png", // 0 uses the most damaged texture, tinted dark
        "res://assets/textures/BallTex3DMG3.png",
        "res://assets/textures/BallTex3DMG2.png",
        "res://assets/textures/BallTex3DMG1.png",
        "res://assets/textures/BallTex3.png",
    };

    public override void _Ready()
    {
        _body = GetParent<RigidBody3D>();
        _mesh = _body.GetNodeOrNull<MeshInstance3D>("Sphere");
        _enemyAi = _body as EnemyAI;
        _chargeFx = _body.GetNodeOrNull<CpuParticles3D>("ChargeFX");
        _ui = GetTree().GetFirstNodeInGroup("ui") as UIScript;

        _textures = new Texture2D[DamageTexturePaths.Length];
        for (int i = 0; i < DamageTexturePaths.Length; i++)
            _textures[i] = GD.Load<Texture2D>(DamageTexturePaths[i]);

        CurrentHealth = MaxHealth;
        UpdateVisual();
    }

    public void TakeDamage(float damage)
    {
        if (Died) return;

        int amount = Mathf.CeilToInt(damage);
        if (amount <= 0) return;

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        UpdateVisual();

        if (CurrentHealth <= 0) Die();
    }

    public void HealDamage()
    {
        if (Died) return;

        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + 1);
        UpdateVisual();
    }

    public void CombatStarted() => InCombat = true;
    public void CombatEnded() => InCombat = false;

    private void UpdateVisual()
    {
        if (_mesh == null) return;
        if (_mesh.MaterialOverride is not StandardMaterial3D material) return;

        int level = Mathf.Clamp(CurrentHealth, 0, MaxHealth);
        int textureIndex = Mathf.Clamp(level, 0, _textures.Length - 1);
        material.AlbedoTexture = _textures[textureIndex];

        if (level == 0)
        {
            material.EmissionEnabled = true;
            material.Emission = new Color(0.9f, 0.1f, 0.05f);
            material.EmissionEnergyMultiplier = 0.6f;
        }
        else
        {
            material.EmissionEnabled = false;
        }
    }

    private void Die()
    {
        Died = true;
        if (_ui == null) _ui = GetTree().GetFirstNodeInGroup("ui") as UIScript;

        if (_body.IsInGroup("player"))
        {
            _body.SetDeferred("freeze", true);
            _body.SetDeferred("visible", false);
            _ui?.ShowLoseUi();
            return;
        }

        _enemyAi?.DisableAi();
        if (_chargeFx != null) _chargeFx.Emitting = false;
        _ui?.EnemyDied();

        _body.SetDeferred("collision_layer", 0);
        _body.SetDeferred("collision_mask", 0);

        SceneTreeTimer timer = GetTree().CreateTimer(1.5);
        timer.Timeout += () =>
        {
            if (IsInstanceValid(_body))
                _body.QueueFree();
        };
    }
}
