using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CctvPip.App;

internal sealed class HostedStreamProcess : IDisposable
{
    private const int HostCommandMessage = 0x8000 + 321;
    private const int CommandMute = 1;
    private const int CommandUnmute = 2;
    private const int CommandRestart = 3;
    private const int CommandShutdown = 4;
    private const int GwlStyle = -16;
    private const int SwShow = 5;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsPopup = unchecked((int)0x80000000);

    private readonly CameraView view;
    private readonly StreamId streamId;
    private readonly Func<AppConfig> configProvider;
    private readonly EventHandler resizeHandler;
    private Process? process;
    private IntPtr hostedWindow;
    private bool muted = true;
    private bool disposed;

    /// <summary>
    /// Creates a controller for one out-of-process stream host and subscribes to resize events for its video surface.
    /// </summary>
    /// <param name="view">The camera view that will contain the embedded host window.</param>
    /// <param name="streamId">The configured stream that this host process should play.</param>
    /// <param name="configProvider">A function that returns the latest application configuration when the host starts.</param>
    public HostedStreamProcess(CameraView view, StreamId streamId, Func<AppConfig> configProvider)
    {
        this.view = view;
        this.streamId = streamId;
        this.configProvider = configProvider;
        resizeHandler = (_, _) => ResizeHostedWindow();
        this.view.VideoView.Resize += resizeHandler;
    }

    public bool IsMuted => muted;

    /// <summary>
    /// Starts the stream host process for this stream.
    /// </summary>
    public void Start()
    {
        Restart();
    }

    /// <summary>
    /// Stops any existing host process and launches a fresh host process using the current configuration.
    /// </summary>
    public void Restart()
    {
        if (disposed)
        {
            return;
        }

        StopProcess();
        StartProcess();
    }

    /// <summary>
    /// Stores and sends a mute state command to the hosted stream process.
    /// </summary>
    /// <param name="muted">Set to <see langword="true"/> to mute the host, or <see langword="false"/> to unmute it.</param>
    public void SetMuted(bool muted)
    {
        this.muted = muted;
        SendCommand(muted ? CommandMute : CommandUnmute);
    }

    /// <summary>
    /// Toggles the stored mute state and sends the matching command to the hosted stream process.
    /// </summary>
    /// <returns>The new mute state after toggling.</returns>
    public bool ToggleMuted()
    {
        SetMuted(!muted);
        return muted;
    }

    /// <summary>
    /// Resizes the embedded host window to fill the current camera view video surface.
    /// </summary>
    public void ResizeToHost()
    {
        ResizeHostedWindow();
    }

