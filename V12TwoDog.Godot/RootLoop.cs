using Godot;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.Networking;
using V12.SampleGame;
using V12TwoDog;

public partial class RootLoop : Node3D
{
	// Called when the node enters the scene tree for the first time.
	GameRoot root;
    IRenderer renderer;
    IGameService Bootstrap;
    WorldXmlHotReloader xm;
    DebugGameService debug;
	readonly Dictionary<JoyButton, bool> _prevJoyButtons = new();
	Vector2 _mouseLook;
	float _mouseSensitivity = 0.002f;
	bool _mouseCaptured;

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

        // Capture mouse for look control (deferred so the scene is ready)
        CallDeferred(nameof(SetMouseCaptured), true);


        /// Testing code
        //var hotreloader = new worldxmlsourcecomponent("hotreload.xml", true);
        //var elem = new element { components = { hotreloader }, name = "hotreloadelement" };
        //root.selectedworld.addelement(elem);
        //xm = (worldxmlhotreloader)root.registry.get("hotreloader_testworld").serviceinstance;
        //xm.initialize();
        //xm._watchers["d:\\git\\v12\\v12twodog\\v12twodog.godot\\.godot\\mono\\temp\\bin\\debug\\hotreload.xml"].changed += (s, e) => debug._needsUpdate = true;

    }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
        var inputService = root.Registry.Get<V12.Core.Input.InputService>();
		if (inputService != null)
		{
			float moveX = 0f;
			float moveY = 0f;
			float lookX = 0f;
			float lookY = 0f;

			// ── Digital input (keyboard) ──────────────────────────────────────
			if (Input.IsActionPressed("strafe_right")) moveX += 1f;
			if (Input.IsActionPressed("strafe_left")) moveX -= 1f;
			if (Input.IsActionPressed("move_forwards")) moveY += 1f;
			if (Input.IsActionPressed("move_backwards")) moveY -= 1f;

			if (Input.IsActionPressed("look_right")) lookX += 1f;
			if (Input.IsActionPressed("look_left")) lookX -= 1f;
			if (Input.IsActionPressed("look_up")) lookY += 1f;
			if (Input.IsActionPressed("look_down")) lookY -= 1f;

            // ── Analog stick input (gamepad) ──────────────────────────────────
			// Left stick: movement
			float stickLX = Input.GetJoyAxis(0, JoyAxis.LeftX);  // -1 left, +1 right
			float stickLY = Input.GetJoyAxis(0, JoyAxis.LeftY);  // -1 up,   +1 down

			// Right stick: camera look
			float stickRX = Input.GetJoyAxis(0, JoyAxis.RightX); // -1 left, +1 right
			float stickRY = Input.GetJoyAxis(0, JoyAxis.RightY); // -1 up,   +1 down

            // ── Mouse look (Y inverted, applied directly bypassing ActionMap clamp) ──
            if (_mouseCaptured && _mouseLook != Vector2.Zero)
            {
                float ms = 0.002f; // rad/pixel
                var player = root.SelectedWorld?.Root?.FirstOrDefault(e => e.Name == "Player");
                if (player != null)
                {
                    var playerT = player.GetComponent<V12.Components.TransformComponent>();
                    if (playerT != null)
                        playerT.RY += -_mouseLook.X * ms;

                    var cam = player.FindChildByName("PlayerCamera3D");
                    if (cam != null)
                    {
                        var camT = cam.GetComponent<V12.Components.TransformComponent>();
                        if (camT != null)
                        {
                            float newPitch = camT.RX + (-_mouseLook.Y) * ms;
                            camT.RX = Math.Clamp(newPitch, -1.520f, 1.520f); // ~±87°
                        }
                    }
                }
                _mouseLook = Vector2.Zero;
            }
            //Console.WriteLine($"Raw stick input: LX={stickLX:F2}, LY={stickLY:F2}, RX={stickRX:F2}, RY={stickRY:F2}");
            // Apply deadzone (Godot may already apply one, but this is safety)
            const float deadzone = 0.15f;
			if (Mathf.Abs(stickLX) < deadzone) stickLX = 0f;
			if (Mathf.Abs(stickLY) < deadzone) stickLY = 0f;
			if (Mathf.Abs(stickRX) < deadzone) stickRX = 0f;
			if (Mathf.Abs(stickRY) < deadzone) stickRY = 0f;

			// Blend: analog overrides digital when the stick is active
			if (Mathf.Abs(stickLX) > 0f || Mathf.Abs(stickLY) > 0f)
			{
				moveX = stickLX;
				moveY = -stickLY; // invert: stick down is +Y, but forward should be positive
			}
			if (Mathf.Abs(stickRX) > 0f || Mathf.Abs(stickRY) > 0f)
			{
				lookX = stickRX;
				lookY = -stickRY; // invert: stick down is +Y, but look-up should be positive
			}

			// Clamp move to [-1, 1]; look passes through unclamped (mouse needs full range)
			moveX = Mathf.Clamp(moveX, -1f, 1f);
			moveY = Mathf.Clamp(moveY, -1f, 1f);


            // ── Debug logging ───────────────────────────────────────────────
            //GD.Print($"Input: moveX={moveX:F2}, moveY={moveY:F2}, lookX={lookX:F2}, lookY={lookY:F2}");

            // ── Send axis events to V12 input system ──────────────────────────
            inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_right", Value = moveX > 0 ? moveX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_left", Value = moveX < 0 ? -moveX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_forward", Value = moveY > 0 ? moveY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_backward", Value = moveY < 0 ? -moveY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_right", Value = lookX > 0 ? lookX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_left", Value = lookX < 0 ? -lookX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_up", Value = lookY > 0 ? lookY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_down", Value = lookY < 0 ? -lookY : 0f });
            }

			// ── Button events ─────────────────────────────────────────────────
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

			// ── Fly mode events ────────────────────────────────────────────────
			if (Input.IsActionJustPressed("fly_toggle"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "fly_toggle", Value = 1 });
			if (Input.IsActionPressed("fly_up"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "fly_up", Value = 1f });
			if (Input.IsActionPressed("fly_down"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "fly_down", Value = 1f });

			// ── Controller button events ───────────────────────────────────────
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

			float ltAxis = Input.GetJoyAxis(0, (JoyAxis)4); // LeftTrigger
			if (ltAxis > 0.15f)
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "run", Value = ltAxis });

			_prevJoyButtons.Clear();
			foreach (var kv in currentJoy)
				_prevJoyButtons[kv.Key] = kv.Value;

		

		root.Update((float)delta);

		List<IRenderable> renderables = root.GetAllRenderables();
		
			foreach (var r in renderables)
			{
				renderer.QueueItem(r);
			}
		
		renderer.step();
        //xm.Update();
        debug.Update((float)delta);
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
