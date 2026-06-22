using Godot;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using V12.Basic.Components;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.Networking;
using V12.Core.Rendering;
using V12.SampleGame;
using V12TwoDog;

public partial class RootLoop : Node3D
{
	GameRoot root;
    IRenderer renderer;
    IGameService Bootstrap;
    GodotAudioPlayer audioPlayer;
    DebugGameService debug;
	readonly Dictionary<JoyButton, bool> _prevJoyButtons = new();
	Vector2 _mouseLook;
	float _mouseSensitivity = 0.002f;
	bool _mouseCaptured;

    // ── Threading ──
    private Thread _v12Thread;
    private CancellationTokenSource _cts;
    private ConcurrentQueue<FrameSnapshot> _frameQueue = new();
    private FrameSnapshot _latestFrame;

    public override void _Input(Godot.InputEvent @event)
    {
        if (@event is Godot.InputEventMouseMotion motion)
        {
            _mouseLook.X += motion.Relative.X;
            _mouseLook.Y += motion.Relative.Y;
        }
    }

    void SetMouseCaptured(bool captured)
    {
        Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        _mouseCaptured = captured;
    }

    public override void _Ready()
    {
		V12.Core.Networking.BsonConfig.Initialize();
		Console.SetOut(new GodotConsoleWriter());
		root = new GameRoot();
		renderer = new V12TwoDog.Renderer(GetTree());
        Bootstrap = new Bootstrap();

        root.Registry.Register("Bootstrap", Bootstrap);
        audioPlayer = new GodotAudioPlayer();
        audioPlayer.Initialize(root);
        root.Registry.Register("IAudioPlayer", audioPlayer);
        AddChild(audioPlayer);
        root.Registry.Register("IRenderer", renderer);
        var _input = root.Registry.Get<InputService>();
        if (_input == null )
        {
            _input = new InputService();
            root.Registry.Register("InputService", _input);
        }
        debug = new DebugGameService();
        debug.Initialize(root);
        root.Registry.Register("DebugGameService", debug );

		root.CreateWorld("TestWorld");
		root.Initialize();

        // Initialize physics services
        root.Registry.Get<V12.Core.Systems.PhysicsService>()?.Initialize(root);
        root.Registry.Get<V12.Core.Systems.PhysicsLocomotionSystem>()?.Initialize(root);
        root.Registry.Get<V12.Core.Systems.LocomotionSystem>()?.Initialize(root);
        root.Registry.Get<V12.Core.Systems.ScriptSystem>()?.Initialize();

        CallDeferred(nameof(SetMouseCaptured), true);

        // ── Start V12 worker thread ──
        _cts = new CancellationTokenSource();
        _v12Thread = new Thread(() => V12WorkerLoop(_cts.Token))
        {
            Name = "V12Worker",
            IsBackground = true
        };
        _v12Thread.Start();
    }

