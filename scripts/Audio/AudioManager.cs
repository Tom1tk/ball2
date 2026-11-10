using Godot;
using System.Collections.Generic;

/// <summary>
/// Small pooled sound player for the generated Ball2 sound set.
/// Registered as an autoload so menu, gameplay and pause sounds share one bus.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private static readonly string[] SoundNames =
    {
        "boost_charge", "boost_release", "dodge",
        "hit_light", "hit_heavy",
        "grapple_fire", "grapple_hook",
        "death", "ui_click",
    };

    private readonly Dictionary<string, AudioStream> _streams = new();

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        foreach (string name in SoundNames)
        {
            var stream = GD.Load<AudioStream>($"res://assets/audio/{name}.wav");
            if (stream != null)
                _streams[name] = stream;
            else
                GD.PushWarning($"AudioManager: missing sound '{name}'");
        }
    }

    public bool HasSound(string name) => _streams.ContainsKey(name);

    public void Play2D(string name, float volumeDb = 0f, float pitch = 1f)
    {
        if (!_streams.TryGetValue(name, out AudioStream stream)) return;

        var player = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = volumeDb,
            PitchScale = pitch,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    public void Play3D(string name, Vector3 position, float volumeDb = 0f, float pitch = 1f)
    {
        if (!_streams.TryGetValue(name, out AudioStream stream)) return;

        var player = new AudioStreamPlayer3D
        {
            Stream = stream,
            VolumeDb = volumeDb,
            PitchScale = pitch,
            MaxDistance = 80f,
            UnitSize = 8f,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(player);
        player.GlobalPosition = position;
        player.Finished += player.QueueFree;
        player.Play();
    }
}
