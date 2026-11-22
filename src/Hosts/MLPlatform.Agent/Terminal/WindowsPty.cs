using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MLPlatform.Agent.Terminal;

/// <summary>ConPTY: a pseudo console fed by two pipes; the shell is created with the console attribute
/// attached.</summary>
public sealed class WindowsPty : IPty
{
    private readonly nint _console;
    private readonly Process _process;

    public Stream Output { get; }
    public Stream Input { get; }

    private WindowsPty(nint console, Process process, SafeFileHandle input, SafeFileHandle output)
    {
        _console = console;
        _process = process;
        Input = new FileStream(input, FileAccess.Write, 1);
        Output = new FileStream(output, FileAccess.Read, 1);
    }

    public static WindowsPty Start(string shell, string cwd, int cols, int rows)
    {
        if (!WindowsNative.CreatePipe(out var inRead, out var inWrite, 0, 0) || !WindowsNative.CreatePipe(out var outRead, out var outWrite, 0, 0))
            throw new IOException("CreatePipe failed: " + Marshal.GetLastPInvokeError());
        var hr = WindowsNative.CreatePseudoConsole(Size(cols, rows), inRead, outWrite, 0, out var console);
        if (hr != 0) throw new IOException($"CreatePseudoConsole failed: 0x{hr:x8}");
        try
        {
            var process = Spawn(console, shell, cwd);
            return new WindowsPty(console, process, inWrite, outRead);
        }
        catch { WindowsNative.ClosePseudoConsole(console); throw; }
        finally { inRead.Dispose(); outWrite.Dispose(); }
    }

    private static WindowsNative.Coord Size(int cols, int rows) => new() { X = (short)cols, Y = (short)rows };

    private static Process Spawn(nint console, string shell, string cwd)
    {
        nint size = 0;
        WindowsNative.InitializeProcThreadAttributeList(0, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        try
        {
            if (!WindowsNative.InitializeProcThreadAttributeList(list, 1, 0, ref size)
                || !WindowsNative.UpdateProcThreadAttribute(list, 0, WindowsNative.PseudoConsoleAttribute, console, nint.Size, 0, 0))
                throw new IOException("Pseudo console attribute failed: " + Marshal.GetLastPInvokeError());
            var startup = new WindowsNative.StartupInfoEx { Cb = Marshal.SizeOf<WindowsNative.StartupInfoEx>(), AttributeList = list, Flags = WindowsNative.StartfUseStdHandles };
            var commandLine = (shell + "\0").ToCharArray();
            if (!WindowsNative.CreateProcess(null, commandLine, 0, 0, false, WindowsNative.ExtendedStartupInfoPresent, 0,
                    Directory.Exists(cwd) ? cwd : null, ref startup, out var info))
                throw new IOException("CreateProcess failed: " + Marshal.GetLastPInvokeError());
            WindowsNative.CloseHandle(info.Thread);
            var process = Process.GetProcessById(info.ProcessId);
            WindowsNative.CloseHandle(info.Process);
            return process;
        }
        finally
        {
            WindowsNative.DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
        }
    }

    public void Resize(int cols, int rows) => WindowsNative.ResizePseudoConsole(_console, Size(cols, rows));

    public async Task<int> WaitForExitAsync(CancellationToken ct)
    {
        await _process.WaitForExitAsync(ct);
        return _process.ExitCode;
    }

    public void Kill()
    {
        try { if (!_process.HasExited) _process.Kill(true); }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        Kill();
        WindowsNative.ClosePseudoConsole(_console);
        Input.Dispose();
        Output.Dispose();
        _process.Dispose();
    }
}
