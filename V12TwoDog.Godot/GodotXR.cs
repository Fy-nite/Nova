using Godot;
using System;
using System.Collections.Generic;
using Vec3 = System.Numerics.Vector3;
using Quat = System.Numerics.Quaternion;
using V12.Core.Input;

namespace V12TwoDog
{
	public class GodotXR
	{
		public static bool InputDebug { get; set; } = true;

		private XROrigin3D _origin;
		private XRCamera3D _hmdCamera;
		private XRController3D _leftController;
		private XRController3D _rightController;
		private V12.Core.Input.InputService _inputService;
		private VRInputProvider _vrInputProvider;
		private OpenXRInterface _xrInterface;
		private const float Deadzone = 0.15f;
	private int _poseLogCounter;
	private readonly Dictionary<string, bool> _prevButtons = new();
	private readonly Dictionary<string, float> _prevFloats = new();

		public bool IsAvailable { get; private set; }
		public XROrigin3D Origin => _origin;
		public VRInputProvider PoseProvider => _vrInputProvider;

		public GodotXR(Node3D parent, V12.Core.Input.InputService inputService)
		{
			_inputService = inputService;
			_vrInputProvider = new VRInputProvider();
            Console.WriteLine("Created node");
			Initialize(parent);
		}

		private void Initialize(Node3D parent)
		{
			_xrInterface = XRServer.FindInterface("OpenXR") as OpenXRInterface;
			if (_xrInterface == null)
			{
				GD.Print("[GodotXR] OpenXR interface not found. XR unavailable.");
				IsAvailable = false;
				return;
			}

			XRServer.PrimaryInterface = _xrInterface;

			// Load action map so XRController3D nodes receive tracked poses
			_xrInterface.Set("action_map", "res://openxr_action_map.tres");

			_origin = new XROrigin3D();
			_origin.Name = "XROrigin3D";
			parent.AddChild(_origin);

			_hmdCamera = new XRCamera3D();
			_hmdCamera.Name = "XR_HMD_Camera";
			_origin.AddChild(_hmdCamera);

			_leftController = new XRController3D();
			_leftController.Name = "XR_LeftController";
			_origin.AddChild(_leftController);
            _leftController.Tracker = "left_hand";
			_rightController = new XRController3D();
			_rightController.Name = "XR_RightController";
			_rightController.Tracker = "right_hand";
			_origin.AddChild(_rightController);

			if (_xrInterface.Initialize())
			{
				GD.Print("[GodotXR] OpenXR initialized successfully!");
				IsAvailable = true;
				parent.GetViewport().UseXR = true;
			}
			else
			{
				GD.Print("[GodotXR] Failed to initialize OpenXR.");
				Cleanup();
				IsAvailable = false;
			}
		}

		private static string CtrlKey(string side, string name) => $"{side}:{name}";

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

		private void PollFloat(XRController3D ctrl, string side, string name)
		{
			float val = ctrl.GetFloat(name);
			string key = CtrlKey(side, name);
			if (_prevFloats.TryGetValue(key, out float prev) && Math.Abs(val - prev) < 0.001f)
				return;
			_prevFloats[key] = val;

			if (InputDebug)
				GD.Print($"[XRInput] {side} analog {name} = {val:F3}");

			OnFloatChanged(side, name, val);
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

			OnVector2Changed(side, "primary", val);
		}

		public void Update()
		{
			if (!IsAvailable) return;

			UpdateHMDPose();
			UpdateControllerPose(_leftController, true);
			UpdateControllerPose(_rightController, false);
			PollControllers();

			if (InputDebug)
			{
				_poseLogCounter++;
				if (_poseLogCounter % 300 == 0)
				{
					GD.Print($"[XRInput] head pos=({_vrInputProvider.HeadPosition.X:F2},{_vrInputProvider.HeadPosition.Y:F2},{_vrInputProvider.HeadPosition.Z:F2})  left={_leftController?.Position ?? Vector3.Zero}  right={_rightController?.Position ?? Vector3.Zero}");
				}
			}
		}

		private void UpdateHMDPose()
		{
			if (_hmdCamera == null) return;
			var originPos = _origin.GlobalPosition;
			var originBasis = _origin.GlobalTransform.Basis;
			var originQuat = originBasis.GetRotationQuaternion();

			var gPos = originPos + originBasis * _hmdCamera.Position;
			var gRot = originQuat * _hmdCamera.Quaternion;

			_vrInputProvider.SetHeadPose(
				new Vec3(gPos.X, gPos.Y, -gPos.Z),
				new Quat(gRot.X, gRot.Y, -gRot.Z, gRot.W));
		}

		private void UpdateControllerPose(XRController3D controller, bool isLeft)
		{
			if (controller == null) return;
			var originPos = _origin.GlobalPosition;
			var originBasis = _origin.GlobalTransform.Basis;
			var originQuat = originBasis.GetRotationQuaternion();

			var gPos = originPos + originBasis * controller.Position;
			var gRot = originQuat * controller.Quaternion;

			var vpos = new Vec3(gPos.X, gPos.Y, -gPos.Z);
			var vrot = new Quat(gRot.X, gRot.Y, -gRot.Z, gRot.W);

			if (isLeft)
				_vrInputProvider.SetLeftHandPose(vpos, vrot);
			else
				_vrInputProvider.SetRightHandPose(vpos, vrot);
		}

		private void OnFloatChanged(string side, string name, double value)
		{
			if (InputDebug)
				GD.Print($"[XRInput] {side} analog {name} = {value:F3}");

			string prefix = $"xr_{side}_";

			if (name == "trigger")
				SendAxis(prefix + "trigger", (float)value);
			else if (name == "grip")
				SendAxis(prefix + "grip", (float)value);
		}

		private void OnVector2Changed(string side, string name, Vector2 value)
		{
			if (name != "primary") return;

			if (InputDebug)
				GD.Print($"[XRInput] {side} thumbstick raw=({value.X:F3}, {value.Y:F3})");

			string prefix = $"xr_{side}_";
			float x = Mathf.Abs(value.X) < Deadzone ? 0f : value.X;
			float y = Mathf.Abs(value.Y) < Deadzone ? 0f : value.Y;
			if (Mathf.Abs(x) > 0f)
				SendAxis(prefix + "thumbstick_x", x);
			if (Mathf.Abs(y) > 0f)
				SendAxis(prefix + "thumbstick_y", y);

			if (side == "left")
			{
				if (x > 0f) SendAxis("move_right", x);
				else if (x < 0f) SendAxis("move_left", -x);

            if (y < 0f) SendAxis("move_forward", -y);
            else if (y > 0f) SendAxis("move_backward", y);
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
				Type = V12.Core.Input.InputEventType.Axis,
				Name = eventName,
				Value = val
			});
		}

		private void SendButtonDown(string eventName)
		{
			_inputService?.SendEvent(new V12.Core.Input.InputEvent
			{
				Type = V12.Core.Input.InputEventType.ButtonDown,
				Name = eventName,
				Value = 1,
				IsPressed = true
			});
		}

		private void SendButtonUp(string eventName)
		{
			_inputService?.SendEvent(new V12.Core.Input.InputEvent
			{
				Type = V12.Core.Input.InputEventType.ButtonUp,
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
