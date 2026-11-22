using System.Runtime.InteropServices;

namespace MLPlatform.Agent.Terminal;

/// <summary>libc entry points for Unix pseudo terminals.</summary>
internal static partial class UnixNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinSize { public ushort Rows, Cols, XPixel, YPixel; }

    [LibraryImport("libc", EntryPoint = "openpty", SetLastError = true)]
    internal static partial int OpenPtyLibc(out int master, out int slave, nint name, nint termios, ref WinSize size);

    [LibraryImport("libutil.so.1", EntryPoint = "openpty", SetLastError = true)]
    internal static partial int OpenPtyLibutil(out int master, out int slave, nint name, nint termios, ref WinSize size);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int fd, nuint request, ref WinSize size);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    internal static unsafe partial nint Read(int fd, byte* buffer, nint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    internal static unsafe partial nint Write(int fd, byte* buffer, nint count);

    [LibraryImport("libc", EntryPoint = "close")]
    internal static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
    internal static partial int Dup(int fd);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    internal static partial int Kill(int pid, int signal);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    internal static partial int WaitPid(int pid, out int status, int options);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    internal static partial int FileActionsInit(nint actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    internal static partial int FileActionsAddDup2(nint actions, int fd, int newFd);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    internal static partial int FileActionsDestroy(nint actions);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
    internal static partial int AttrInit(nint attr);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    internal static partial int AttrSetFlags(nint attr, short flags);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    internal static partial int AttrDestroy(nint attr);

    [LibraryImport("libc", EntryPoint = "posix_spawnp", SetLastError = true)]
    internal static partial int SpawnP(out int pid, nint file, nint actions, nint attr, nint argv, nint envp);

    internal static int OpenPty(out int master, out int slave, ref WinSize size)
    {
        try { return OpenPtyLibc(out master, out slave, 0, 0, ref size); }
        catch (EntryPointNotFoundException) { return OpenPtyLibutil(out master, out slave, 0, 0, ref size); }
    }
}
