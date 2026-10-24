using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Godot;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Rendering;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace V12TwoDog.Godot
{
    /// <summary>
    /// Nova/Godot adapter for the Studio viewport interaction: a thin bridge that
    /// exposes renderer state through the three renderer-agnostic contracts the
    /// core <c>ViewportInteractionService</c> consumes —
    /// <see cref="IViewportRegistry"/> (viewport rects + camera matrices),
    /// <see cref="IInputSource"/> (per-frame mouse/keyboard snapshot) and
    /// <see cref="IDirectTransformProvider"/> (latency-free transform writes).
    /// All orbit / fly / picking / gizmo logic lives in Studio.Core; this class
    /// only captures Godot state and applies queued transforms back onto nodes.
    ///
    /// Picking is engine-side now (<c>SceneQuery</c> walks mesh geometry), so the
    /// previous mesh readback / RID cache / pick-membership machinery is gone.
    ///
    /// Threading: the service ticks on the V12 worker thread, but Godot nodes
    /// may only be touched from the main thread. ALL Godot access happens in
    /// <see cref="PollInput"/> (viewport + input capture) and
    /// <see cref="ApplyEditorDrivenTransforms"/> (transform pump), both called
    /// from the host's <c>_Process</c> / <c>_PhysicsProcess</c>. The
    /// service-facing readers consume immutable per-frame snapshots (a swapped
    /// list, a swapped struct, concurrent dictionaries), so no lock is needed.
    /// </summary>
    public sealed class GodotViewportAdapter : IViewportRegistry, IInputSource, IDirectTransformProvider
    {
        private readonly Renderer _renderer;

        /// <summary>Latest per-frame viewport snapshot (main-thread swap, worker
        /// read). Immutable once published: never mutated in place.</summary>
        private volatile List<ViewportInfo>? _viewports;
        private static readonly IReadOnlyList<ViewportInfo> EmptyViewports = Array.Empty<ViewportInfo>();

        /// <summary>Latest input snapshot (main-thread write, worker read).</summary>
        private InputSnapshot _snapshot;

        /// <summary>Absolute wheel accumulator (notch × 120, scroll-up positive).</summary>
        private int _wheel;

        private readonly ConcurrentDictionary<IWorldElement, Matrix4x4> _pendingTransforms = new(ReferenceEqualityComparer.Instance);
        private readonly ConcurrentDictionary<IWorldElement, byte> _editorDriven = new(ReferenceEqualityComparer.Instance);
        private readonly ConcurrentDictionary<IWorldElement, byte> _pendingReleases = new(ReferenceEqualityComparer.Instance);

        /// <summary>First visible viewport container seen this frame
        /// (main-thread only); defines the window the mouse is read in.</summary>
        private Control? _primaryContainer;

        /// <summary>Host-supplied proxy used when a SubViewport is drawn by the UI
        /// instead of a SubViewportContainer (Paper editor).</summary>
        private Control? _viewportProxy;

        /// <summary>Register the invisible Control that carries the UI-drawn viewport rect.</summary>
        public void SetViewportProxy(Control? proxy) => _viewportProxy = proxy;

        public GodotViewportAdapter(Renderer renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        // ── IViewportRegistry (worker thread: reads the published snapshot) ─

        public IReadOnlyList<ViewportInfo> Viewports
        {
            get
            {
                var vps = _viewports;
                return vps ?? EmptyViewports;
            }
        }

        // ── IInputSource (worker thread: reads the latest snapshot) ─────────

        public InputSnapshot GetSnapshot() => _snapshot;

        // ── IDirectTransformProvider (worker thread: queue only) ────────────

        public void SetElementTransformDirect(IWorldElement element, Matrix4x4 localTransform)
        {
            _editorDriven[element] = 0;
            _pendingTransforms[element] = localTransform;
        }

        public void ReleaseElementTransformDirect(IWorldElement element)
        {
            _editorDriven.TryRemove(element, out _);
            _pendingTransforms.TryRemove(element, out _);
            _pendingReleases[element] = 0;
        }

        // ── Main-thread capture (host calls from _Process, every frame) ─────

        public void PollInput()
        {
            // Viewports first: the mouse is read relative to the window that
            // hosts them, so the first visible container must be known.
            CaptureViewports();
            CaptureInput();
        }

        /// <summary>Accumulate a wheel notch (main thread, from _Input events).</summary>
        public void AccumulateWheel(int delta) => _wheel += delta;

        /// <summary>Apply the worker's queued direct transforms onto Godot nodes
        /// and clear pending releases. Main thread only.</summary>
        public void ApplyEditorDrivenTransforms()
        {
            foreach (var element in _pendingReleases.Keys)
            {
                if (!_pendingReleases.TryRemove(element, out _)) continue;
                _renderer.SetEditorDriven(element.Id, false);
            }
            foreach (var (element, transform) in _pendingTransforms)
            {
                if (!_pendingTransforms.TryRemove(element, out var latest)) continue;
                if (!_editorDriven.ContainsKey(element)) continue;
                var node = _renderer.GetNode(element.Id);
                if (node == null) continue;
                if (!GodotObject.IsInstanceValid(node)) continue;
                _renderer.SetEditorDriven(element.Id, true);
                node.Transform = ToGodotTransform(latest);
            }
        }

        private void CaptureViewports()
        {
            var list = new List<ViewportInfo>();
            _primaryContainer = null;

            foreach (var (id, vp) in _renderer.SceneViewports)
            {
                if (!GodotObject.IsInstanceValid(vp)) continue;
                Control? container = FindContainer(vp);
                if (container == null && _viewportProxy != null && GodotObject.IsInstanceValid(_viewportProxy))
                    container = _viewportProxy;
                if (container == null || !container.Visible) continue;
                _primaryContainer ??= container;

                var rect = container.GetGlobalRect();
                if (rect.Size.X <= 0 || rect.Size.Y <= 0) continue;

                bool hasCamera = false;
                var view = default(Matrix4x4);
                var projection = default(Matrix4x4);
                var cam = vp.GetCamera3D();
                if (cam != null && GodotObject.IsInstanceValid(cam))
                {
                    hasCamera = true;
                    view = ViewFromGodot(cam.GlobalTransform);
                    projection = ProjectionFromGodot(cam.GetCameraProjection());
                }

                list.Add(new ViewportInfo(
                    id, rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y,
                    hasCamera, view, projection));
            }

            // Publish an immutable snapshot the worker can iterate without racing
            // the next frame's capture.
            _viewports = list;
        }

        private void CaptureInput()
        {
            // Coordinates must live in the same space as the viewport rects
            // (Control.GetGlobalRect). DisplayServer.MouseGetPosition() is
            // desktop-global; Control.GetGlobalMousePosition() is relative to
            // the control's window, which is what the rects use.
            Vector2 mouse;
            var container = _primaryContainer;
            bool containerUsable = container != null && GodotObject.IsInstanceValid(container)
                && container.IsInsideTree();
            if (containerUsable)
            {
                var g = container!.GetGlobalMousePosition();
                mouse = new Vector2(g.X, g.Y);
            }
            else
            {
                var m = DisplayServer.MouseGetPosition();
                mouse = new Vector2(m.X, m.Y);
            }

            bool active = containerUsable
                ? DisplayServer.WindowIsFocused(container!.GetWindow().GetWindowId())
                : DisplayServer.WindowIsFocused();

            // Translate Godot's platform-specific key codes into the engine's
            // renderer-agnostic InputKeys. A fresh set is built every frame so the
            // service can detect press edges against the previous snapshot.
            var keys = new HashSet<int>();
            AddIf(keys, Input.IsKeyPressed(Key.W), InputKeys.W);
            AddIf(keys, Input.IsKeyPressed(Key.A), InputKeys.A);
            AddIf(keys, Input.IsKeyPressed(Key.S), InputKeys.S);
            AddIf(keys, Input.IsKeyPressed(Key.D), InputKeys.D);
            AddIf(keys, Input.IsKeyPressed(Key.Q), InputKeys.Q);
            AddIf(keys, Input.IsKeyPressed(Key.E), InputKeys.E);
            AddIf(keys, Input.IsKeyPressed(Key.T), InputKeys.T);
            AddIf(keys, Input.IsKeyPressed(Key.R), InputKeys.R);
            AddIf(keys, Input.IsKeyPressed(Key.Escape), InputKeys.Escape);
            AddIf(keys, Input.IsKeyPressed(Key.Shift), InputKeys.Shift);
            AddIf(keys, Input.IsKeyPressed(Key.Alt), InputKeys.Alt);
            AddIf(keys, Input.IsKeyPressed(Key.Ctrl), InputKeys.Ctrl);

            _snapshot = new InputSnapshot
            {
                MousePosition = mouse,
                LeftDown = Input.IsMouseButtonPressed(MouseButton.Left),
                RightDown = Input.IsMouseButtonPressed(MouseButton.Right),
                MiddleDown = Input.IsMouseButtonPressed(MouseButton.Middle),
                Wheel = _wheel,
                PressedKeys = keys,
                WindowActive = active,
                MouseLocked = Input.MouseMode == Input.MouseModeEnum.Captured,
            };
        }

        private static void AddIf(HashSet<int> keys, bool down, int key)
        {
            if (down) keys.Add(key);
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static Transform3D ToGodotTransform(Matrix4x4 matrix)
        {
            var basis = new Basis(
                new global::Godot.Vector3(matrix.M11, matrix.M12, matrix.M13),
                new global::Godot.Vector3(matrix.M21, matrix.M22, matrix.M23),
                new global::Godot.Vector3(matrix.M31, matrix.M32, matrix.M33));
            return new Transform3D(basis, new global::Godot.Vector3(matrix.M41, matrix.M42, matrix.M43));
        }

        private static SubViewportContainer? FindContainer(SubViewport vp)
        {
            var node = vp.GetParent();
            while (node != null)
            {
                if (node is SubViewportContainer container) return container;
                node = node.GetParent();
            }
            return null;
        }

        /// <summary>World-from-local Godot transform → System.Numerics view
        /// matrix. The inverse maps world→camera in Godot's column-vector
        /// convention; row-vector System.Numerics needs its TRANSPOSE:
        /// V row i = column i of the inverse (basis vectors as rows), with the
        /// translation column landing in row 3.</summary>
        private static Matrix4x4 ViewFromGodot(Transform3D worldFromLocal)
        {
            var t = worldFromLocal.Inverse();
            return new Matrix4x4(
                t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z, 0f,
                t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z, 0f,
                t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z, 0f,
                t.Origin.X, t.Origin.Y, t.Origin.Z, 1f);
        }

        /// <summary>Godot Projection (column vectors) → System.Numerics
        /// projection (row-vector convention: transpose, one axis per row).</summary>
        private static Matrix4x4 ProjectionFromGodot(Projection p)
        {
            return new Matrix4x4(
                p.X.X, p.X.Y, p.X.Z, p.X.W,
                p.Y.X, p.Y.Y, p.Y.Z, p.Y.W,
                p.Z.X, p.Z.Y, p.Z.Z, p.Z.W,
                p.W.X, p.W.Y, p.W.Z, p.W.W);
        }
    }
}
