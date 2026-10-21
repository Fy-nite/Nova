using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotSplitter : ISplitter
    {
        private readonly SplitContainer _split;
        private float _ratio = 0.5f;
        private int? _initialMinimumPane;
        private bool _initialMinimumApplied;

        public GodotSplitter(SplitterOrientation orientation)
        {
            _split = new SplitContainer();
            // Godot: Vertical=true arranges children vertically (stacked);
            // false arranges them horizontally (side by side).
            _split.Vertical = orientation == SplitterOrientation.Stacked;
            _split.Dragged += OnDragged;
            _split.Resized += OnResized;
        }

        private Action<float>? _onSplitChanged;

        public object NativeControl => _split;

        public bool Visible
        {
            get => _split.Visible;
            set => _split.Visible = value;
        }

        private bool IsHorizontal => !_split.Vertical;

        private float TotalSize => IsHorizontal ? _split.Size.X : _split.Size.Y;

        private Control? ChildAt(int index)
            => _split.GetChildCount() > index ? _split.GetChild(index) as Control : null;

        private float SizeOf(Control? c)
        {
            if (c == null) return 0f;
            return IsHorizontal ? c.Size.X : c.Size.Y;
        }

        private float ActualRatio
        {
            get
            {
                var total = TotalSize;
                if (total <= 0f) return 0.5f;
                return Math.Clamp(SizeOf(ChildAt(0)) / total, 0f, 1f);
            }
        }

        public float SplitRatio
        {
            get => ActualRatio;
            set
            {
                _ratio = Math.Clamp(value, 0f, 1f);
                CorrectNow();
            }
        }

        /// <summary>Set SplitOffset absolutely so the first child occupies the
        /// desired ratio of the total. SplitOffset is measured from
        /// SplitContainer's default 50/50 boundary, so derive that boundary
        /// from the measured children + separator rather than accumulating
        /// deltas (accumulation stalls inside clamp plateaus). Re-runs on
        /// child resizes until the layout settles.</summary>
        public void SetInitialMinimumPane(int paneIndex)
        {
            if (paneIndex is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(paneIndex));
            _initialMinimumPane = paneIndex;
            _initialMinimumApplied = false;
            ScheduleInitialMinimum();
        }

        private void ScheduleInitialMinimum()
        {
            if (_initialMinimumPane != null && !_initialMinimumApplied)
                Callable.From(ApplyInitialMinimum).CallDeferred();
        }

        private void ApplyInitialMinimum()
        {
            if (_initialMinimumApplied || _initialMinimumPane == null || !_split.IsInsideTree()) return;
            if (_split.GetChildCount() < 2) return;
            var total = TotalSize;
            if (total <= 1f) return;
            var first = ChildAt(0);
            var second = ChildAt(1);
            if (first == null || second == null) return;
            var firstMin = AxisMinimum(first);
            var secondMin = AxisMinimum(second);
            var separator = Math.Max(0f, _split.GetThemeConstant("separation", "SplitContainer"));
            var available = Math.Max(1f, total - separator);
            var firstSize = _initialMinimumPane.Value == 0 ? firstMin : available - secondMin;
            firstSize = Math.Clamp(firstSize, Math.Min(firstMin, available), Math.Max(Math.Min(firstMin, available), available - Math.Min(secondMin, available)));
            _ratio = firstSize / total;
            var offset = (int)Math.Round(firstSize - available * 0.5f);
            _split.SplitOffsets = new[] { offset };
            _initialMinimumApplied = true;

        }

        private float AxisMinimum(Control control)
        {
            var size = control.GetCombinedMinimumSize();
            return IsHorizontal ? size.X : size.Y;
        }

        private void CorrectNow()
        {
            if (!_split.IsInsideTree()) return;
            if (_initialMinimumPane != null && !_initialMinimumApplied)
                ScheduleInitialMinimum();
            ApplyRatio();
        }

        private bool ApplyRatio()
        {
            var total = TotalSize;
            if (total <= 1f) return false;
            float first = SizeOf(ChildAt(0));
            float second = SizeOf(ChildAt(1));
            if (first <= 0f || second <= 0f) return false;

            // The separator is a fixed theme constant. Do NOT derive it from
            // total-first-second: when a pane is clamped to its minimum by the
            // layout, the deficit lands in that subtraction and the "separator"
            // absorbs it, which corrupts the offset and makes this method fight
            // the layout (pushing the crushed pane further down each pass).
            float separator = Math.Max(0f, _split.GetThemeConstant("separation", "SplitContainer"));
            float defaultMid = (total - separator) * 0.5f;
            float targetFirst = _ratio * total;
            int offset = (int)Math.Round(targetFirst - defaultMid);
            var current = _split.SplitOffsets;
            if (current.Length != 1 || current[0] != offset)
                _split.SplitOffsets = new[] { offset };
            return true;
        }

        public void SetFirst(IWidget child)
        {
            if (child.NativeControl is Node node)
            {
                _split.AddChild(node);
                if (node is Control c) c.Resized += OnResized;
                ScheduleInitialMinimum();
            }
        }

        public void SetSecond(IWidget child)
        {
            if (child.NativeControl is Node node)
            {
                _split.AddChild(node);
                if (node is Control c) c.Resized += OnResized;
                ScheduleInitialMinimum();
            }
        }

        public void SetOnSplitChanged(Action<float> onSplitChanged)
            => _onSplitChanged = onSplitChanged;

        private void OnDragged(long offset)
        {
            _ratio = ActualRatio;
            _onSplitChanged?.Invoke(_ratio);
        }

        private void OnResized()
        {
            CorrectNow();
        }
    }
}
