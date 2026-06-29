using Godot;
using System;
using System.Collections.Generic;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;


namespace V12TwoDog
{
    public class XRTrackingService
    {
        public static bool InputDebug { get; set; } = true;

        private XROrigin3D _origin;
        private XRCamera3D _hmdCamera;
        private XRController3D _leftController;
        private XRController3D _rightController;
        private InputService _inputService;
        private OpenXRInterface _xrInterface;
        private const float Deadzone = 0.15f;

        private IWorldElement _cachedPlayer;
        private IWorldElement _cachedRoot;
        private IWorldElement _cachedHead;
        private IWorldElement _cachedLeftHand;
        private IWorldElement _cachedRightHand;
        private int _cacheTimer;

        public bool IsAvailable { get; private set; }
        public XROrigin3D Origin => _origin;

        public XRTrackingService(Node3D parent, InputService inputService)
        {
            _inputService = inputService;
            Initialize(parent);
        }

        private void Initialize(Node3D parent)
        {
            _xrInterface = XRServer.FindInterface("OpenXR") as OpenXRInterface;
            if (_xrInterface == null)
            {
                GD.Print("[XRTracking] OpenXR interface not found. XR unavailable.");
                IsAvailable = false;
                return;
            }

            XRServer.PrimaryInterface = _xrInterface;

            _xrInterface.Set("action_map", "res://openxr_action_map.tres");

            _origin = new XROrigin3D();
            _origin.Name = "XROrigin3D";
            parent.AddChild(_origin);

            _hmdCamera = new XRCamera3D();
            _hmdCamera.Name = "XR_HMD_Camera";
            _origin.AddChild(_hmdCamera);

            _leftController = new XRController3D();
            _leftController.Name = "XR_LeftController";
            _leftController.Tracker = "left_hand";
            _origin.AddChild(_leftController);

            _rightController = new XRController3D();
            _rightController.Name = "XR_RightController";
            _rightController.Tracker = "right_hand";
            _origin.AddChild(_rightController);

            if (_xrInterface.Initialize())
            {
                GD.Print("[XRTracking] OpenXR initialized successfully!");
                IsAvailable = true;
                parent.GetViewport().UseXR = true;
            }
            else
            {
                GD.Print("[XRTracking] Failed to initialize OpenXR.");
                Cleanup();
                IsAvailable = false;
            }
        }

        private void CacheTrackedElements()
        {
            _cacheTimer = 0;
            _cachedPlayer = null;
            _cachedRoot = null;
            _cachedHead = null;
            _cachedLeftHand = null;
            _cachedRightHand = null;

            var world = GameRoot.Instance?.SelectedWorld;
            if (world == null) return;

            foreach (var element in world.Root)
            {
                if (element.Name == "Player")
                {
                    _cachedPlayer = element;
                    break;
                }
            }

            if (_cachedPlayer == null) return;

            foreach (var child in _cachedPlayer.Children)
            {
                if (child.GetComponent<XRRootComponent>() != null)
                {
                    _cachedRoot = child;
                    break;
                }
            }

            if (_cachedRoot == null) return;

            foreach (var child in _cachedRoot.Children)
            {
                if (child.GetComponent<XRHeadComponent>() != null)
                    _cachedHead = child;
                else if (child.GetComponent<XRHandComponent>() is XRHandComponent hand)
                {
                    if (hand.Side == HandSide.Left)
                        _cachedLeftHand = child;
                    else
                        _cachedRightHand = child;
                }
            }
        }

        public void Update()
        {
            if (!IsAvailable) return;

            _cacheTimer++;
            if (_cacheTimer > 60)
                CacheTrackedElements();

            SyncOriginTransform();
            UpdateElementLocalPose(_hmdCamera, _cachedHead);
            UpdateElementLocalPose(_leftController, _cachedLeftHand);
            UpdateElementLocalPose(_rightController, _cachedRightHand);

            PollControllers();
        }

        private void SyncOriginTransform()
        {
            if (_cachedPlayer == null) return;

            var playerPos = _cachedPlayer.LocalTransform.Position;
            var playerRot = _cachedPlayer.LocalTransform.Rotation;

            float yaw = 0f;
            var playerComp = _cachedPlayer.GetComponent<V12.Basic.Components.PlayerComponent>();
            if (playerComp != null)
            {
                var field = typeof(V12.Basic.Components.PlayerComponent).GetField("_yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                    yaw = (float)(field.GetValue(playerComp) ?? 0f);
            }

            _origin.Position = new Vector3(playerPos.X, playerPos.Y, playerPos.Z);
            _origin.Quaternion = new global::Godot.Quaternion(Vector3.Up, yaw);
        }

        private void UpdateElementLocalPose(Node3D source, IWorldElement target)
        {
            if (source == null || target == null) return;

            var srcPos = source.Position;
            var lt = target.LocalTransform;
            lt.Position = new System.Numerics.Vector3(srcPos.X, srcPos.Y, srcPos.Z);

            var q = source.Transform.Basis.GetRotationQuaternion();
            lt.Rotation = new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W);
            target.LocalTransform = lt;
        }

        private void PollControllers()
        {
            if (_leftController != null)
                PollController(_leftController, "left");
            if (_rightController != null)
                PollController(_rightController, "right");
        }

        private void PollController(XRController3D ctrl, string side)
        {
            PollFloat(ctrl, side, "trigger");
            PollFloat(ctrl, side, "grip");
            PollButton(ctrl, side, "trigger_click");
            PollButton(ctrl, side, "grip_click");
            PollButton(ctrl, side, "ax_button");
            PollButton(ctrl, side, "by_button");
            PollButton(ctrl, side, "menu_button");
            PollButton(ctrl, side, "primary_click");
            PollVector2(ctrl, side, "primary");
        }

