using Godot;
using System.Collections.Generic;

/// <summary>
/// HUD, pause menu, controls/rebinding screen, lose screen and grapple cursors.
/// Port of the Unity UIScript, without the legacy strawberry/win-screen remnants.
/// </summary>
public partial class UIScript : CanvasLayer
{
    public bool GamePlaying { get; private set; }
    public int EnemiesAlive;
    public int EnemiesMax = 2;

    public BallMovement Player { get; private set; }

    private ProgressBar _boostBar;
    private ProgressBar _dodgeBar;
    private Label _enemyCounter;
    private Label _debugLabel;
    private TextureRect _crosshairLeft;
    private TextureRect _crosshairRight;
    private Control _pauseMenu;
    private Control _loseScreen;
    private Control _controlsPanel;
    private VBoxContainer _controlsList;
    private readonly Dictionary<string, Button> _bindingButtons = new();
    private string _awaitingAction;
    private Button _awaitingButton;

    public override void _Ready()
    {
        AddToGroup("ui");
        ProcessMode = ProcessModeEnum.Always;

        _boostBar = GetNode<ProgressBar>("Root/BoostBar");
        _dodgeBar = GetNode<ProgressBar>("Root/DodgeBar");
        _enemyCounter = GetNode<Label>("Root/EnemyCounter");
        _debugLabel = GetNode<Label>("Root/DebugLabel");
        _crosshairLeft = GetNode<TextureRect>("Root/CrosshairLeft");
        _crosshairRight = GetNode<TextureRect>("Root/CrosshairRight");
        _pauseMenu = GetNode<Control>("Root/PauseMenu");
        _loseScreen = GetNode<Control>("Root/LoseScreen");
        _controlsPanel = GetNode<Control>("Root/ControlsPanel");
        _controlsList = GetNode<VBoxContainer>("Root/ControlsPanel/Panel/Scroll/List");

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
        _controlsPanel.Visible = false;
        BuildControlsRows();

        GetNode<Button>("Root/PauseMenu/Panel/Buttons/ResumeButton").Pressed += HidePauseUi;
        GetNode<Button>("Root/PauseMenu/Panel/Buttons/RestartButton").Pressed += RestartGame;
        GetNode<Button>("Root/PauseMenu/Panel/Buttons/ControlsButton").Pressed += ShowControls;
        GetNode<Button>("Root/PauseMenu/Panel/Buttons/MenuButton").Pressed += BackToMenu;
        GetNode<Button>("Root/PauseMenu/Panel/Buttons/QuitButton").Pressed += QuitGame;
        GetNode<Button>("Root/LoseScreen/Panel/Buttons/RestartButton").Pressed += RestartGame;
        GetNode<Button>("Root/LoseScreen/Panel/Buttons/MenuButton").Pressed += BackToMenu;
        GetNode<Button>("Root/ControlsPanel/Panel/BackButton").Pressed += HideControls;
        WireButtonSounds();

        UpdateEnemyCounter();
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

    public override void _Input(InputEvent @event)
    {
        if (_awaitingAction == null) return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape)
            {
                CancelRebind();
            }
            else
            {
                var binding = new InputEventKey { PhysicalKeycode = key.PhysicalKeycode };
                InputBindings.Instance?.Rebind(_awaitingAction, binding);
                FinishRebind();
            }
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton mouse && mouse.Pressed)
        {
            var binding = new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex };
            InputBindings.Instance?.Rebind(_awaitingAction, binding);
            FinishRebind();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ShowControls()
    {
        RefreshBindingLabels();
        _controlsPanel.Visible = true;
    }

    private void HideControls()
    {
        CancelRebind();
        _controlsPanel.Visible = false;
    }

    private void BuildControlsRows()
    {
        foreach ((string action, string label) in InputBindings.BindableActions)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            var nameLabel = new Label
            {
                Text = label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 18);

            var button = new Button
            {
                Text = InputBindings.Instance?.GetEventLabel(action) ?? "?",
                CustomMinimumSize = new Vector2(190f, 38f),
                Alignment = HorizontalAlignment.Center,
            };
            button.AddThemeFontSizeOverride("font_size", 17);
            button.Pressed += () => BeginRebind(action, button);

            row.AddChild(nameLabel);
            row.AddChild(button);
            _controlsList.AddChild(row);
            _bindingButtons[action] = button;
        }
    }

    private void BeginRebind(string action, Button button)
    {
        CancelRebind();
        _awaitingAction = action;
        _awaitingButton = button;
        button.Text = "Press a key... (Esc cancels)";
        button.GrabFocus();
    }

    private void FinishRebind()
    {
        if (_awaitingButton != null && _awaitingAction != null)
            _awaitingButton.Text = InputBindings.Instance?.GetEventLabel(_awaitingAction) ?? "?";
        _awaitingAction = null;
        _awaitingButton = null;
    }

    private void CancelRebind()
    {
        if (_awaitingButton != null && _awaitingAction != null)
            _awaitingButton.Text = InputBindings.Instance?.GetEventLabel(_awaitingAction) ?? "?";
        _awaitingAction = null;
        _awaitingButton = null;
    }

    private void RefreshBindingLabels()
    {
        foreach ((string action, _) in InputBindings.BindableActions)
        {
            if (_bindingButtons.TryGetValue(action, out Button button))
                button.Text = InputBindings.Instance?.GetEventLabel(action) ?? "?";
        }
    }

    private void WireButtonSounds()
    {
        foreach (Node node in FindChildren("*", "Button", true, false))
        {
            if (node is Button button)
                button.Pressed += () => AudioManager.Instance?.Play2D("ui_click", -8f);
        }
    }

    private void UpdateEnemyCounter()
    {
        if (_enemyCounter != null)
            _enemyCounter.Text = $"Enemies: {EnemiesAlive} / {EnemiesMax}";
    }
}
