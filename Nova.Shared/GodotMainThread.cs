using System;
using System.Collections.Concurrent;
using Godot;
using V12.Core.Diagnostics;
namespace V12TwoDog
{
    public static class GodotMainThread
    {
        private static readonly ConcurrentQueue<Action> _actions = new();

        public static void Execute(Action action)
        {
            if (action == null) return;
            _actions.Enqueue(action);
        }

        public static void FlushPending()
        {
            using var _zone = V12Profiler.BeginZone("GodotMainThread.FlushPending", V12Profiler.Colors.Runtime);
            int count = 0;
            while (_actions.TryDequeue(out var action))
            {
                count++;
                try { action(); }
                catch (Exception ex)
                {
                    GD.PrintErr($"[GodotMainThread] {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (count > 0)
                V12Profiler.Plot("GodotMainThread Flushed", count);
        }
    }
}
