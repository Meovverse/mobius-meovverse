using Godot;
using GraveCanTell.Core;

namespace GraveCanTell.Game;

/// <summary>
/// 通关后的制作人员名单：播 assets/videos/Ending.ogv（由美术交付的 Ending.mp4 转码，
/// Godot 只认 Ogg Theora），背景音乐 assets/audio/ending_theme.mp3。
/// 放完、按任意键、或超时都回封面——不会卡死。
/// </summary>
public partial class Credits : Node2D
{
    private VideoStreamPlayer _video;
    private float _t;

    public override void _Ready()
    {
        MenuHud.Ensure(GetTree(), null, false);   // 名单页不带返回钮（HUD 常驻 Root，得显式隐藏）
        AudioIndex.StopBgm();                      // 名单页只放 ending_theme
        AddChild(new ColorRect { Color = new Color(0, 0, 0), Size = new Vector2(640, 360),
                                 MouseFilter = Control.MouseFilterEnum.Ignore });

        var vs = ResourceLoader.Load<VideoStream>("res://assets/videos/Ending.ogv");
        if (vs != null)
        {
            _video = new VideoStreamPlayer { Stream = vs, Expand = true, MouseFilter = Control.MouseFilterEnum.Ignore,
                                             Position = Vector2.Zero, Size = new Vector2(640, 360) };
            AddChild(_video);
            _video.Play();
        }
        else
        {
            AddChild(new Label { Text = "（制作人员名单视频缺失）", Position = new Vector2(0, 170),
                                 Size = new Vector2(640, 20), HorizontalAlignment = HorizontalAlignment.Center });
        }

        // 背景音乐（视频本身无音轨；BGM 单独给，循环）
        var m = ResourceLoader.Load<AudioStream>("res://assets/audio/ending_theme.mp3");
        if (m != null)
        {
            if (m is AudioStreamMP3 mp3) mp3.Loop = true;
            var mu = new AudioStreamPlayer { Stream = m, VolumeDb = -4 };
            AddChild(mu);
            mu.Play();
        }

        AddChild(new Label { Text = "按任意键跳过", Position = new Vector2(0, 334),
                             Size = new Vector2(616, 18), HorizontalAlignment = HorizontalAlignment.Right,
                             Modulate = new Color(1, 1, 1, 0.5f) });
    }

    private bool _gone;

    // Citrate#50：用事件式 _Input 跳过，比逐帧轮询可靠（GUI/视频控件吞不吞都拦不住 _Input）。
    public override void _Input(InputEvent e)
    {
        if (_gone || _t < 0.4f) return;
        if (e is InputEventKey { Pressed: true } || e is InputEventMouseButton { Pressed: true }
            || e is InputEventJoypadButton { Pressed: true } || e.IsActionPressed("ui_accept"))
            Go();
    }

    public override void _Process(double dt)
    {
        _t += (float)dt;
        if (_video == null || !_video.IsPlaying() || _t > 300f) Go();
    }

    private void Go()
    {
        if (_gone) return;
        _gone = true;
        GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
    }
}
