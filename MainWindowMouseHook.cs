using System.Runtime.InteropServices;

namespace CctvPip.App;

internal sealed class MainWindowMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;

    private readonly Form owner;
    private readonly Action<Point> showContextMenu;
    private readonly Action toggleFullscreen;
    private readonly Action swapStreams;
    private readonly Func<Point, bool> isPointInsidePip;
    private readonly Func<Point, bool> isMouseActionAllowed;
    private readonly LowLevelMouseProc callback;
    private IntPtr hookHandle;
    private DateTime lastLeftClickAt = DateTime.MinValue;
    private Point lastLeftClickPoint = Point.Empty;
    private bool disposed;

    /// <summary>
    /// Installs a low-level mouse hook that routes right-click and double-click actions to the main viewer window.
    /// </summary>
    /// <param name="owner">The form that owns the context menu and receives marshalled UI actions.</param>
    /// <param name="showContextMenu">Shows the viewer menu using the original right-click screen point.</param>
    /// <param name="toggleFullscreen">The action invoked when the user double-clicks outside the PIP area.</param>
    /// <param name="swapStreams">The action invoked when the user double-clicks inside the PIP area.</param>
    /// <param name="isPointInsidePip">A predicate that determines whether a screen point is inside the current PIP bounds.</param>
    /// <param name="isMouseActionAllowed">A predicate that determines whether the mouse point belongs to the active viewer surface.</param>
    public MainWindowMouseHook(
        Form owner,
        Action<Point> showContextMenu,
        Action toggleFullscreen,
        Action swapStreams,
        Func<Point, bool> isPointInsidePip,
        Func<Point, bool> isMouseActionAllowed)
    {
        this.owner = owner;
        this.showContextMenu = showContextMenu;
        this.toggleFullscreen = toggleFullscreen;
        this.swapStreams = swapStreams;
        this.isPointInsidePip = isPointInsidePip;
        this.isMouseActionAllowed = isMouseActionAllowed;
        callback = HookCallback;
        hookHandle = SetWindowsHookEx(WhMouseLl, callback, IntPtr.Zero, 0);
    }

    /// <summary>
    /// Removes the low-level mouse hook if it is currently installed.
    /// </summary>
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

    /// <summary>
    /// Receives low-level mouse messages from Windows and forwards relevant button-up events to the app handler.
    /// </summary>
    /// <param name="nCode">The hook code provided by Windows; negative values must be passed to the next hook.</param>
    /// <param name="wParam">The mouse message identifier.</param>
    /// <param name="lParam">A pointer to the native low-level mouse data structure.</param>
    /// <returns>The result from the next hook in the chain.</returns>
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

    /// <summary>
    /// Handles right-click menu display and double-click actions for a screen point inside the viewer.
    /// </summary>
    /// <param name="message">The mouse button-up message being handled.</param>
    /// <param name="screenPoint">The mouse position in screen coordinates.</param>
    private void HandleMouseMessage(int message, Point screenPoint)
    {
        if (!CanHandleMouseAt(screenPoint))
        {
            lastLeftClickAt = DateTime.MinValue;
            return;
        }

        if (message == WmRButtonUp)
        {
            BeginOnOwner(() =>
            {
                if (CanHandleMouseAt(screenPoint))
                {
                    showContextMenu(screenPoint);
                }
            });
            return;
        }

        if (IsSecondLeftClick(screenPoint))
        {
            lastLeftClickAt = DateTime.MinValue;
            BeginOnOwner(() =>
            {
                if (!CanHandleMouseAt(screenPoint))
                {
                    return;
                }

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

    /// <summary>
    /// Checks that the exposed window under the pointer is the viewer or one of its native children.
    /// Embedded stream windows belong to other processes, so native ancestry is used instead of process identity.
    /// </summary>
    /// <param name="screenPoint">The mouse position in screen coordinates.</param>
    /// <returns><see langword="true"/> when the viewer can handle a mouse action at this point.</returns>
    private bool CanHandleMouseAt(Point screenPoint)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated || !owner.Visible ||
            owner.WindowState == FormWindowState.Minimized || !isMouseActionAllowed(screenPoint))
        {
            return false;
        }

        var window = WindowFromPoint(new NativePoint(screenPoint.X, screenPoint.Y));
        return window != IntPtr.Zero && (window == owner.Handle || IsChild(owner.Handle, window));
    }

    /// <summary>
    /// Marshals an action onto the owner form's UI thread when the form is still valid.
    /// </summary>
    /// <param name="action">The UI action to execute asynchronously on the owner form.</param>
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

    /// <summary>
    /// Determines whether the current left-click completes a system-timed double-click near the previous click.
    /// </summary>
    /// <param name="screenPoint">The latest left-click position in screen coordinates.</param>
    /// <returns><see langword="true"/> when the click is close enough in time and distance to count as a double-click.</returns>
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

    /// <summary>
    /// Callback signature used by the Windows low-level mouse hook API.
    /// </summary>
    /// <param name="nCode">The hook code supplied by Windows.</param>
    /// <param name="wParam">The mouse message identifier.</param>
    /// <param name="lParam">A pointer to native mouse event data.</param>
    /// <returns>The native hook result.</returns>
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

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>Gets the visible native window under a point in screen coordinates.</summary>
    /// <param name="point">The screen point to hit-test.</param>
    /// <returns>The window at the point, or zero when none exists.</returns>
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    /// <summary>Checks native child ancestry, including windows hosted from another process.</summary>
    /// <param name="parent">The ancestor window handle.</param>
    /// <param name="window">The potential child window handle.</param>
    /// <returns>Whether the window is a descendant of the supplied parent.</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    /// <summary>
    /// Installs a Windows hook procedure.
    /// </summary>
    /// <param name="idHook">The hook type to install.</param>
    /// <param name="lpfn">The hook callback function.</param>
    /// <param name="hMod">The module handle containing the callback, or zero for this process.</param>
    /// <param name="dwThreadId">The thread ID to hook, or zero for all desktop threads.</param>
    /// <returns>The hook handle, or <see cref="IntPtr.Zero"/> on failure.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    /// <summary>
    /// Removes a Windows hook procedure.
    /// </summary>
    /// <param name="hhk">The hook handle returned by <see cref="SetWindowsHookEx"/>.</param>
    /// <returns><see langword="true"/> when the hook is removed successfully.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    /// <summary>
    /// Passes hook data to the next hook procedure in the chain.
    /// </summary>
    /// <param name="hhk">The current hook handle.</param>
    /// <param name="nCode">The hook code supplied by Windows.</param>
    /// <param name="wParam">The mouse message identifier.</param>
    /// <param name="lParam">A pointer to native mouse event data.</param>
    /// <returns>The result produced by the next hook procedure.</returns>
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
