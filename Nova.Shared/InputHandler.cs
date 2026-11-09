using Godot;

using System.Collections.Generic;
using V12.Basic.Components;
using V12.Core;
using V12.Core.Diagnostics;
using V12.Core.Input;

/// <summary>
/// Translates Godot keyboard, mouse, and gamepad input into V12 InputService events.
/// </summary>
public class InputHandler
{
    private readonly Dictionary<JoyButton, bool> _prevJoyButtons = new();
    private bool _f10WasPressed;
    private Vector2 _accumulatedMouseLook;

    /// <summary>Physical keys held last frame, for raw key-edge forwarding.</summary>
    private readonly HashSet<InputKey> _prevKeys = new();
    private readonly HashSet<InputKey> _currentKeys = new();

    /// <summary>
    /// Godot key → engine <see cref="InputKey"/> map. Built once; covers the keys
    /// shared by both enums. Godot's enum has no left/right distinction for
    /// modifiers, so Shift/Ctrl/Alt/Meta map to their left variants.
    /// </summary>
    private static readonly (Key Godot, InputKey V12)[] RawKeyMap = BuildRawKeyMap();

    private static (Key Godot, InputKey V12)[] BuildRawKeyMap()
    {
        var map = new List<(Key, InputKey)>();
        void Add(Key godot, InputKey v12) => map.Add((godot, v12));

        for (char c = 'A'; c <= 'Z'; c++)
            Add(Enum.Parse<Key>(c.ToString()), Enum.Parse<InputKey>(c.ToString()));

        for (int i = 0; i <= 9; i++)
            Add(Enum.Parse<Key>("Key" + i), Enum.Parse<InputKey>("D" + i));

        for (int i = 1; i <= 24; i++)
            Add(Enum.Parse<Key>("F" + i), Enum.Parse<InputKey>("F" + i));

        for (int i = 0; i <= 9; i++)
            Add(Enum.Parse<Key>("Kp" + i), Enum.Parse<InputKey>("NumPad" + i));

        Add(Key.KpAdd, InputKey.Add);
        Add(Key.KpSubtract, InputKey.Subtract);
        Add(Key.KpMultiply, InputKey.Multiply);
        Add(Key.KpDivide, InputKey.Divide);
        Add(Key.KpPeriod, InputKey.Decimal);
        Add(Key.Numlock, InputKey.NumLock);

        Add(Key.Space, InputKey.Space);
        Add(Key.Enter, InputKey.Enter);
        Add(Key.Escape, InputKey.Escape);
        Add(Key.Tab, InputKey.Tab);
        Add(Key.Backspace, InputKey.Back);
        Add(Key.Capslock, InputKey.CapsLock);
        Add(Key.Delete, InputKey.Delete);
        Add(Key.Insert, InputKey.Insert);

        Add(Key.Home, InputKey.Home);
        Add(Key.End, InputKey.End);
        Add(Key.Pageup, InputKey.PageUp);
        Add(Key.Pagedown, InputKey.PageDown);
        Add(Key.Up, InputKey.Up);
        Add(Key.Down, InputKey.Down);
        Add(Key.Left, InputKey.Left);
        Add(Key.Right, InputKey.Right);

        Add(Key.Shift, InputKey.LeftShift);
        Add(Key.Ctrl, InputKey.LeftControl);
        Add(Key.Alt, InputKey.LeftAlt);
        Add(Key.Meta, InputKey.LeftWindows);

        Add(Key.Print, InputKey.PrintScreen);
        Add(Key.Scrolllock, InputKey.Scroll);
        Add(Key.Pause, InputKey.Pause);

        Add(Key.Semicolon, InputKey.OemSemicolon);
        Add(Key.Equal, InputKey.OemPlus);
        Add(Key.Comma, InputKey.OemComma);
        Add(Key.Minus, InputKey.OemMinus);
        Add(Key.Period, InputKey.OemPeriod);
        Add(Key.Slash, InputKey.OemQuestion);
        Add(Key.Quoteleft, InputKey.OemTilde);
        Add(Key.Bracketleft, InputKey.OemOpenBrackets);
        Add(Key.Backslash, InputKey.OemPipe);
        Add(Key.Bracketright, InputKey.OemCloseBrackets);
        Add(Key.Apostrophe, InputKey.OemQuotes);

        return map.ToArray();
    }

    public bool MouseCaptured { get; set; }
    public bool DebugMode { get; set; }

    public void AccumulateMouseLook(Vector2 delta)
    {
        _accumulatedMouseLook += delta;
    }

    /// <summary>
    /// Forwards physical key edges so handlers and <see cref="InputActionMap"/> key bindings
    /// can react to any mapped key, independent of the named actions above. Events carry both
    /// <see cref="V12.Core.Input.InputEvent.Key"/> and the <see cref="InputKey"/> name.
    /// </summary>
    private void ForwardRawKeys(InputService inputService)
    {
        if (!DisplayServer.WindowIsFocused())
        {
            _prevKeys.Clear();
            return;
        }

        _currentKeys.Clear();
        foreach (var (godotKey, v12Key) in RawKeyMap)
        {
            if (!Input.IsKeyPressed(godotKey)) continue;
            _currentKeys.Add(v12Key);

            if (_prevKeys.Contains(v12Key)) continue;
            inputService.SendEvent(new V12.Core.Input.InputEvent
            {
                Type = InputEventType.ButtonDown,
                Name = v12Key.ToString(),
                Key = v12Key,
            });
        }

        foreach (var v12Key in _prevKeys)
        {
            if (_currentKeys.Contains(v12Key)) continue;
            inputService.SendEvent(new V12.Core.Input.InputEvent
            {
                Type = InputEventType.ButtonUp,
                Name = v12Key.ToString(),
                Key = v12Key,
            });
        }

        _prevKeys.Clear();
        foreach (var v12Key in _currentKeys) _prevKeys.Add(v12Key);
    }

    /// <summary>
    /// Process all input for the current physics frame.
    /// Returns true if escape was pressed (caller should toggle mouse capture).
    /// </summary>
    public bool ProcessFrame(InputService inputService, bool xrAvailable, GameRoot root)
    {
        using var _zone = V12Profiler.BeginZone("InputHandler.ProcessFrame", V12Profiler.Colors.Runtime);
        if (inputService == null) return false;

        ForwardRawKeys(inputService);

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

        // ── Mouse look ──
        // Feed the built-in PlayerComponent and also publish the raw deltas as
        // events so custom controllers can read them without depending on it.
        if (!xrAvailable && MouseCaptured && _accumulatedMouseLook != Vector2.Zero)
        {
            var player = root.Player;
            if (player != null)
            {
                var pc = player.GetComponent<PlayerComponent>();
                if (pc != null)
                    pc.AddMouseDelta(_accumulatedMouseLook.X, _accumulatedMouseLook.Y);
            }

            inputService.SendEvent(new V12.Core.Input.InputEvent
            {
                Type = V12.Core.Input.InputEventType.Axis,
                Name = "mouse_dx",
                Value = _accumulatedMouseLook.X,
            });
            inputService.SendEvent(new V12.Core.Input.InputEvent
            {
                Type = V12.Core.Input.InputEventType.Axis,
                Name = "mouse_dy",
                Value = _accumulatedMouseLook.Y,
            });

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
