using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MLPlatform.Agent.Terminal;

/// <summary>kernel32 entry points for the Windows pseudo console (ConPTY).</summary>
internal static partial class WindowsNative
{
    internal const uint ExtendedStartupInfoPresent = 0x00080000, CreateUnicodeEnvironment = 0x00000400, StartfUseStdHandles = 0x00000100;
    internal static readonly nint PseudoConsoleAttribute = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx
    {
        public int Cb;
        public nint Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public uint Flags;
        public short ShowWindow, Reserved2Count;
        public nint Reserved2, StdInput, StdOutput, StdError, AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation { public nint Process, Thread; public int ProcessId, ThreadId; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, int size);

    [LibraryImport("kernel32.dll")]
    internal static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);

    [LibraryImport("kernel32.dll")]
    internal static partial int ResizePseudoConsole(nint console, Coord size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(nint console);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previous, nint returnSize);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(nint list);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(string? application, char[] commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string? currentDirectory, ref StartupInfoEx startup,
        out ProcessInformation info);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
