using System.Runtime.CompilerServices;
using Godot;

namespace ImGuiGodot.Internal;

internal static class Util
{
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern Rid ConstructRid(ulong id);
}
