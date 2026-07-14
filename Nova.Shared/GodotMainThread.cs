using System;
using System.Collections.Concurrent;
using Godot;
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
            while (_actions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex)
                {
                    GD.PrintErr($"[GodotMainThread] {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
