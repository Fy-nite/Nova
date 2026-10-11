using System.Collections.Generic;
using V12;

namespace V12TwoDog
{
    /// <summary>
    /// Process-wide hand-off of game paks from an embedding host (e.g.
    /// <c>V12Host.RegisterGamePak</c>) to the running <see cref="V12Runtime"/>.
    ///
    /// <para>
    /// Only one Godot instance may run per process, so a static is sufficient:
    /// RootLoop reads <see cref="Pending"/> when it builds the runtime and, when
    /// non-empty, uses those paks instead of auto-discovering from assemblies.
    /// </para>
    /// </summary>
    public static class GamepakHandoff
    {
        private static readonly List<IV12Gamepack> _pending = new();

        public static void Register(IV12Gamepack pak)
        {
            if (pak != null && !_pending.Contains(pak))
                _pending.Add(pak);
        }

        public static IReadOnlyList<IV12Gamepack> Pending => _pending;

        public static void Clear() => _pending.Clear();
    }
}
