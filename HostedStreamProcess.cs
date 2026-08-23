using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public HostedStreamProcess(CameraView view, StreamId streamId, Func<AppConfig> configProvider)
    {
        this.view = view;
        this.streamId = streamId;
        this.configProvider = configProvider;
        resizeHandler = (_, _) => ResizeHostedWindow();
        this.view.VideoView.Resize += resizeHandler;
    }

    public bool IsMuted => muted;

    public void Start()
    {
        Restart();
    }

    public void Restart()
    {
        if (disposed)
        {
            return;
        }

        StopProcess();
        StartProcess();
    }

    public void SetMuted(bool muted)
    {
        this.muted = muted;
        SendCommand(muted ? CommandMute : CommandUnmute);
    }

    public bool ToggleMuted()
    {
        SetMuted(!muted);
        return muted;
    }

    public void ResizeToHost()
    {
        ResizeHostedWindow();
    }

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

            hostedWindow = WaitForMainWindow(process, TimeSpan.FromSeconds(8));
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

    private void ResizeHostedWindow()
    {
        if (hostedWindow == IntPtr.Zero || view.VideoView.IsDisposed)
        {
            return;
        }

        var size = view.VideoView.ClientSize;
        MoveWindow(hostedWindow, 0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height), true);
    }

    private void SendCommand(int command)
    {
        if (hostedWindow != IntPtr.Zero)
        {
            SendMessage(hostedWindow, HostCommandMessage, (IntPtr)command, IntPtr.Zero);
        }
    }

    private static IntPtr WaitForMainWindow(Process process, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("Stream host exited before its window was ready.");
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException("Timed out waiting for the stream host window.");
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