    private void V12WorkerLoop(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            float dt = (float)sw.Elapsed.TotalSeconds;
            sw.Restart();
            dt = Math.Clamp(dt, 0.001f, 0.05f);

            try
            {
                root.Update(dt);

                var frame = root.CaptureFrame();
                _frameQueue.Enqueue(frame);
            }
            catch (Exception ex)
            {
                GD.Print($"[V12Worker] {ex.GetType().Name}: {ex.Message}");
            }

            int sleepMs = Math.Max(1, (int)((1f / 60f - dt) * 1000));
            Thread.Sleep(sleepMs);
        }
    }

	public override void _Process(double delta)
	{
        var inputService = root.Registry.Get<V12.Core.Input.InputService>();
		if (inputService != null)
		{
			float moveX = 0f;
			float moveY = 0f;
			float lookX = 0f;
			float lookY = 0f;

			// ── Digital input (keyboard) ──
			if (Input.IsActionPressed("strafe_right")) moveX += 1f;
			if (Input.IsActionPressed("strafe_left")) moveX -= 1f;
			if (Input.IsActionPressed("move_forwards")) moveY += 1f;
			if (Input.IsActionPressed("move_backwards")) moveY -= 1f;

			if (Input.IsActionPressed("look_right")) lookX += 1f;
			if (Input.IsActionPressed("look_left")) lookX -= 1f;
			if (Input.IsActionPressed("look_up")) lookY += 1f;
			if (Input.IsActionPressed("look_down")) lookY -= 1f;

            // ── Analog stick input (gamepad) ──
			float stickLX = Input.GetJoyAxis(0, JoyAxis.LeftX);
			float stickLY = Input.GetJoyAxis(0, JoyAxis.LeftY);
			float stickRX = Input.GetJoyAxis(0, JoyAxis.RightX);
			float stickRY = Input.GetJoyAxis(0, JoyAxis.RightY);

            // ── Mouse look (sent as delta to worker thread via PlayerComponent) ──
            if (_mouseCaptured && _mouseLook != Vector2.Zero)
            {
                var player = root.SelectedWorld?.Root?.FirstOrDefault(e => e.Name == "Player");
                if (player != null)
                {
                    var pc = player.GetComponent<PlayerComponent>();
                    if (pc != null)
                        pc.AddMouseDelta(_mouseLook.X, _mouseLook.Y);
                }
                _mouseLook = Vector2.Zero;
            }

            const float deadzone = 0.15f;
			if (Mathf.Abs(stickLX) < deadzone) stickLX = 0f;
			if (Mathf.Abs(stickLY) < deadzone) stickLY = 0f;
			if (Mathf.Abs(stickRX) < deadzone) stickRX = 0f;
			if (Mathf.Abs(stickRY) < deadzone) stickRY = 0f;

			if (Mathf.Abs(stickLX) > 0f || Mathf.Abs(stickLY) > 0f)
			{
				moveX = stickLX;
				moveY = -stickLY;
			}
			if (Mathf.Abs(stickRX) > 0f || Mathf.Abs(stickRY) > 0f)
			{
				lookX = stickRX;
				lookY = -stickRY;
			}

			moveX = Mathf.Clamp(moveX, -1f, 1f);
			moveY = Mathf.Clamp(moveY, -1f, 1f);

            // ── Send axis events ──
            inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_right", Value = moveX > 0 ? moveX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_left", Value = moveX < 0 ? -moveX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_forward", Value = moveY > 0 ? moveY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_backward", Value = moveY < 0 ? -moveY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_right", Value = lookX > 0 ? lookX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_left", Value = lookX < 0 ? -lookX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_up", Value = lookY > 0 ? lookY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_down", Value = lookY < 0 ? -lookY : 0f });
            }

			// ── Button events ──
			if (Input.IsActionJustPressed("jump"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "jump", Value = 1 });
			if (Input.IsActionJustPressed("crouch"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "crouch", Value = 1 });
			if (Input.IsActionJustPressed("run"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "run", Value = 1 });
			if (Input.IsActionJustReleased("run"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "run", Value = 0 });
			if (Input.IsActionJustPressed("lean_left"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "lean_left", Value = 1 });
			if (Input.IsActionJustReleased("lean_left"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "lean_left", Value = 0 });
			if (Input.IsActionJustPressed("lean_right"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "lean_right", Value = 1 });
			if (Input.IsActionJustReleased("lean_right"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "lean_right", Value = 0 });
			if (Input.IsActionJustPressed("escape"))
			{
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "esc", Value = 1 });
				SetMouseCaptured(!_mouseCaptured);
			}
			if (Input.IsActionJustPressed("interact"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "interact", Value = 1 });

			// ── Fly mode events ──
			if (Input.IsActionJustPressed("fly_toggle"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "fly_toggle", Value = 1 });
			if (Input.IsActionJustReleased("fly_toggle"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "fly_toggle", Value = 0 });
			if (Input.IsActionPressed("fly_up"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "fly_up", Value = 1f });
			if (Input.IsActionPressed("fly_down"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "fly_down", Value = 1f });

			// ── Controller button events ──
			var currentJoy = new Dictionary<JoyButton, bool>();
			for (int i = 0; i < (int)JoyButton.Max; i++)
			{
				var btn = (JoyButton)i;
				currentJoy[btn] = Input.IsJoyButtonPressed(0, btn);
			}

			void SendCtrlBtn(string name, JoyButton btn)
			{
				bool now = currentJoy.GetValueOrDefault(btn, false);
				bool prev = _prevJoyButtons.GetValueOrDefault(btn, false);
				if (now && !prev)
					inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = name, Value = 1 });
				if (!now && prev)
					inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = name, Value = 0 });
			}

			SendCtrlBtn("jump", JoyButton.A);
			SendCtrlBtn("crouch", JoyButton.B);
			SendCtrlBtn("interact", JoyButton.X);
			SendCtrlBtn("fly_toggle", JoyButton.Y);
			SendCtrlBtn("lean_left", JoyButton.LeftShoulder);
			SendCtrlBtn("lean_right", JoyButton.RightShoulder);
			SendCtrlBtn("esc", JoyButton.Start);

			float ltAxis = Input.GetJoyAxis(0, (JoyAxis)4);
			if (ltAxis > 0.15f)
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "run", Value = ltAxis });

			_prevJoyButtons.Clear();
			foreach (var kv in currentJoy)
				_prevJoyButtons[kv.Key] = kv.Value;

		// ── Consume latest frame snapshot on main thread ──
        while (_frameQueue.TryDequeue(out var frame))
            _latestFrame = frame;

        if (_latestFrame != null)
        {
            renderer.ApplySnapshot(_latestFrame);
            audioPlayer.ApplySnapshot(_latestFrame);
        }

        debug.Update((float)delta);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            _cts?.Cancel();
            _v12Thread?.Join(1000);
        }
    }
}

public class GodotConsoleWriter : TextWriter
{
    private readonly StringBuilder _buffer = new StringBuilder();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(string value)
    {
        _buffer.Append(value);
    }

    public override void WriteLine(string value)
    {
        _buffer.Append(value);
        Flush();
    }

    public override void WriteLine()
    {
        Flush();
    }

    public override void Flush()
    {
        if (_buffer.Length > 0)
        {
            GD.Print(_buffer.ToString().TrimEnd('\r', '\n'));
            _buffer.Clear();
        }
    }
}
