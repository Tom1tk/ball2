using Godot;

/// <summary>
/// Main menu: play, how-to-play and quit. Port of the Unity MainMenuScript.
/// </summary>
public partial class MainMenuScript : Control
{
    [Export] public string GameScene = "res://scenes/main.tscn";

    private Control _howToPlay;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _howToPlay = GetNode<Control>("HowToPlay");
        _howToPlay.Visible = false;

        GetNode<Button>("MenuBox/PlayButton").Pressed += StartGame;
        GetNode<Button>("MenuBox/HowToButton").Pressed += ToggleHowToPlay;
        GetNode<Button>("MenuBox/QuitButton").Pressed += QuitGame;
        GetNode<Button>("HowToPlay/Panel/CloseButton").Pressed += ToggleHowToPlay;
    }

    private void StartGame()
    {
        GetTree().ChangeSceneToFile(GameScene);
    }

    private void ToggleHowToPlay()
    {
        _howToPlay.Visible = !_howToPlay.Visible;
    }

    private void QuitGame()
    {
        GetTree().Quit();
    }
}