    /// <summary>
    /// Releases the resize subscription and shuts down the child stream host process.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        view.VideoView.Resize -= resizeHandler;
        StopProcess();
    }

    /// <summary>
    /// Launches the stream host executable, waits for its window, embeds it, and applies the current mute state.
    /// </summary>
    private void StartProcess()
    {
        var streamHostPath = ResolveStreamHostPath();
        var config = configProvider();
        var stream = config.GetStream(streamId);
        var streamName = streamId == StreamId.Cctv1 ? "cctv1" : "cctv2";

        view.ShowNoSignal("STARTING HOST");

        var startInfo = new ProcessStartInfo
        {
            FileName = streamHostPath,
            WorkingDirectory = Path.GetDirectoryName(streamHostPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--hosted");
        startInfo.ArgumentList.Add("--stream");
        startInfo.ArgumentList.Add(streamName);
        startInfo.ArgumentList.Add("--label");
        startInfo.ArgumentList.Add(stream.Label);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(config.Path);

        if (!muted)
        {
            startInfo.ArgumentList.Add("--unmuted");
        }

        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                view.ShowNoSignal("HOST START FAILED");
                return;
            }

            hostedWindow = WaitForHostWindow(process, TimeSpan.FromSeconds(8), stream.Label);
            EmbedHostedWindow();
            SendCommand(muted ? CommandMute : CommandUnmute);
            view.ShowLive();
        }
        catch (Exception ex)
        {
            StopProcess();
            view.ShowNoSignal("HOST START FAILED", ex.Message);
        }
    }

    /// <summary>
    /// Requests graceful shutdown of the child host process and terminates it if it does not exit in time.
    /// </summary>
    private void StopProcess()
    {
        var currentProcess = process;
        var currentWindow = hostedWindow;
        process = null;
        hostedWindow = IntPtr.Zero;

        if (currentProcess is null)
        {
            return;
        }

        try
        {
            if (!currentProcess.HasExited)
            {
                var commandWindow = currentWindow != IntPtr.Zero ? currentWindow : currentProcess.MainWindowHandle;
                if (commandWindow != IntPtr.Zero)
                {
                    SendMessage(commandWindow, HostCommandMessage, (IntPtr)CommandShutdown, IntPtr.Zero);
                }

                if (!currentProcess.WaitForExit(1500))
                {
                    currentProcess.Kill(entireProcessTree: true);
                    currentProcess.WaitForExit(1500);
                }
            }
        }
        catch
        {
        }
        finally
        {
            currentProcess.Dispose();
        }
    }

    /// <summary>
    /// Parents the discovered host window into the WinForms video surface and adjusts its native window styles.
    /// </summary>
    private void EmbedHostedWindow()
    {
        var hostControl = view.VideoView;
        if (hostedWindow == IntPtr.Zero || hostControl.IsDisposed)
        {
            return;
        }

        SetParent(hostedWindow, hostControl.Handle);
        var style = GetWindowLong(hostedWindow, GwlStyle);
        SetWindowLong(hostedWindow, GwlStyle, (style | WsChild | WsVisible) & ~WsPopup);
        ShowWindow(hostedWindow, SwShow);
        ResizeHostedWindow();
    }

    /// <summary>
    /// Moves and resizes the embedded host window to match the current video surface client size.
    /// </summary>
    private void ResizeHostedWindow()
    {
        if (hostedWindow == IntPtr.Zero || view.VideoView.IsDisposed)
        {
            return;
        }

        var size = view.VideoView.ClientSize;
        MoveWindow(hostedWindow, 0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height), true);
    }

    /// <summary>
    /// Sends a host command message to the embedded stream host window.
    /// </summary>
    /// <param name="command">The host command identifier, such as mute, unmute, restart, or shutdown.</param>
    private void SendCommand(int command)
    {
        if (hostedWindow != IntPtr.Zero)
        {
            SendMessage(hostedWindow, HostCommandMessage, (IntPtr)command, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Waits for the child process to create the expected host window.
    /// </summary>
    /// <param name="process">The child stream host process that was just started.</param>
    /// <param name="timeout">The maximum amount of time to wait for a usable host window.</param>
    /// <param name="expectedTitlePrefix">The title prefix used to distinguish the desired host window.</param>
    /// <returns>The native window handle for the host form.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the process exits before a host window is available.</exception>
    /// <exception cref="TimeoutException">Thrown when no matching host window is found before the timeout expires.</exception>
    private static IntPtr WaitForHostWindow(Process process, TimeSpan timeout, string expectedTitlePrefix)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("Stream host exited before its window was ready.");
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero && IsExpectedHostWindow(process.MainWindowHandle, expectedTitlePrefix))
            {
                return process.MainWindowHandle;
            }

            var windowHandle = FindProcessWindow(process.Id, expectedTitlePrefix);
            if (windowHandle != IntPtr.Zero)
            {
                return windowHandle;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for the {expectedTitlePrefix} stream host window.");
    }

    /// <summary>
    /// Enumerates visible top-level windows and finds the window owned by the requested process with the expected title prefix.
    /// </summary>
    /// <param name="processId">The process ID that must own the host window.</param>
    /// <param name="expectedTitlePrefix">The title prefix used to match the correct host form.</param>
    /// <returns>The matching native window handle, or <see cref="IntPtr.Zero"/> when no match is found.</returns>
    private static IntPtr FindProcessWindow(int processId, string expectedTitlePrefix)
    {
        var foundWindow = IntPtr.Zero;
        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }

            if (!IsExpectedHostWindow(windowHandle, expectedTitlePrefix))
            {
                return true;
            }

            foundWindow = windowHandle;
            return false;
        }, IntPtr.Zero);

        return foundWindow;
    }

    /// <summary>
    /// Checks whether a native window title starts with the expected host label.
    /// </summary>
    /// <param name="windowHandle">The native window handle to inspect.</param>
    /// <param name="expectedTitlePrefix">The required title prefix, such as <c>CCTV1</c> or <c>CCTV2</c>.</param>
    /// <returns><see langword="true"/> when the window title matches the expected host label.</returns>
    private static bool IsExpectedHostWindow(IntPtr windowHandle, string expectedTitlePrefix)
    {
        var title = GetWindowTitle(windowHandle);
        return title.StartsWith(expectedTitlePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the Unicode title text for a native window.
    /// </summary>
    /// <param name="windowHandle">The native window handle whose title should be read.</param>
    /// <returns>The window title, or an empty string when the title cannot be read.</returns>
    private static string GetWindowTitle(IntPtr windowHandle)
    {
        var length = Math.Max(GetWindowTextLength(windowHandle) + 1, 256);
        var builder = new StringBuilder(length);
        return GetWindowText(windowHandle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    /// <summary>
    /// Resolves the stream host executable path from the main output folder or development build output.
    /// </summary>
    /// <returns>The full path to <c>CctvPip.StreamHost.exe</c>.</returns>
    /// <exception cref="FileNotFoundException">Thrown when no candidate stream host executable exists.</exception>
    private static string ResolveStreamHostPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "StreamHost", "CctvPip.StreamHost.exe"),
            Path.Combine(AppContext.BaseDirectory, "CctvPip.StreamHost.exe"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "CctvPip.StreamHost",
                "bin",
                "Debug",
                "net8.0-windows",
                "CctvPip.StreamHost.exe"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not find CctvPip.StreamHost.exe. Build the stream host project first.", candidates[0]);
    }

    /// <summary>
    /// Changes the parent window for a native child window.
    /// </summary>
    /// <param name="hWndChild">The native window handle that should become a child window.</param>
    /// <param name="hWndNewParent">The native window handle that should become the parent.</param>
    /// <returns>The previous parent window handle, or <see cref="IntPtr.Zero"/> on failure.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    /// <summary>
    /// Moves and resizes a native window.
    /// </summary>
    /// <param name="hWnd">The native window handle to move.</param>
    /// <param name="x">The new left coordinate relative to the parent window.</param>
    /// <param name="y">The new top coordinate relative to the parent window.</param>
    /// <param name="width">The new window width in pixels.</param>
    /// <param name="height">The new window height in pixels.</param>
    /// <param name="repaint">Whether Windows should repaint the window after the move.</param>
    /// <returns><see langword="true"/> when the native call succeeds.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    /// <summary>
    /// Reads a 32-bit native window attribute.
    /// </summary>
    /// <param name="hWnd">The native window handle to inspect.</param>
    /// <param name="nIndex">The attribute index to read, such as <c>GWL_STYLE</c>.</param>
    /// <returns>The requested native window attribute value.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    /// <summary>
    /// Writes a 32-bit native window attribute.
    /// </summary>
    /// <param name="hWnd">The native window handle to update.</param>
    /// <param name="nIndex">The attribute index to write, such as <c>GWL_STYLE</c>.</param>
    /// <param name="dwNewLong">The new attribute value.</param>
    /// <returns>The previous native window attribute value.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>
    /// Changes the show state of a native window.
    /// </summary>
    /// <param name="hWnd">The native window handle to show or hide.</param>
    /// <param name="nCmdShow">The show command, such as <c>SW_SHOW</c>.</param>
    /// <returns><see langword="true"/> when the native call succeeds.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Sends a synchronous message to a native window.
    /// </summary>
    /// <param name="hWnd">The native window handle that receives the message.</param>
    /// <param name="msg">The message identifier.</param>
    /// <param name="wParam">The message-specific word-sized parameter.</param>
    /// <param name="lParam">The message-specific pointer-sized parameter.</param>
    /// <returns>The message result returned by the target window procedure.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Callback signature used while enumerating top-level native windows.
    /// </summary>
    /// <param name="hWnd">The native window handle currently being enumerated.</param>
    /// <param name="lParam">The caller-provided pointer value passed to <see cref="EnumWindows"/>.</param>
    /// <returns><see langword="true"/> to continue enumeration, or <see langword="false"/> to stop.</returns>
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Enumerates all top-level native windows.
    /// </summary>
    /// <param name="lpEnumFunc">The callback invoked for each top-level window.</param>
    /// <param name="lParam">A caller-provided pointer passed through to the callback.</param>
    /// <returns><see langword="true"/> when enumeration completes successfully.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Retrieves the process ID that owns a native window.
    /// </summary>
    /// <param name="hWnd">The native window handle to inspect.</param>
    /// <param name="lpdwProcessId">Receives the owning process ID.</param>
    /// <returns>The thread ID that created the window.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    /// <summary>
    /// Determines whether a native window is visible.
    /// </summary>
    /// <param name="hWnd">The native window handle to inspect.</param>
    /// <returns><see langword="true"/> when the window has the visible style.</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// Retrieves the length of a native window's title text.
    /// </summary>
    /// <param name="hWnd">The native window handle to inspect.</param>
    /// <returns>The title length in characters, not including the null terminator.</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>
    /// Retrieves a native window's title text.
    /// </summary>
    /// <param name="hWnd">The native window handle to inspect.</param>
    /// <param name="lpString">The buffer that receives the title text.</param>
    /// <param name="nMaxCount">The maximum number of characters to copy, including the null terminator.</param>
    /// <returns>The number of characters copied, not including the null terminator.</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
