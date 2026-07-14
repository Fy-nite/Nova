using Godot;

using System.Collections.Generic;
using V12.Basic.Components;
using V12.Core;
using V12.Core.Input;

/// <summary>
/// Translates Godot keyboard, mouse, and gamepad input into V12 InputService events.
/// </summary>
public class InputHandler
{
    private readonly Dictionary<JoyButton, bool> _prevJoyButtons = new();
    private bool _f10WasPressed;
    private Vector2 _accumulatedMouseLook;

    public bool MouseCaptured { get; set; }
    public bool DebugMode { get; set; }

    public void AccumulateMouseLook(Vector2 delta)
    {
        _accumulatedMouseLook += delta;
    }

    /// <summary>
    /// Process all input for the current physics frame.
    /// Returns true if escape was pressed (caller should toggle mouse capture).
    /// </summary>
    public bool ProcessFrame(InputService inputService, bool xrAvailable, GameRoot root)
    {
        if (inputService == null) return false;

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
        if (!xrAvailable && MouseCaptured && _accumulatedMouseLook != Vector2.Zero)
        {
            var player = root.Player;
            if (player != null)
            {
                var pc = player.GetComponent<PlayerComponent>();
                if (pc != null)
                    pc.AddMouseDelta(_accumulatedMouseLook.X, _accumulatedMouseLook.Y);
            }
            _accumulatedMouseLook = Vector2.Zero;
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
        if (Input.IsActionJustPressed("interact"))
            inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "interact", Value = 1 });
        if (Input.IsActionJustReleased("interact"))
            inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "interact", Value = 0 });

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

        // ── Escape: send event, caller toggles mouse capture ──
        bool escapePressed = false;
        if (Input.IsActionJustPressed("escape"))
        {
            inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "esc", Value = 1 });
            escapePressed = true;
        }

        // ── F10: toggle debug logging ──
        if (Input.IsKeyPressed(Key.F10))
        {
            if (!_f10WasPressed)
            {
                DebugMode = !DebugMode;
                GD.Print($"[Debug] Debug mode: {(DebugMode ? "ON" : "OFF")}");
                _f10WasPressed = true;
            }
        }
        else
        {
            _f10WasPressed = false;
        }

        return escapePressed;
    }
}
