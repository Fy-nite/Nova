using Godot;
using System;
using Vec3 = System.Numerics.Vector3;
using Quat = System.Numerics.Quaternion;
using V12.Core.Input;

namespace V12TwoDog
{
	public class GodotXR
	{
		private XROrigin3D _origin;
		private XRCamera3D _hmdCamera;
		private XRController3D _leftController;
		private XRController3D _rightController;
		private V12.Core.Input.InputService _inputService;
		private VRInputProvider _vrInputProvider;
		private OpenXRInterface _xrInterface;
		private const float Deadzone = 0.15f;

		public bool IsAvailable { get; private set; }
		public XROrigin3D Origin => _origin;

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

			_origin = new XROrigin3D();
			_origin.Name = "XROrigin3D";
			parent.AddChild(_origin);

			_hmdCamera = new XRCamera3D();
			_hmdCamera.Name = "XR_HMD_Camera";
			_origin.AddChild(_hmdCamera);

			_leftController = new XRController3D();
			_leftController.Name = "XR_LeftController";
			_origin.AddChild(_leftController);

			_rightController = new XRController3D();
			_rightController.Name = "XR_RightController";
			_origin.AddChild(_rightController);

			if (_xrInterface.Initialize())
			{
				GD.Print("[GodotXR] OpenXR initialized successfully!");
				IsAvailable = true;
				parent.GetViewport().UseXR = true;
				ConnectControllerSignals();
			}
			else
			{
				GD.Print("[GodotXR] Failed to initialize OpenXR.");
				Cleanup();
				IsAvailable = false;
			}
		}

		private void ConnectControllerSignals()
		{
			if (_leftController != null)
			{
				_leftController.ButtonPressed += (name) => OnButtonPressed("left", name);
				_leftController.ButtonReleased += (name) => OnButtonReleased("left", name);
				_leftController.InputFloatChanged += (name, value) => OnFloatChanged("left", name, value);
				_leftController.InputVector2Changed += (name, value) => OnVector2Changed("left", name, value);
			}
			if (_rightController != null)
			{
				_rightController.ButtonPressed += (name) => OnButtonPressed("right", name);
				_rightController.ButtonReleased += (name) => OnButtonReleased("right", name);
				_rightController.InputFloatChanged += (name, value) => OnFloatChanged("right", name, value);
				_rightController.InputVector2Changed += (name, value) => OnVector2Changed("right", name, value);
			}
		}

		public void Update()
		{
			if (!IsAvailable) return;

			UpdateHMDPose();
			UpdateControllerPose(_leftController, true);
			UpdateControllerPose(_rightController, false);
		}

		private void UpdateHMDPose()
		{
			if (_hmdCamera == null) return;
			var pos = _hmdCamera.Position;
			var rot = _hmdCamera.Quaternion;
			_vrInputProvider.HeadPosition = new Vec3(pos.X, pos.Y, pos.Z);
			_vrInputProvider.HeadOrientation = new Quat(rot.X, rot.Y, rot.Z, rot.W);
		}

		private void UpdateControllerPose(XRController3D controller, bool isLeft)
		{
			if (controller == null) return;
			var pos = controller.Position;
			var rot = controller.Quaternion;
			if (isLeft)
			{
				_vrInputProvider.LeftHandPosition = new Vec3(pos.X, pos.Y, pos.Z);
				_vrInputProvider.LeftHandOrientation = new Quat(rot.X, rot.Y, rot.Z, rot.W);
			}
			else
			{
				_vrInputProvider.RightHandPosition = new Vec3(pos.X, pos.Y, pos.Z);
				_vrInputProvider.RightHandOrientation = new Quat(rot.X, rot.Y, rot.Z, rot.W);
			}
		}

		private void OnFloatChanged(string side, string name, double value)
		{
			string prefix = $"xr_{side}_";

			if (name == "trigger")
				SendAxis(prefix + "trigger", (float)value);
			else if (name == "grip")
				SendAxis(prefix + "grip", (float)value);
		}

		private void OnVector2Changed(string side, string name, Vector2 value)
		{
			if (name != "primary") return;

			string prefix = $"xr_{side}_";
			float x = Mathf.Abs(value.X) < Deadzone ? 0f : value.X;
			float y = Mathf.Abs(value.Y) < Deadzone ? 0f : value.Y;
            Console.WriteLine($"{prefix} {x} {y}");
			if (Mathf.Abs(x) > 0f)
				SendAxis(prefix + "thumbstick_x", x);
			if (Mathf.Abs(y) > 0f)
				SendAxis(prefix + "thumbstick_y", y);

			if (side == "left")
			{
				if (x > 0f) SendAxis("move_right", x);
				else if (x < 0f) SendAxis("move_left", -x);

				if (y > 0f) SendAxis("move_forward", y);
				else if (y < 0f) SendAxis("move_backward", -y);
			}
			else
			{
				if (x > 0f) SendAxis("look_right", x);
				else if (x < 0f) SendAxis("look_left", -x);

				if (y > 0f) SendAxis("look_up", y);
				else if (y < 0f) SendAxis("look_down", -y);
			}
		}

		private void OnButtonPressed(string side, string name)
		{
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
