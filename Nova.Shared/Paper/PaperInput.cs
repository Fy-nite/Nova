using System;
using Godot;
using PPaper = global::Prowl.PaperUI.Paper;
using PMouse = global::Prowl.PaperUI.PaperMouseBtn;
using PKey = global::Prowl.PaperUI.PaperKey;

namespace V12TwoDog.Paper
{
    /// <summary>
    /// Forwards Godot input into a Paper UI instance.
    ///
    /// Paper is immediate-mode: it never subscribes to host events. The host pushes
    /// pointer position, button state, wheel and key state each event, and Paper
    /// resolves hover/click/focus on the next <c>BeginFrame</c>.
    /// Mirrors <c>Paper/Samples/OpenTK/PaperTKWindow.cs</c>
    /// (<c>SetPointerState</c> / <c>SetPointerWheel</c> / <c>SetKeyState</c>).
    ///
    /// Coordinates are assumed to be in Paper canvas pixels, i.e. the same space the
    /// canvas was created with. Paper canvases here always match the host window 1:1.
    /// </summary>
    public static class PaperInput
    {
        public static void Feed(PPaper paper, InputEvent evt)
        {
            if (paper == null) return;

            switch (evt)
            {
                case InputEventMouseMotion motion:
                    paper.SetPointerState(PMouse.Unknown, motion.Position.X, motion.Position.Y, false, true);
                    break;

                case InputEventMouseButton mb:
                    switch (mb.ButtonIndex)
                    {
                        case MouseButton.WheelUp:
                            paper.SetPointerWheel(1f);
                            paper.SetPointerPosition(mb.Position.X, mb.Position.Y);
                            break;
                        case MouseButton.WheelDown:
                            paper.SetPointerWheel(-1f);
                            paper.SetPointerPosition(mb.Position.X, mb.Position.Y);
                            break;
                        case MouseButton.WheelLeft:
                            paper.SetPointerWheelX(1f);
                            break;
                        case MouseButton.WheelRight:
                            paper.SetPointerWheelX(-1f);
                            break;
                        default:
                        {
                            var btn = mb.ButtonIndex switch
                            {
                                MouseButton.Left => PMouse.Left,
                                MouseButton.Right => PMouse.Right,
                                MouseButton.Middle => PMouse.Middle,
                                MouseButton.Xbutton1 => PMouse.Button4,
                                MouseButton.Xbutton2 => PMouse.Button5,
                                _ => PMouse.Unknown,
                            };
                            if (btn == PMouse.Unknown) break;
                            paper.SetPointerState(btn, mb.Position.X, mb.Position.Y, mb.Pressed, false);
                            break;
                        }
                    }
                    break;

                case InputEventKey key:
                {
                    var pk = MapKey(key.Keycode);
                    if (pk != PKey.Unknown)
                        paper.SetKeyState(pk, key.Pressed);

                    // Text entry: only when a Paper widget actually holds keyboard focus.
                    if (key.Pressed && !key.Echo && key.Unicode != 0)
                    {
                        try
                        {
                            if (paper.FocusedElementId != 0 || paper.WantsCaptureKeyboard)
                                paper.AddInputCharacter(char.ConvertFromUtf32((int)key.Unicode));
                        }
                        catch { }
                    }
                    break;
                }
            }
        }

        /// <summary>Push the current cursor position without a button event (hover tracking).</summary>
        public static void MovePointer(PPaper paper, Vector2 pos)
            => paper?.SetPointerState(PMouse.Unknown, pos.X, pos.Y, false, true);

        private static PKey MapKey(Key key)
        {
            // Letters/digits share ASCII codes with PaperKey's ordering (A..Z, Num1..Num0).
            if (key >= Key.A && key <= Key.Z) return (PKey)((int)PKey.A + (int)(key - Key.A));
            if (key >= Key.Key0 && key <= Key.Key9)
            {
                int d = (int)(key - Key.Key0);
                return (PKey)((int)PKey.Num1 + (d == 0 ? 9 : d - 1));
            }

            return key switch
            {
                Key.Enter or Key.KpEnter => PKey.Enter,
                Key.Escape => PKey.Escape,
                Key.Backspace => PKey.Backspace,
                Key.Tab => PKey.Tab,
                Key.Space => PKey.Space,
                Key.Minus => PKey.Minus,
                Key.Equal => PKey.Equals,
                Key.Bracketleft => PKey.LeftBracket,
                Key.Bracketright => PKey.RightBracket,
                Key.Backslash => PKey.Backslash,
                Key.Semicolon => PKey.Semicolon,
                Key.Apostrophe => PKey.Apostrophe,
                Key.Quoteleft => PKey.Grave,
                Key.Comma => PKey.Comma,
                Key.Period => PKey.Period,
                Key.Slash => PKey.Slash,
                Key.Left => PKey.Left,
                Key.Right => PKey.Right,
                Key.Up => PKey.Up,
                Key.Down => PKey.Down,
                Key.Home => PKey.Home,
                Key.End => PKey.End,
                Key.Pageup => PKey.PageUp,
                Key.Pagedown => PKey.PageDown,
                Key.Insert => PKey.Insert,
                Key.Delete => PKey.Delete,
                Key.Shift => PKey.LeftShift,
                Key.Ctrl => PKey.LeftControl,
                Key.Alt => PKey.LeftAlt,
                Key.Meta => PKey.LeftSuper,
                Key.F1 => PKey.F1,
                Key.F2 => PKey.F2,
                Key.F3 => PKey.F3,
                Key.F4 => PKey.F4,
                Key.F5 => PKey.F5,
                Key.F6 => PKey.F6,
                Key.F7 => PKey.F7,
                Key.F8 => PKey.F8,
                Key.F9 => PKey.F9,
                Key.F10 => PKey.F10,
                Key.F11 => PKey.F11,
                Key.F12 => PKey.F12,
                _ => PKey.Unknown,
            };
        }
    }
}

