using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Headless gameplay integration tests. Loads the real main scene and exercises the
/// runtime adapters (navigation, damage textures, lock-on, grapple state machine,
/// contact damage, pause/lose flow and enemy despawn).
///
/// Run with:
///   godot-mono --headless --path . res://tests/GameplayTests.tscn
/// Exit code 0 = all passed, 1 = one or more failures.
/// </summary>
public partial class GameplayTestRunner : Node
{
    private int _passed;
    private int _failed;

    private Node _main;
    private BallMovement _player;
    private EnemyAI _enemy;
    private UIScript _ui;
    private Health _playerHealth;
    private Health _enemyHealth;
    private LockOnAdapter _lockOn;
    private GrappleSystem _grapple;
    private Camera3D _camera;

    public override async void _Ready()
    {
        GD.Print("=== Ball2 gameplay integration tests ===");

        try
        {
            await Setup();
            await TestSpawnState();
            TestArenaAndNavigation();
            await TestEnemyNavigation();
            TestAudioAndBindings();
            TestDamageTextures();
            await TestLockOn();
            await TestGrapple();
            await TestContactDamage();
            TestPauseResume();
            await TestEnemyDeath();
            TestPlayerDeath();
        }
        catch (Exception ex)
        {
            Fail("unhandled exception", ex.ToString());
        }

        GD.Print($"=== RESULT: {_passed} passed, {_failed} failed ===");
        GetTree().Paused = false;
        GetTree().Quit(_failed == 0 ? 0 : 1);
    }

    private async Task Setup()
    {
        var packed = GD.Load<PackedScene>("res://scenes/main.tscn");
        _main = packed.Instantiate();
        AddChild(_main);

        await PhysicsFrames(10);

        _player = GetTree().GetFirstNodeInGroup("player") as BallMovement;
        _enemy = GetTree().GetFirstNodeInGroup("enemies") as EnemyAI;
        _ui = GetTree().GetFirstNodeInGroup("ui") as UIScript;
        _camera = _main.GetNodeOrNull<Camera3D>("Camera");

        Check("player spawned", _player != null);
        Check("enemy spawned", _enemy != null);
        Check("HUD spawned", _ui != null);
        Check("camera current", _camera != null && _camera.Current);
        Check("audio autoload ready", AudioManager.Instance != null);
        Check("input bindings autoload ready", InputBindings.Instance != null);

        if (_player == null || _enemy == null || _ui == null || _camera == null)
            throw new InvalidOperationException("main scene did not create the expected nodes");

        _playerHealth = _player.GetNode<Health>("Health");
        _enemyHealth = _enemy.GetNode<Health>("Health");
        _lockOn = _player.GetNode<LockOnAdapter>("LockOnAdapter");
        _grapple = _player.GetNode<GrappleSystem>("GrappleSystem");
    }

    private async Task TestSpawnState()
    {
        // Let both balls settle onto the floor. Spawn heights are chosen so the
        // landing impact is below the wall-damage threshold.
        await PhysicsFrames(60);
        Check("player survives spawn without damage",
            _playerHealth.CurrentHealth == _playerHealth.MaxHealth,
            $"{_playerHealth.CurrentHealth}/{_playerHealth.MaxHealth}");
        Check("enemy survives spawn without damage",
            _enemyHealth.CurrentHealth == _enemyHealth.MaxHealth,
            $"{_enemyHealth.CurrentHealth}/{_enemyHealth.MaxHealth}");
        Check("enemy counter starts at one", _ui.EnemiesAlive == 1,
            $"{_ui.EnemiesAlive}/{_ui.EnemiesMax}");
    }