        private static string CtrlKey(string side, string name) => $"{side}:{name}";
        private readonly Dictionary<string, bool> _prevButtons = new();
        private readonly Dictionary<string, float> _prevFloats = new();

        private void PollFloat(XRController3D ctrl, string side, string name)
        {
            float val = ctrl.GetFloat(name);
            string key = CtrlKey(side, name);
            if (_prevFloats.TryGetValue(key, out float prev) && Math.Abs(val - prev) < 0.001f)
                return;
            _prevFloats[key] = val;

            if (InputDebug)
                GD.Print($"[XRInput] {side} analog {name} = {val:F3}");

            string prefix = $"xr_{side}_";
            if (name == "trigger")
                SendAxis(prefix + "trigger", val);
            else if (name == "grip")
                SendAxis(prefix + "grip", val);
        }

        private void PollButton(XRController3D ctrl, string side, string name)
        {
            bool now = ctrl.IsButtonPressed(name);
            string key = CtrlKey(side, name);
            bool prev = _prevButtons.TryGetValue(key, out bool p) && p;
            _prevButtons[key] = now;

            if (now == prev) return;

            if (InputDebug)
                GD.Print($"[XRInput] {side} button {(now ? "pressed" : "released")}: {name}");

            if (now)
                OnButtonPressed(side, name);
            else
                OnButtonReleased(side, name);
        }

        private void PollVector2(XRController3D ctrl, string side, string name)
        {
            if (name != "primary") return;
            Vector2 val = ctrl.GetVector2(name);
            if (val.X == 0f && val.Y == 0f) return;

            if (InputDebug)
                GD.Print($"[XRInput] {side} thumbstick raw=({val.X:F3}, {val.Y:F3})");

            string prefix = $"xr_{side}_";
            float x = Mathf.Abs(val.X) < Deadzone ? 0f : val.X;
            float y = Mathf.Abs(val.Y) < Deadzone ? 0f : val.Y;
            if (Mathf.Abs(x) > 0f)
                SendAxis(prefix + "thumbstick_x", x);
            if (Mathf.Abs(y) > 0f)
                SendAxis(prefix + "thumbstick_y", y);

            if (side == "left")
            {
                if (x > 0f) SendAxis("move_right", x);
                else if (x < 0f) SendAxis("move_left", -x);
                if (y < 0f) SendAxis("move_backward", -y);
                else if (y > 0f) SendAxis("move_forward", y);
            }
            else
            {
                if (x > 0f) SendAxis("look_right", x);
                else if (x < 0f) SendAxis("look_left", -x);
                if (y < 0f) SendAxis("look_up", -y);
                else if (y > 0f) SendAxis("look_down", y);
            }
        }

        private void OnButtonPressed(string side, string name)
        {
            if (InputDebug)
                GD.Print($"[XRInput] {side} button pressed: {name}");
            string prefix = $"xr_{side}_";

            switch (name)
            {
                case "trigger_click": SendButtonDown(prefix + "trigger_click"); break;
                case "grip_click":    SendButtonDown(prefix + "grip_click");    break;
                case "ax_button":     SendButtonDown(prefix + "primary");      break;
                case "by_button":     SendButtonDown(prefix + "secondary");    break;
                case "menu_button":   SendButtonDown(prefix + "menu");         break;
                case "primary_click": SendButtonDown(prefix + "thumbstick_click"); break;
            }

            if (side == "right")
            {
                switch (name)
                {
                    case "trigger_click": SendButtonDown("interact"); break;
                    case "ax_button":     SendButtonDown("jump");    break;
                    case "menu_button":   SendButtonDown("esc");     break;
                }
            }
        }

        private void OnButtonReleased(string side, string name)
        {
            if (InputDebug)
                GD.Print($"[XRInput] {side} button released: {name}");
            string prefix = $"xr_{side}_";

            switch (name)
            {
                case "trigger_click": SendButtonUp(prefix + "trigger_click"); break;
                case "grip_click":    SendButtonUp(prefix + "grip_click");    break;
                case "ax_button":     SendButtonUp(prefix + "primary");      break;
                case "by_button":     SendButtonUp(prefix + "secondary");    break;
                case "menu_button":   SendButtonUp(prefix + "menu");         break;
                case "primary_click": SendButtonUp(prefix + "thumbstick_click"); break;
            }

            if (side == "right")
            {
                switch (name)
                {
                    case "trigger_click": SendButtonUp("interact"); break;
                    case "ax_button":     SendButtonUp("jump");    break;
                    case "menu_button":   SendButtonUp("esc");     break;
                }
            }
        }

        private void SendAxis(string eventName, float val)
        {
            _inputService?.SendEvent(new V12.Core.Input.InputEvent
            {
                Type = InputEventType.Axis,
                Name = eventName,
                Value = val
            });
        }

        private void SendButtonDown(string eventName)
        {
            _inputService?.SendEvent(new V12.Core.Input.InputEvent
            {
                Type = InputEventType.ButtonDown,
                Name = eventName,
                Value = 1,
                IsPressed = true
            });
        }

        private void SendButtonUp(string eventName)
        {
            _inputService?.SendEvent(new V12.Core.Input.InputEvent
            {
                Type = InputEventType.ButtonUp,
                Name = eventName,
                Value = 0,
                IsPressed = false
            });
        }

        public void Cleanup()
        {
            if (_xrInterface != null)
            {
                try { _xrInterface.Uninitialize(); } catch { }
            }

            if (_origin != null && GodotObject.IsInstanceValid(_origin))
            {
                _origin.QueueFree();
                _origin = null;
            }

            IsAvailable = false;
        }
    }
}
