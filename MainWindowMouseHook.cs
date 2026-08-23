using System.Runtime.InteropServices;

namespace CctvPip.App;

internal sealed class MainWindowMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;

    private readonly Form owner;
    private readonly ContextMenuStrip contextMenu;
    private readonly Action toggleFullscreen;
    private readonly Action swapStreams;
    private readonly Func<Point, bool> isPointInsidePip;
    private readonly Func<Point, bool> isMouseActionAllowed;
    private readonly LowLevelMouseProc callback;
    private IntPtr hookHandle;
    private DateTime lastLeftClickAt = DateTime.MinValue;
    private Point lastLeftClickPoint = Point.Empty;
    private bool disposed;

    public MainWindowMouseHook(
        Form owner,
        ContextMenuStrip contextMenu,
        Action toggleFullscreen,
        Action swapStreams,
        Func<Point, bool> isPointInsidePip,
        Func<Point, bool> isMouseActionAllowed)
    {
        this.owner = owner;
        this.contextMenu = contextMenu;
        this.toggleFullscreen = toggleFullscreen;
        this.swapStreams = swapStreams;
        this.isPointInsidePip = isPointInsidePip;
        this.isMouseActionAllowed = isMouseActionAllowed;
        callback = HookCallback;
        hookHandle = SetWindowsHookEx(WhMouseLl, callback, IntPtr.Zero, 0);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || disposed)
        {
            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        if (message is WmRButtonUp or WmLButtonUp)
        {
            var hookData = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            var screenPoint = new Point(hookData.Point.X, hookData.Point.Y);
            HandleMouseMessage(message, screenPoint);
        }

        return CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    private void HandleMouseMessage(int message, Point screenPoint)
    {
        if (!isMouseActionAllowed(screenPoint))
        {
            return;
        }

        if (message == WmRButtonUp)
        {
            BeginOnOwner(() =>
            {
                if (isMouseActionAllowed(screenPoint))
                {
                    contextMenu.Show(owner, owner.PointToClient(screenPoint));
                }
            });
            return;
        }

        if (IsSecondLeftClick(screenPoint))
        {
            lastLeftClickAt = DateTime.MinValue;
            BeginOnOwner(() =>
            {
                if (isPointInsidePip(screenPoint))
                {
                    swapStreams();
                    return;
                }

                toggleFullscreen();
            });
            return;
        }

        lastLeftClickAt = DateTime.UtcNow;
        lastLeftClickPoint = screenPoint;
    }

    private void BeginOnOwner(Action action)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        try
        {
            owner.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool IsSecondLeftClick(Point screenPoint)
    {
        if (lastLeftClickAt == DateTime.MinValue)
        {
            return false;
        }

        var elapsedMs = (DateTime.UtcNow - lastLeftClickAt).TotalMilliseconds;
        if (elapsedMs > SystemInformation.DoubleClickTime)
        {
            return false;
        }

        var doubleClickSize = SystemInformation.DoubleClickSize;
        var doubleClickBounds = new Rectangle(
            lastLeftClickPoint.X - doubleClickSize.Width / 2,
            lastLeftClickPoint.Y - doubleClickSize.Height / 2,
            doubleClickSize.Width,
            doubleClickSize.Height);

        return doubleClickBounds.Contains(screenPoint);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseHookStruct
    {
        public readonly NativePoint Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
