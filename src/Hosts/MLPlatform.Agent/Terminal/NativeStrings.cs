using System.Runtime.InteropServices;

namespace MLPlatform.Agent.Terminal;

/// <summary>A NULL-terminated array of UTF-8 C strings (argv / envp) in unmanaged memory.</summary>
internal sealed class NativeStrings : IDisposable
{
    private readonly nint[] _items;

    public nint Pointer { get; }

    public NativeStrings(IReadOnlyList<string> values)
    {
        _items = [.. values.Select(Marshal.StringToCoTaskMemUTF8)];
        Pointer = Marshal.AllocHGlobal(nint.Size * (_items.Length + 1));
        for (var i = 0; i < _items.Length; i++) Marshal.WriteIntPtr(Pointer, i * nint.Size, _items[i]);
        Marshal.WriteIntPtr(Pointer, _items.Length * nint.Size, 0);
    }

    public void Dispose()
    {
        foreach (var p in _items) Marshal.FreeCoTaskMem(p);
        Marshal.FreeHGlobal(Pointer);
    }
}
