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
        // Optical hand-tracking fallbacks (no controllers). Bound to the
        // OpenXR hand tracker trackers so hand poses work even when the user
        // isn't holding controllers.
        private XRNode3D? _leftHandTracker;
        private XRNode3D? _rightHandTracker;
        private InputService _inputService;
        private OpenXRInterface _xrInterface;
        private Renderer _renderer;
        private const float Deadzone = 0.15f;

        private IWorldElement _cachedPlayer;
        private IWorldElement _cachedRoot;
        private IWorldElement _cachedHead;
        private IWorldElement _cachedLeftHand;
        private IWorldElement _cachedRightHand;
        private int _cacheTimer;

        public bool IsAvailable { get; private set; }
        public XROrigin3D Origin => _origin;

        /// <summary>
        /// The currently-tracked node for each hand (controller when held, else the
        /// optical hand tracker). Used by visuals (e.g. the pickup laser) so they
        /// can be parented to / drawn from the hand's real position instead of a
        /// stale snapshot transform.
        /// </summary>
        public Node3D? LeftHandSource => PickHandSource(_leftController, _leftHandTracker);
        public Node3D? RightHandSource => PickHandSource(_rightController, _rightHandTracker);

        /// <summary>Latest left/right trigger and grip values (0..1), read by network sync.</summary>
        public float LeftTrigger { get; private set; }
        public float LeftGrip { get; private set; }
        public float RightTrigger { get; private set; }
        public float RightGrip { get; private set; }

        public XRTrackingService(Node3D parent, InputService inputService, Renderer renderer)
        {
            _inputService = inputService;
            _renderer = renderer;
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
            // Default pose: every runtime provides it. (Some runtimes do not
            // expose the "grip" pose, which made GetIsActive() false and froze
            // the hands.)
            _origin.AddChild(_leftController);

            _rightController = new XRController3D();
            _rightController.Name = "XR_RightController";
            _rightController.Tracker = "right_hand";
            _origin.AddChild(_rightController);

            // Optical hand tracking fallback: when the user isn't holding
            // controllers, the controller trackers stay idle — the hand poses
            // come from the OpenXR hand_tracking extension instead.
            _leftHandTracker = new XRNode3D();
            _leftHandTracker.Name = "XR_LeftHandTracker";
            _leftHandTracker.Tracker = "/user/hand_tracker/left";
            _leftHandTracker.Pose = "default";
            _leftHandTracker.ShowWhenTracked = true;
            _origin.AddChild(_leftHandTracker);

            _rightHandTracker = new XRNode3D();
            _rightHandTracker.Name = "XR_RightHandTracker";
            _rightHandTracker.Tracker = "/user/hand_tracker/right";
            _rightHandTracker.Pose = "default";
            _rightHandTracker.ShowWhenTracked = true;
            _origin.AddChild(_rightHandTracker);

            if (_xrInterface.Initialize())
            {
                GD.Print("[XRTracking] OpenXR initialized successfully!");
                IsAvailable = true;
                parent.GetViewport().UseXR = true;
            }
            else
            {
                // Keep the origin/camera/controller nodes: they stay inert until
                // OpenXR comes up. Update() retries periodically, so plugging a
                // headset in a few seconds after boot still enables VR.
                GD.Print("[XRTracking] OpenXR not ready yet — will keep retrying while the runtime runs.");
                IsAvailable = false;
            }
        }

        private int _initRetryCounter;
        private const int InitRetryIntervalFrames = 60;  // ~1s at 60fps
        private const int InitRetryMaxAttempts = 300;    // ~5 min of retries, then give up

        private void TryDelayedInitialize()
        {
            if (_xrInterface == null || IsAvailable) return;
            if (++_initRetryCounter % InitRetryIntervalFrames != 0) return;
            if (_initRetryCounter / InitRetryIntervalFrames > InitRetryMaxAttempts) return;

            try
            {
                if (_xrInterface.Initialize())
                {
                    IsAvailable = true;
                    if (_origin != null && GodotObject.IsInstanceValid(_origin) && _origin.GetViewport() is { } vp)
                        vp.UseXR = true;

                    // Register with the GameRoot even though we weren't available
                    // at boot, so gamepaks/network code that looks up the service
                    // later (e.g. XR rig sync) can still find it.
                    try
                    {
                        if (GameRoot.Instance != null && GameRoot.Instance.Registry.Get("XRTrackingService") == null)
                            GameRoot.Instance.Registry.Register("XRTrackingService", this);
                    }
                    catch { }

                    GD.Print("[XRTracking] OpenXR initialized successfully (delayed).");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[XRTracking] Delayed OpenXR init failed: {ex.Message}");
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

            // Use ECS query API — searches all active worlds (PersistentWorld + SelectedWorld)
            _cachedPlayer = GameRoot.Instance?.FindElement(e => e.Name == "Player");

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
            if (!IsAvailable)
            {
                TryDelayedInitialize();
                return;
            }

            // Cache immediately on the first frame (and whenever the cache is
            // empty) instead of waiting 60 frames — otherwise the hands sit at
            // the origin for a full second after the player spawns.
            _cacheTimer++;
            if (_cacheTimer > 60 || _cachedPlayer == null)
                CacheTrackedElements();

            SyncOriginTransform();
            UpdateElementLocalPose(_hmdCamera, _cachedHead);
            UpdateElementLocalPose(PickHandSource(_leftController, _leftHandTracker), _cachedLeftHand);
            UpdateElementLocalPose(PickHandSource(_rightController, _rightHandTracker), _cachedRightHand);

            RegisterDirectParents();

            PollControllers();

            // ── Periodic diagnostic (wall-clock, ~every 2s) so we can see what
            // the runtime reports — physics tick rate may be very high so a
            // frame counter would flood the console. ──
            ulong now = Time.GetTicksMsec();
            if (now - _lastDiagMs >= 2000)
            {
                _lastDiagMs = now;
                GD.Print($"[XRDiag] avail={IsAvailable} " +
                         $"L:ctrl(active={SafeActive(_leftController)},data={SafeData(_leftController)}) " +
                         $"hand(active={SafeActive(_leftHandTracker)},data={SafeData(_leftHandTracker)}) " +
                         $"R:ctrl(active={SafeActive(_rightController)},data={SafeData(_rightController)}) " +
                         $"hand(active={SafeActive(_rightHandTracker)},data={SafeData(_rightHandTracker)})");
            }
        }

        private static bool SafeActive(XRNode3D? node) => node != null && GodotObject.IsInstanceValid(node) && node.GetIsActive();
        private static bool SafeData(XRNode3D? node) => node != null && GodotObject.IsInstanceValid(node) && node.GetHasTrackingData();
        private ulong _lastDiagMs;

        /// <summary>
        /// Prefer whichever source has tracking data for this hand: the
        /// controller when it is tracked (user holds it), else the optical hand
        /// tracker (bare-hand tracking). Returns null when neither has data so
        /// the element keeps its last pose instead of snapping to the origin.
        /// </summary>
        private static Node3D? PickHandSource(XRController3D? controller, XRNode3D? handTracker)
        {
            bool ctrlOk = controller != null && GodotObject.IsInstanceValid(controller)
                && (controller.GetIsActive() || controller.GetHasTrackingData());
            bool handOk = handTracker != null && GodotObject.IsInstanceValid(handTracker)
                && (handTracker.GetIsActive() || handTracker.GetHasTrackingData());

            if (ctrlOk) return controller;
            if (handOk) return handTracker;

            // Nothing tracked right now — say so rather than writing origin
            // over the current pose.
            return null;
        }

        /// <summary>
        /// Pin the local player's head/hand render nodes to the actual tracked
        /// controller/hand nodes so the visual boxes sit exactly on the hands with
        /// zero latency (the snapshot + interpolation pipeline lags by a frame or
        /// two otherwise). Passing null as the source releases the pin, falling
        /// back to snapshot rendering at the last written pose.
        /// </summary>
        private void RegisterDirectParents()
        {
            if (_cachedHead != null)
                _renderer.SetDirectParent(_cachedHead.Id, _hmdCamera);
            if (_cachedLeftHand != null)
                _renderer.SetDirectParent(_cachedLeftHand.Id, LeftHandSource);
            if (_cachedRightHand != null)
                _renderer.SetDirectParent(_cachedRightHand.Id, RightHandSource);
        }

        private void SyncOriginTransform()
        {
            if (_cachedPlayer == null) return;

            var playerPos = _cachedPlayer.LocalTransform.Position;
            var playerRot = _cachedPlayer.LocalTransform.Rotation;

            // XR player rotation is identity, so the origin carries no yaw and
            // the HMD alone is the camera. For a desktop player that later gains
            // XR, mirroring the element rotation keeps the rig facing the same
            // way the desktop camera does. (Previously this reflected the private
            // _yaw field, which in XR mode was driven by the right thumbstick and
            // rotated the whole world around the player.)
            if (playerRot.X == 0f && playerRot.Y == 0f && playerRot.Z == 0f && playerRot.W == 0f)
                playerRot = System.Numerics.Quaternion.Identity;

            _origin.Position = new Vector3(playerPos.X, playerPos.Y, playerPos.Z);
            _origin.Quaternion = new global::Godot.Quaternion(playerRot.X, playerRot.Y, playerRot.Z, playerRot.W);
        }

        private void UpdateElementLocalPose(Node3D? source, IWorldElement target)
        {
            if (source == null || target == null) return;

            var srcPos = source.Position;
            var lt = target.LocalTransform;
            lt.Position = new System.Numerics.Vector3(srcPos.X, srcPos.Y, srcPos.Z);

            var basis = source.Transform.Basis;
            if (basis.Row0 != Vector3.Zero || basis.Row1 != Vector3.Zero || basis.Row2 != Vector3.Zero)
            {
                var q = basis.GetRotationQuaternion();
                lt.Rotation = new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W);
            }
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
            {
                if (side == "left") LeftTrigger = val;
                else RightTrigger = val;
                SendAxis(prefix + "trigger", val);
            }
            else if (name == "grip")
            {
                if (side == "left") LeftGrip = val;
                else RightGrip = val;
                SendAxis(prefix + "grip", val);
            }
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
            // Release direct-parent pins so the renderer falls back to snapshot
            // rendering (or frees cleanly) after XR shuts down.
            if (_renderer != null)
            {
                if (_cachedHead != null) _renderer.SetDirectParent(_cachedHead.Id, null);
                if (_cachedLeftHand != null) _renderer.SetDirectParent(_cachedLeftHand.Id, null);
                if (_cachedRightHand != null) _renderer.SetDirectParent(_cachedRightHand.Id, null);
            }

            // Only uninitialize if we actually got OpenXR up — otherwise the
            // native side complains about a null openxr_api on shutdown.
            if (_xrInterface != null && IsAvailable)
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
