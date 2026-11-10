using Godot;

/// <summary>
/// Loads, saves and applies player input bindings. Defaults come from project.godot;
/// overrides live in <c>user://input_bindings.cfg</c> so they survive restarts.
/// Registered as an autoload.
/// </summary>
public partial class InputBindings : Node
{
    public static InputBindings Instance { get; private set; }

    public static readonly (string Action, string Label)[] BindableActions =
    {
        ("move_forward", "Move Forward"),
        ("move_back", "Move Back"),
        ("move_left", "Move Left"),
        ("move_right", "Move Right"),
        ("boost", "Boost / Charge"),
        ("dodge_left", "Dodge Left"),
        ("dodge_right", "Dodge Right"),
        ("grapple_left", "Grapple Left"),
        ("grapple_right", "Grapple Right"),
        ("reel", "Reel"),
        ("pause", "Pause"),
        ("restart", "Restart"),
    };

    private const string SavePath = "user://input_bindings.cfg";

    public override void _Ready()
    {
        Instance = this;
        LoadBindings();
    }

    public string GetEventLabel(string action)
    {
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            if (e is InputEventKey key)
                return key.PhysicalKeycode != Key.None
                    ? key.AsTextPhysicalKeycode()
                    : key.AsTextKeycode();
            if (e is InputEventMouseButton mouse)
            {
                return mouse.ButtonIndex switch
                {
                    MouseButton.Left => "LMB",
                    MouseButton.Right => "RMB",
                    MouseButton.Middle => "MMB",
                    _ => $"Mouse {(int)mouse.ButtonIndex}",
                };
            }
        }
        return "Unbound";
    }

    public void Rebind(string action, InputEvent newEvent)
    {
        if (!InputMap.HasAction(action)) return;

        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, newEvent);
        SaveBindings();
    }

    private void SaveBindings()
    {
        var config = new ConfigFile();

        foreach ((string action, _) in BindableActions)
        {
            int index = 0;
            foreach (InputEvent e in InputMap.ActionGetEvents(action))
            {
                if (e is InputEventKey key)
                {
                    config.SetValue(action, $"{index}_type", "key");
                    config.SetValue(action, $"{index}_code", (int)key.PhysicalKeycode);
                    index++;
                }
                else if (e is InputEventMouseButton mouse)
                {
                    config.SetValue(action, $"{index}_type", "mouse");
                    config.SetValue(action, $"{index}_code", (int)mouse.ButtonIndex);
                    index++;
                }
            }
            config.SetValue(action, "count", index);
        }

        Error err = config.Save(SavePath);
        if (err != Error.Ok)
            GD.PushWarning($"InputBindings: could not save bindings ({err})");
    }

    private void LoadBindings()
    {
        var config = new ConfigFile();
        if (config.Load(SavePath) != Error.Ok) return;

        foreach ((string action, _) in BindableActions)
        {
            if (!InputMap.HasAction(action)) continue;

            int count = (int)config.GetValue(action, "count", 0);
            if (count <= 0) continue;

            var events = new Godot.Collections.Array<InputEvent>();
            for (int i = 0; i < count; i++)
            {
                string type = (string)config.GetValue(action, $"{i}_type", "key");
                int code = (int)config.GetValue(action, $"{i}_code", 0);

                if (type == "mouse")
                {
                    events.Add(new InputEventMouseButton { ButtonIndex = (MouseButton)code });
                }
                else
                {
                    events.Add(new InputEventKey { PhysicalKeycode = (Key)code });
                }
            }

            if (events.Count == 0) continue;
            InputMap.ActionEraseEvents(action);
            foreach (InputEvent e in events)
                InputMap.ActionAddEvent(action, e);
        }
    }
}
