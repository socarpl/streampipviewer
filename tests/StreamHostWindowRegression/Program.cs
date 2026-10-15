using System.Reflection;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

internal static class Program
{
    private const int ExStyleIndex = -20;
    private const int ToolWindow = 0x80;
    private const int AppWindow = 0x40000;

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Core.Initialize();
            var assembly = Assembly.Load("CctvPip.StreamHost");
            var optionsType = assembly.GetType("CctvPip.StreamHost.HostOptions")!;
            var formType = assembly.GetType("CctvPip.StreamHost.StreamHostForm")!;
            foreach (var hosted in new[] { true, false })
            {
                var options = Activator.CreateInstance(optionsType, "Test Host", "unused://test",
                    Array.Empty<string>(), Array.Empty<string>(), true, hosted)!;
                using var form = (Form)Activator.CreateInstance(formType, options)!;
                // Native handles are created without showing the form, so playback never starts.
                CheckStyles(form.Handle, hosted, "initial handle");
                Check(form.ShowInTaskbar == !hosted, $"{Mode(hosted)} has the expected taskbar visibility");
                typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                CheckStyles(form.Handle, hosted, "recreated handle");
                if (hosted)
                {
                    using var parent = new Form();
                    const int styleIndex = -16;
                    SetParent(form.Handle, parent.Handle);
                    SetWindowLong(form.Handle, styleIndex,
                        (GetWindowLong(form.Handle, styleIndex) | 0x40000000) & ~unchecked((int)0x80000000));
                    CheckStyles(form.Handle, hosted, "embedded handle");
                }
            }
            Console.WriteLine("All stream-host window regression checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckStyles(IntPtr handle, bool hosted, string description)
    {
        var style = GetWindowLong(handle, ExStyleIndex);
        Check(hosted ? (style & ToolWindow) != 0 && (style & AppWindow) == 0
            : (style & ToolWindow) == 0 && (style & AppWindow) != 0,
            $"{Mode(hosted)} uses the expected Alt+Tab styles for its {description}");
    }

    private static string Mode(bool hosted) => hosted ? "Embedded host" : "Standalone host";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"PASS: {message}");
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
}
