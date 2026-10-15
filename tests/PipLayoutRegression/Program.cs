using System.Globalization;
using System.Reflection;
using CctvPip.App;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data");
        Directory.CreateDirectory(directory);
        try
        {
            TestLayout();
            TestConfiguration(directory);
            TestOptions(directory);
            Console.WriteLine("All percentage PIP regression checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void TestLayout()
    {
        Check(PipLayout.GetBounds(new Size(1000, 500), .28, 10, 10).Location == new Point(100, 50),
            "10% by 10% gives (100, 50) at 1000 by 500");
        Check(PipLayout.GetBounds(new Size(2000, 1000), .28, 10, 10).Location == new Point(200, 100),
            "Doubling the video area doubles the percentage offsets");
        Check(PipLayout.GetBounds(new Size(1000, 500), .28, 0, 0).Location == Point.Empty,
            "Zero margins place PIP at the top-left");
        var hd = PipLayout.GetBounds(new Size(1920, 1080), .28, 70, 4);
        var uhd = PipLayout.GetBounds(new Size(3840, 2160), .28, 70, 4);
        Check(Math.Abs(hd.X * 2 - uhd.X) <= 1 && Math.Abs(hd.Y * 2 - uhd.Y) <= 1 &&
            Math.Abs(hd.Width * 2 - uhd.Width) <= 1, "Position and size scale together between 1080p and 4K");
        foreach (var size in new[] { new Size(3840, 2160), new Size(960, 540), new Size(80, 40) })
        {
            var bounds = PipLayout.GetBounds(size, .28, 100, 100);
            Check(new Rectangle(Point.Empty, size).Contains(bounds) && bounds.Right == size.Width && bounds.Bottom == size.Height,
                $"Clamping keeps all of PIP inside {size.Width} by {size.Height}");
        }
        Check(PipLayout.GetBounds(Size.Empty, .28, 70, 4) == Rectangle.Empty, "Minimized/empty video area is safe");
    }

    private static void TestConfiguration(string directory)
    {
        using var defaults = Config(directory, "defaults", "");
        Check(defaults.PipLeftPercent == 70 && defaults.PipTopPercent == 4 &&
            !File.ReadAllText(defaults.Path).Contains("pip.x="), "New configurations use percentage defaults");
        using var legacy = Config(directory, "legacy", "pip.x=1300\npip.y=40\n");
        legacy.InitializePipPosition(new Size(1920, 1080));
        var migrated = PipLayout.GetBounds(new Size(1920, 1080), legacy.PipScale, legacy.PipLeftPercent, legacy.PipTopPercent);
        Check(migrated.Location == new Point(1300, 40), "Legacy pixels migrate to the same initial position");
        var saved = File.ReadAllText(legacy.Path);
        legacy.InitializePipPosition(new Size(3840, 2160));
        Check(saved == File.ReadAllText(legacy.Path), "Migration runs once and is not repeated on resize");
        using var reloaded = NewConfig(legacy.Path);
        Check(reloaded.PipLeftPercent == legacy.PipLeftPercent && reloaded.PipTopPercent == legacy.PipTopPercent,
            "Migrated percentages survive reload");
        using var mixed = Config(directory, "mixed", "pip.left.percent=10\npip.x=1300\npip.y=54\n");
        mixed.InitializePipPosition(new Size(1920, 1080));
        Check(mixed.PipLeftPercent == 10 && mixed.PipTopPercent == 5, "Explicit percentages override legacy pixels per axis");
        using var outOfBounds = Config(directory, "old-outside", "pip.x=99999\npip.y=-30\n");
        outOfBounds.InitializePipPosition(new Size(1920, 1080));
        var bounds = PipLayout.GetBounds(new Size(1920, 1080), .28, outOfBounds.PipLeftPercent, outOfBounds.PipTopPercent);
        Check(bounds.Right == 1920 && bounds.Y == 0, "Legacy off-screen coordinates migrate from their visible clamped position");
        using var invalid = Config(directory, "invalid", "pip.left.percent=NaN\npip.top.percent=Infinity\n");
        Check(invalid.PipLeftPercent == 70 && invalid.PipTopPercent == 4, "Invalid percentage values use safe defaults");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            var settings = AppSettings.FromConfig(defaults) with { PipLeftPercent = 10.25, PipTopPercent = 5.75 };
            defaults.ApplySettings(settings);
            using var roundTrip = NewConfig(defaults.Path);
            Check(roundTrip.PipLeftPercent == 10.25 && roundTrip.PipTopPercent == 5.75 &&
                File.ReadAllText(defaults.Path).Contains("pip.left.percent=10.25"), "Percentages save and reload independently of decimal locale");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static void TestOptions(string directory)
    {
        using var config = Config(directory, "options", "pip.left.percent=10\npip.top.percent=10\n");
        var initialFile = File.ReadAllText(config.Path);
        AppSettings? preview = null;
        var cancelled = false;
        using (var options = new OptionsForm(AppSettings.FromConfig(config), config, () => {},
            settings => preview = settings, config.ApplySettings, () => cancelled = true, () => {}))
        {
            var left = (NumericUpDown)Field(options, "pipLeftInput")!;
            var top = (NumericUpDown)Field(options, "pipTopInput")!;
            Check(left.Minimum == 0 && left.Maximum == 100 && left.DecimalPlaces == 2 && top.Maximum == 100,
                "Settings accept fractional percentages from 0 to 100");
            left.Value = 12.5M;
            top.Value = 7.25M;
            Check(preview is { PipLeftPercent: 12.5, PipTopPercent: 7.25 } && File.ReadAllText(config.Path) == initialFile,
                "Percentage edits preview without writing the configuration");
            // Initialize the full native layout off-screen for a settings preview.
            options.ShowInTaskbar = false;
            options.Enabled = false;
            options.StartPosition = FormStartPosition.Manual;
            options.Location = new Point(-32000, -32000);
            options.Show();
            options.Enabled = true;
            using var bitmap = new Bitmap(options.Width, options.Height);
            options.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "settings-preview.png"));
            Call(options, "CancelAndClose");
            Check(cancelled && File.ReadAllText(config.Path) == initialFile, "Cancel leaves saved percentage settings unchanged");
        }
        using var saveOptions = new OptionsForm(AppSettings.FromConfig(config), config, () => {},
            _ => {}, config.ApplySettings, () => {}, () => {});
        ((NumericUpDown)Field(saveOptions, "pipLeftInput")!).Value = 12.5M;
        ((NumericUpDown)Field(saveOptions, "pipTopInput")!).Value = 7.25M;
        Call(saveOptions, "SaveAndClose");
        Check(config.PipLeftPercent == 12.5 && config.PipTopPercent == 7.25,
            "Save persists the chosen percentages");
    }

    private static AppConfig Config(string directory, string name, string text)
    {
        var path = Path.Combine(directory, name + ".properties");
        File.WriteAllText(path, text);
        return NewConfig(path);
    }

    private static AppConfig NewConfig(string path) => (AppConfig)typeof(AppConfig)
        .GetConstructors(PrivateInstance).Single().Invoke(new object[] { path, PropertiesFile.Load(path) });

    private static object? Field(object target, string name) => target.GetType().GetField(name, PrivateInstance)!.GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, PrivateInstance)!.Invoke(target, null);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}");
    }
}
