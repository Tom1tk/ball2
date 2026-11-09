using Godot;

/// <summary>
/// HUD, pause menu, lose screen and grapple cursors.
/// Port of the Unity UIScript, minus the legacy commented-out win/lose code.
/// </summary>
public partial class UIScript : CanvasLayer
{
    public bool GamePlaying { get; private set; }
    public int EnemiesAlive;
    public int EnemiesMax = 2;
    public int StrawbCollected;
    public int StrawbMax;

    public BallMovement Player { get; private set; }

    private ProgressBar _boostBar;
    private ProgressBar _dodgeBar;
    private Label _enemyCounter;
    private Label _strawberryCounter;
    private Label _debugLabel;
    private TextureRect _crosshairLeft;
    private TextureRect _crosshairRight;
    private Control _pauseMenu;
    private Control _loseScreen;

    public override void _Ready()
    {
        AddToGroup("ui");
        ProcessMode = ProcessModeEnum.Always;

        _boostBar = GetNode<ProgressBar>("Root/BoostBar");
        _dodgeBar = GetNode<ProgressBar>("Root/DodgeBar");
        _enemyCounter = GetNode<Label>("Root/EnemyCounter");
        _strawberryCounter = GetNode<Label>("Root/StrawberryCounter");
        _debugLabel = GetNode<Label>("Root/DebugLabel");
        _crosshairLeft = GetNode<TextureRect>("Root/CrosshairLeft");
        _crosshairRight = GetNode<TextureRect>("Root/CrosshairRight");
        _pauseMenu = GetNode<Control>("Root/PauseMenu");
        _loseScreen = GetNode<Control>("Root/LoseScreen");

        Player = GetTree().GetFirstNodeInGroup("player") as BallMovement;
        EnemiesMax = Mathf.Max(1, GetTree().GetNodesInGroup("enemies").Count);
        EnemiesAlive = EnemiesMax;

        _dodgeBar.MaxValue = Player?.DodgeCooldown ?? 1f;
        _crosshairLeft.PivotOffset = _crosshairLeft.Size * 0.5f;
        _crosshairRight.PivotOffset = _crosshairRight.Size * 0.5f;

        HideBoostUi();
        HideDodgeUi();
        HidePauseUiInternal();
        _loseScreen.Visible = false;

        GetNode<Button>("Root/PauseMenu/Panel/Buttons/ResumeButton").Pressed += HidePauseUi;
        GetNode<Button>("Root/PauseMenu/Panel/Buttons/RestartButton").Pressed += RestartGame;
        GetNode<Button>("Root/PauseMenu/Panel/Buttons/MenuButton").Pressed += BackToMenu;
        GetNode<Button>("Root/PauseMenu/Panel/Buttons/QuitButton").Pressed += QuitGame;
        GetNode<Button>("Root/LoseScreen/Panel/Buttons/RestartButton").Pressed += RestartGame;
        GetNode<Button>("Root/LoseScreen/Panel/Buttons/MenuButton").Pressed += BackToMenu;

        UpdateEnemyCounter();
        UpdateStrawberryCounter();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Process(double delta)
    {
        if (Player != null && IsInstanceValid(Player))
        {
            if (_boostBar.Visible)
                _boostBar.Value = Player.BoostLevel;
            if (_dodgeBar.Visible)
                _dodgeBar.Value = Player.DodgeTimer;
        }
    }

    public void SetDebugText(string text)
    {
        if (_debugLabel != null)
            _debugLabel.Text = text;
    }

    public void ShowBoostUi() => _boostBar.Visible = true;
    public void HideBoostUi() => _boostBar.Visible = false;
    public void ShowDodgeUi() => _dodgeBar.Visible = true;
    public void HideDodgeUi() => _dodgeBar.Visible = false;

    public void ShowPauseUi()
    {
        GamePlaying = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = true;
        _pauseMenu.Visible = true;
    }

    public void HidePauseUi()
    {
        if (_loseScreen.Visible) return;

        GamePlaying = false;
        _pauseMenu.Visible = false;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void TogglePause()
    {
        if (GamePlaying) HidePauseUi();
        else ShowPauseUi();
    }

    public void ShowLoseUi()
    {
        _loseScreen.Visible = true;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void RestartLevel()
    {
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    public void RestartGame()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    public void BackToMenu()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://scenes/menu.tscn");
    }

    public void QuitGame() => GetTree().Quit();

    public void EnemyDied()
    {
        EnemiesAlive = Mathf.Max(0, EnemiesAlive - 1);
        UpdateEnemyCounter();
    }

    public void AddStrawberry()
    {
        StrawbCollected++;
        UpdateStrawberryCounter();
    }

    public void SetCrosshair(bool left, Vector2 screenPosition, bool visible, float scale)
    {
        TextureRect crosshair = left ? _crosshairLeft : _crosshairRight;
        crosshair.Visible = visible;
        if (!visible) return;

        crosshair.Scale = Vector2.One * scale;
        crosshair.Position = screenPosition - crosshair.Size * scale * 0.5f;
    }

    private void HidePauseUiInternal()
    {
        GamePlaying = false;
        _pauseMenu.Visible = false;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void UpdateEnemyCounter()
    {
        if (_enemyCounter != null)
            _enemyCounter.Text = $"Enemies: {EnemiesAlive} / {EnemiesMax}";
    }

    private void UpdateStrawberryCounter()
    {
        if (_strawberryCounter != null)
            _strawberryCounter.Text = $"Strawberries: {StrawbCollected} / {StrawbMax}";
    }
}
