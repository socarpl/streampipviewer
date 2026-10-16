using System.ComponentModel;
using System.Reflection;

internal static class Program
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var configPath = Path.Combine(AppContext.BaseDirectory, "reconnect-test.properties");
        try
        {
            var app = Assembly.Load("CctvPip.App");
            var propertiesType = app.GetType("CctvPip.App.PropertiesFile")!;
            var properties = propertiesType.GetMethod("Load")!.Invoke(null, new object[] { configPath });
            var configType = app.GetType("CctvPip.App.AppConfig")!;
            var config = configType.GetConstructors(InstanceFields).Single()
                .Invoke(new[] { configPath, properties });
            using var form = (Form)Activator.CreateInstance(app.GetType("CctvPip.App.MainForm")!, config)!;
            var firstPlayer = Field(form, "cctv1Player")!;
            var secondPlayer = Field(form, "cctv2Player")!;
            // Exercise the real form/menu without starting hosts or camera connections.
            SetField(firstPlayer, "disposed", true);
            SetField(secondPlayer, "disposed", true);
            form.ShowInTaskbar = false;
            form.Enabled = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();

            var menu = (ContextMenuStrip)Field(form, "contextMenu")!;
            var reconnect = (ToolStripMenuItem)Field(form, "reconnectMenuItem")!;
            Check(menu.Items[0] == reconnect && reconnect.Text == "Reconnect", "Reconnect is the first menu item");

            Point PipPoint()
            {
                var swapped = (bool)Field(form, "swapped")!;
                var pip = (Control)Field(form, swapped ? "cctv1View" : "cctv2View")!;
                return pip.PointToScreen(new Point(pip.ClientSize.Width / 2, pip.ClientSize.Height / 2));
            }
            Point MainPoint()
            {
                var stage = (Control)Field(form, "stage")!;
                return stage.PointToScreen(new Point(10, stage.ClientSize.Height - 10));
            }
            void OpenAt(Point point)
            {
                SetField(form, "contextMenuScreenPoint", (Point?)point);
                typeof(ContextMenuStrip).GetMethod("OnOpening", InstanceFields)!
                    .Invoke(menu, new object[] { new CancelEventArgs() });
            }

            OpenAt(MainPoint());
            Check(ReferenceEquals(Field(form, "contextMenuPlayer"), firstPlayer), "Main click targets CCTV1 before swap");
            OpenAt(PipPoint());
            Check(ReferenceEquals(Field(form, "contextMenuPlayer"), secondPlayer), "PIP click targets CCTV2 before swap");
            Call(form, "SwapStreams");
            Check(ReferenceEquals(Field(form, "contextMenuPlayer"), secondPlayer), "Open menu keeps the originally selected stream after layout changes");
            OpenAt(MainPoint());
            Check(ReferenceEquals(Field(form, "contextMenuPlayer"), secondPlayer), "Main click targets CCTV2 after swap");
            OpenAt(PipPoint());
            Check(ReferenceEquals(Field(form, "contextMenuPlayer"), firstPlayer), "PIP click targets CCTV1 after swap");
            Check(Field(form, "contextMenuPlayer") != null && reconnect.Enabled, "Reconnect remains enabled for the selected stream");
            var originalSettings = Field(form, "currentSettings");
            foreach (var size in new[] { new Size(3840, 2160), new Size(1000, 700), new Size(1920, 1080) })
            {
                form.ClientSize = size;
                Call(form, "ApplyLayout");
                var stage = (Control)Field(form, "stage")!;
                var pip = (Control)Field(form, "cctv1View")!; // Feeds are swapped above.
                Check(Math.Abs(pip.Left - stage.Width * .70) <= 1 && Math.Abs(pip.Top - stage.Height * .04) <= 1,
                    $"Real form keeps PIP at 70% / 4% when resized to {size.Width} by {size.Height}");
            }
            Check(ReferenceEquals(originalSettings, Field(form, "currentSettings")), "Resizing does not overwrite the requested position");
            Call(form, "ShowOptions");
            var options = (Form)Field(form, "optionsForm")!;
            Check(options.Text == "PIP Settings" && options.Controls.OfType<ToolStrip>().Any(),
                "Production settings dialog opens with its embedded toolbar resources");
            options.Close();
            Console.WriteLine("All reconnect-target regression checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, InstanceFields)!.GetValue(target);

    private static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, InstanceFields)!.SetValue(target, value);

    private static void Call(object target, string name) =>
        target.GetType().GetMethod(name, InstanceFields)!.Invoke(target, null);

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}");
    }
}