    private void TestArenaAndNavigation()
    {
        var region = _main.GetNodeOrNull<NavigationRegion3D>("Navigation");
        Check("NavigationRegion3D present", region != null);
        Check("Test2 arena under navigation region",
            region != null && region.GetNodeOrNull("ArenaTest2") != null);

        if (region != null && region.NavigationMesh != null)
        {
            Check("navmesh baked", region.NavigationMesh.GetPolygonCount() > 100,
                $"{region.NavigationMesh.GetPolygonCount()} polygons");
        }

        Check("enemy has NavigationAgent3D",
            _enemy.GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D") != null);

        // The tower at x=-35.55, z=36.37 (10 x 20 m) sits between these points.
        // A straight line is 20 m; a correct navmesh path must route around it.
        if (region != null)
        {
            Rid map = region.GetNavigationMap();
            Check("navigation map valid", map.IsValid);
            if (map.IsValid)
            {
                Vector3[] path = NavigationServer3D.MapGetPath(
                    map, new Vector3(-25f, 0.5f, 36.37f), new Vector3(-45f, 0.5f, 36.37f),
                    true, 1);
                float length = 0f;
                for (int i = 1; i < path.Length; i++)
                    length += path[i - 1].DistanceTo(path[i]);

                Check("navmesh routes around tower", path.Length >= 3 && length > 22f,
                    $"{path.Length} points, {length:F1} m");
            }
        }
    }

    private async Task TestEnemyNavigation()
    {
        // Put the tower at x=-35.55, z=36.37 between the enemy (-28) and player
        // (-42.5). The enemy is within its 15 m detection range and must path
        // around the tower rather than push into it.
        _player.GlobalPosition = new Vector3(-42.5f, 1.05f, 36.37f);
        _player.LinearVelocity = Vector3.Zero;
        _enemy.GlobalPosition = new Vector3(-28f, 1.05f, 36.37f);
        _enemy.LinearVelocity = Vector3.Zero;
        _enemy.AiEnabled = true;
        await PhysicsFrames(4);

        var agent = _enemy.GetNode<NavigationAgent3D>("NavigationAgent3D");
        await PhysicsFrames(10);

        // Sample the path before the enemy closes the distance; the final path
        // naturally shortens once it has rounded the tower.
        Vector3[] path = agent.GetCurrentNavigationPath();
        float pathLength = 0f;
        for (int i = 1; i < path.Length; i++)
            pathLength += path[i - 1].DistanceTo(path[i]);
        Check("enemy AI follows a navigation path", path.Length >= 3 && pathLength > 15f,
            $"{path.Length} points, {pathLength:F1} m");

        await PhysicsFrames(90);
        float moved = _enemy.GlobalPosition.DistanceTo(new Vector3(-28f, 1.05f, 36.37f));
        Check("enemy moves around the tower", moved > 2f,
            $"moved {moved:F1} m from start");

        _enemy.AiEnabled = false;
        _enemy.GlobalPosition = new Vector3(-25.2f, 1.05f, -27.8f);
        _enemy.LinearVelocity = Vector3.Zero;
        _player.GlobalPosition = new Vector3(0f, 1.05f, 0f);
        _player.LinearVelocity = Vector3.Zero;
        await PhysicsFrames(4);
    }

    private void TestAudioAndBindings()
    {
        Check("audio samples imported",
            AudioManager.Instance != null && AudioManager.Instance.HasSound("hit_heavy"));

        var bindings = InputBindings.Instance;
        Check("input bindings autoload ready", bindings != null);
        if (bindings == null) return;

        var original = (InputEvent)InputMap.ActionGetEvents("boost")[0].Duplicate();
        bindings.Rebind("boost", new InputEventKey { PhysicalKeycode = Key.J });
        Check("rebinding updates the input map label",
            bindings.GetEventLabel("boost") == "J", bindings.GetEventLabel("boost"));
        bindings.Rebind("boost", original);
        Check("rebinding restores the original key",
            bindings.GetEventLabel("boost") == "Space", bindings.GetEventLabel("boost"));
    }

    private void TestDamageTextures()
    {
        int before = _playerHealth.CurrentHealth;
        _playerHealth.TakeDamage(1);
        Check("player damage reduces health", _playerHealth.CurrentHealth == before - 1,
            $"{before} -> {_playerHealth.CurrentHealth}");

        var mesh = _player.GetNode<MeshInstance3D>("Sphere");
        var material = mesh.MaterialOverride as StandardMaterial3D;
        var expected = GD.Load<Texture2D>("res://assets/textures/BallTex3DMG1.png");
        Check("damage texture swaps to DMG1 at 3 HP",
            material != null && material.AlbedoTexture == expected);

        for (int i = 0; i < 4; i++) _playerHealth.HealDamage();
        Check("healing restores full health", _playerHealth.CurrentHealth == _playerHealth.MaxHealth,
            $"{_playerHealth.CurrentHealth}/{_playerHealth.MaxHealth}");
    }

    private async Task TestLockOn()
    {
        _enemy.AiEnabled = false;
        _enemy.LinearVelocity = Vector3.Zero;
        Vector3 aim = -_camera.GlobalTransform.Basis.Z;
        Vector3 inFront = _player.GlobalPosition + aim * 8f;
        inFront.Y = 1.05f; // keep the enemy on the floor; a hard landing hurts
        _enemy.GlobalPosition = inFront;
        await PhysicsFrames(12);

        Check("lock acquired on enemy in front", _lockOn.HasLock);
        Check("lock icon visible while locked",
            _lockOn.GetNode<Sprite3D>("LockIcon").Visible);

        Vector3 farAway = _player.GlobalPosition + aim * 40f;
        farAway.Y = 1.05f;
        _enemy.GlobalPosition = farAway;
        await PhysicsFrames(12);
        Check("lock breaks when enemy leaves break range", !_lockOn.HasLock);

        _enemy.GlobalPosition = new Vector3(-25.2f, 1.05f, -27.8f);
        _enemy.LinearVelocity = Vector3.Zero;
        await PhysicsFrames(4);
        Check("enemy alive after lock test", !_enemyHealth.Died,
            $"health {_enemyHealth.CurrentHealth}/{_enemyHealth.MaxHealth}");
    }

    private async Task TestGrapple()
    {
        // Keep the enemy out of the way so the hook ray hits the Test2 course.
        // Stay inside the 500 x 500 floor so it does not fall out of the world.
        _enemy.GlobalPosition = new Vector3(-200f, 1.05f, -200f);
        _enemy.LinearVelocity = Vector3.Zero;
        await PhysicsFrames(4);

        Input.ActionPress("grapple_left");
        bool hooked = await WaitUntil(() => _grapple.AnyHooked, 120);
        Check("grapple fires and hooks the course", hooked);
        Input.ActionRelease("grapple_left");

        bool returned = await WaitUntil(() => !_grapple.AnyHooked, 180);
        Check("grapple releases and returns to ready", returned);

        _enemy.GlobalPosition = new Vector3(-25.2f, 1.05f, -27.8f);
        _enemy.LinearVelocity = Vector3.Zero;
        await PhysicsFrames(4);
        Check("enemy alive after grapple test", !_enemyHealth.Died,
            $"health {_enemyHealth.CurrentHealth}/{_enemyHealth.MaxHealth}");
    }

    private async Task TestContactDamage()
    {
        Check("enemy alive before contact test", !_enemyHealth.Died,
            $"health {_enemyHealth.CurrentHealth}/{_enemyHealth.MaxHealth}");
        Check("player alive before contact test", !_playerHealth.Died);
        if (!_enemyHealth.Died)
            while (_enemyHealth.CurrentHealth < _enemyHealth.MaxHealth) _enemyHealth.HealDamage();
        if (!_playerHealth.Died)
            while (_playerHealth.CurrentHealth < _playerHealth.MaxHealth) _playerHealth.HealDamage();

        _enemy.AiEnabled = false;
        _player.GlobalPosition = new Vector3(0f, 1.05f, 0f);
        _player.LinearVelocity = Vector3.Zero;
        _enemy.GlobalPosition = new Vector3(3f, 1.05f, 0f);
        _enemy.LinearVelocity = Vector3.Zero;
        await PhysicsFrames(3);

        int enemyBefore = _enemyHealth.CurrentHealth;
        int playerBefore = _playerHealth.CurrentHealth;

        // Aim for ~9 m/s at spawn: above the 5 m/s damage threshold but not
        // enough to kill the 3 HP enemy (so the death test below still runs).
        _player.LinearVelocity = new Vector3(9f, 0f, 0f);

        bool hit = await WaitUntil(() =>
            _enemyHealth.CurrentHealth < enemyBefore || _playerHealth.CurrentHealth < playerBefore, 120);
        Check("ball-to-ball contact applies damage", hit,
            $"enemy {enemyBefore}->{_enemyHealth.CurrentHealth}, player {playerBefore}->{_playerHealth.CurrentHealth}");
    }

    private void TestPauseResume()
    {
        _ui.TogglePause();
        Check("pause menu pauses the tree", GetTree().Paused);
        Check("pause menu is visible", _main.GetNode<Control>("UI/Root/PauseMenu").Visible);

        var controlsButton = _main.GetNode<Button>("UI/Root/PauseMenu/Panel/Buttons/ControlsButton");
        var controlsPanel = _main.GetNode<Control>("UI/Root/ControlsPanel");
        controlsButton.EmitSignal("pressed");
        Check("controls screen opens from pause menu", controlsPanel.Visible);
        _main.GetNode<Button>("UI/Root/ControlsPanel/Panel/BackButton").EmitSignal("pressed");
        Check("controls screen closes", !controlsPanel.Visible);

        _ui.TogglePause();
        Check("resume unpauses the tree", !GetTree().Paused);
        Check("pause menu hides on resume",
            !_main.GetNode<Control>("UI/Root/PauseMenu").Visible);
    }

    private async Task TestEnemyDeath()
    {
        int aliveBefore = _ui.EnemiesAlive;
        _enemyHealth.TakeDamage(99f);

        Check("enemy dies from lethal damage", _enemyHealth.Died);
        Check("enemy AI disabled on death", !_enemy.AiEnabled);
        Check("enemy counter decrements", _ui.EnemiesAlive == aliveBefore - 1,
            $"{aliveBefore} -> {_ui.EnemiesAlive}");

        await PhysicsFrames(120);
        Check("enemy despawns after death delay",
            !IsInstanceValid(_enemy) || !_enemy.IsInsideTree());
    }

    private void TestPlayerDeath()
    {
        _playerHealth.TakeDamage(99f);
        Check("player dies from lethal damage", _playerHealth.Died);
        Check("lose screen shown", _main.GetNode<Control>("UI/Root/LoseScreen").Visible);
        Check("death pauses the tree", GetTree().Paused);
        GetTree().Paused = false;
    }

    private async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> WaitUntil(Func<bool> predicate, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (predicate()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return predicate();
    }

    private void Check(string name, bool condition, string detail = null)
    {
        if (condition)
        {
            _passed++;
            GD.Print($"[PASS] {name}" + (detail != null ? $" ({detail})" : ""));
        }
        else
        {
            _failed++;
            GD.PrintErr($"[FAIL] {name}" + (detail != null ? $" ({detail})" : ""));
        }
    }

    private void Fail(string name, string detail)
    {
        _failed++;
        GD.PrintErr($"[FAIL] {name}: {detail}");
    }
}
