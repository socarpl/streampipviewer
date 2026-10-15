using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CctvPip.App;

internal static class Program
{
    private const int LeftUp = 0x0202;
    private const int RightUp = 0x0205;
    private static readonly MethodInfo HandleMouse = typeof(MainWindowMouseHook)
        .GetMethod("HandleMouseMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CanHandle = typeof(MainWindowMouseHook)
        .GetMethod("CanHandleMouseAt", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Contains("--child"))
        {
            using var child = new TestWindow();
            child.Shown += (_, _) => Console.WriteLine(child.Handle.ToInt64());
            Application.Run(child);
            return 0;
        }

        try
        {
            Run();
            Console.WriteLine("All mouse-hook regression checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Run()
    {
        using var owner = new TestWindow();
        using var blocker = new TestWindow();
        using var menu = new ContextMenuStrip();
        menu.Items.Add("Test");
        owner.Show();
        var point = owner.PointToScreen(new Point(60, 60));
        var fullscreenCount = 0;
        var swapCount = 0;
        var inPip = false;
        var allowed = true;
        Point? menuClickPoint = null;
        using var hook = new MainWindowMouseHook(owner, p =>
        {
            menuClickPoint = p;
            menu.Show(owner, owner.PointToClient(p));
        }, () => fullscreenCount++,
            () => swapCount++, _ => inPip,
            p => allowed && !menu.Visible && owner.RectangleToScreen(owner.ClientRectangle).Contains(p));

        bool Hit() => (bool)CanHandle.Invoke(hook, new object[] { point })!;
        void Click(int message = LeftUp) => HandleMouse.Invoke(hook, new object[] { message, point });
        void DoubleClick() { Click(); Click(); }

        Check(Hit(), "Exposed viewer accepts input");
        DoubleClick();
        Application.DoEvents();
        Check(fullscreenCount == 1, "Exposed main view toggles fullscreen");
        inPip = true;
        DoubleClick();
        Application.DoEvents();
        Check(swapCount == 1, "Exposed PIP swaps feeds");
        inPip = false;
        Click(RightUp);
        Application.DoEvents();
        Check(menu.Visible, "Exposed viewer opens its context menu");
        Check(menuClickPoint == point, "Context menu receives the original right-click screen point");
        menu.Close();

        blocker.Show();
        Check(!Hit(), "A covering window from the same process is rejected");
        DoubleClick();
        Click(RightUp);
        Application.DoEvents();
        Check(fullscreenCount == 1 && !menu.Visible, "Covered clicks perform no viewer actions");

        blocker.Hide();
        Click();
        blocker.Show();
        Click();
        blocker.Hide();
        Click();
        Application.DoEvents();
        Check(fullscreenCount == 1, "A click on another window breaks a pending double-click");
        // Clear the first-click state before checking queued actions.
        allowed = false;
        Click();
        allowed = true;

        DoubleClick();
        blocker.Show();
        Application.DoEvents();
        Check(fullscreenCount == 1, "Queued fullscreen action is cancelled when covered");
        blocker.Hide();
        inPip = true;
        DoubleClick();
        blocker.Show();
        Application.DoEvents();
        Check(swapCount == 1, "Queued swap action is cancelled when covered");
        blocker.Hide();
        Click(RightUp);
        blocker.Show();
        Application.DoEvents();
        Check(!menu.Visible, "Queued context menu is cancelled when covered");
        blocker.Hide();

        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--child");
        using var child = Process.Start(startInfo)!;
        try
        {
            var handleLine = child.StandardOutput.ReadLineAsync();
            if (!handleLine.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Test child window failed to start.");
            var childHandle = new IntPtr(long.Parse(handleLine.Result!));
            Check(!Hit(), "A covering window from another process is rejected");
            DoubleClick();
            Click(RightUp);
            Application.DoEvents();
            Check(fullscreenCount == 1 && swapCount == 1 && !menu.Visible,
                "Clicks on another application perform no viewer actions");

            // Model the stream host's native cross-process parenting.
            const int styleIndex = -16;
            SetWindowLong(childHandle, styleIndex,
                (GetWindowLong(childHandle, styleIndex) | 0x40000000) & ~unchecked((int)0x80000000));
            SetParent(childHandle, owner.Handle);
            MoveWindow(childHandle, 0, 0, 160, 120, true);
            Check(Hit(), "Embedded video window from another process accepts input");
            DoubleClick();
            Application.DoEvents();
            Check(swapCount == 2, "Double-click works over an embedded stream window");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill();
                child.WaitForExit(5000);
            }
        }

        owner.Hide();
        Check(!Hit(), "Hidden viewer rejects input");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}");
    }

    private sealed class TestWindow : Form
    {
        public TestWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(Screen.PrimaryScreen!.WorkingArea.Location, new Size(160, 120));
        }

        protected override bool ShowWithoutActivation => true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
}
