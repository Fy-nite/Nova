using Godot;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
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
    public override void _Ready()
	{
		V12.Core.Networking.BsonConfig.Initialize();
		root = new GameRoot();
		renderer = new V12TwoDog.Renderer(GetTree());
        Bootstrap = new Bootstrap();


        root.Registry.Register("IRenderer", renderer);
        var _input = root.Registry.Get<InputService>();
        if (_input == null )
        {
            _input = new InputService();

            root.Registry.Register("InputService", _input);
        }
        var debug = new DebugGameService();

        root.Registry.Register("DebugGameService", debug );
		root.Initialize();
		//root.V12Loop();
		root.CreateWorld("TestWorld","HotReload");
        var hotreloader = new WorldXmlSourceComponent("hotreload.xml", true);
        var elem = new Element{Components = { hotreloader }, Name="HotReloadElement"};
        root.SelectedWorld.AddElement(elem);
        xm = (WorldXmlHotReloader)root.Registry.Get("HotReloader_TestWorld").ServiceInstance;
        xm.Initialize();
        xm._watchers["D:\\git\\V12\\V12TwoDog\\V12TwoDog.Godot\\.godot\\mono\\temp\\bin\\Debug\\hotreload.xml"].Changed += (s, e) => debug._needsUpdate = true;

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

			// Clamp to [-1, 1]
			moveX = Mathf.Clamp(moveX, -1f, 1f);
			moveY = Mathf.Clamp(moveY, -1f, 1f);
			lookX = Mathf.Clamp(lookX, -1f, 1f);
			lookY = Mathf.Clamp(lookY, -1f, 1f);

          
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
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "esc", Value = 1 });
			if (Input.IsActionJustPressed("interact"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "interact", Value = 1 });
				
		

		root.Update((float)delta);


		foreach (var service in root.Registry.GetAll<IGameService>())
			service.Update((float)delta);

		List<IRenderable> renderables = root.GetAllRenderables();
		
			foreach (var r in renderables)
			{
				renderer.QueueItem(r);
			}
		
		renderer.step();
        xm.Update();
    }
}
