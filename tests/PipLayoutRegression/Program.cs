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
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data");
        Directory.CreateDirectory(directory);
        try
        {
            TestLayout();
            TestConfiguration(directory);
            TestConfigFileName(directory);
            TestOptions(directory);
            TestSettingsFiles(directory);
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
        Check(!saved.Contains("pip.x=") && !saved.Contains("pip.y=") &&
            saved.Contains("pip.left.percent=") && saved.Contains("pip.top.percent="),
            "Migration replaces pixel keys with percentage keys");
        legacy.InitializePipPosition(new Size(3840, 2160));
        Check(saved == File.ReadAllText(legacy.Path), "Migration runs once and is not repeated on resize");
        using var reloaded = NewConfig(legacy.Path);
        Check(reloaded.PipLeftPercent == legacy.PipLeftPercent && reloaded.PipTopPercent == legacy.PipTopPercent,
            "Migrated percentages survive reload");
        using var mixed = Config(directory, "mixed", "pip.left.percent=10\npip.x=1300\npip.y=54\n");
        mixed.InitializePipPosition(new Size(1920, 1080));
        Check(mixed.PipLeftPercent == 10 && mixed.PipTopPercent == 5, "Explicit percentages override legacy pixels per axis");
        using var migratedAlready = Config(directory, "already-migrated",
            "# Keep this comment\npip.left.percent=10.25\npip.top.percent=5.75\npip.x=100\nPIP.X=1382\npip.y=40\ncustom.setting=keep\n");
        migratedAlready.InitializePipPosition(new Size(1920, 1080));
        var cleaned = File.ReadAllText(migratedAlready.Path);
        Check(!cleaned.Contains("pip.x=", StringComparison.OrdinalIgnoreCase) && !cleaned.Contains("pip.y=", StringComparison.OrdinalIgnoreCase) &&
            migratedAlready.PipLeftPercent == 10.25 && migratedAlready.PipTopPercent == 5.75 &&
            cleaned.Contains("# Keep this comment") && cleaned.Contains("custom.setting=keep"),
            "Existing percentage configs lose all legacy duplicates while preserving settings and comments");
        using var cleanedReloaded = NewConfig(migratedAlready.Path);
        Check(cleanedReloaded.PipLeftPercent == 10.25 && cleanedReloaded.PipTopPercent == 5.75 &&
            !File.ReadAllText(cleanedReloaded.Path).Contains("pip.x=", StringComparison.OrdinalIgnoreCase),
            "Reload does not recreate removed pixel keys");
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
        using (var options = new OptionsForm(AppSettings.FromConfig(config), config,
            settings => preview = settings, config.ApplySettings, () => cancelled = true))
        {
            var left = (NumericUpDown)Field(options, "pipLeftInput")!;
            var top = (NumericUpDown)Field(options, "pipTopInput")!;
            Check(left.Minimum == 0 && left.Maximum == 100 && left.DecimalPlaces == 2 && top.Maximum == 100,
                "Settings accept fractional percentages from 0 to 100");
            var leftSlider = (TrackBar)Field(options, "pipLeftSlider")!;
            var topSlider = (TrackBar)Field(options, "pipTopSlider")!;
            var scaleSlider = (TrackBar)Field(options, "pipScaleSlider")!;
            var scaleInput = (NumericUpDown)Field(options, "pipScaleInput")!;
            left.Value = 22;
            Check(leftSlider.Value == 22, "Changing a position input updates its slider");
            topSlider.Value = 18;
            Check(top.Value == 18, "Changing a position slider updates its numeric input");
            scaleInput.Value = 32.5M;
            Check(scaleSlider.Value == 33 && preview?.PipScale == .325, "Size input previews fine adjustments and updates the slider");
            scaleSlider.Value = 35;
            Check(scaleInput.Value == 35 && preview?.PipScale == .35, "Size slider updates its input and preview");
            var colorText = (TextBox)Field(options, "borderColorTextBox")!;
            var colorButton = (Button)Field(options, "borderColorButton")!;
            colorText.Text = "a1B2c3";
            Check(colorButton.BackColor.ToArgb() == Color.FromArgb(161, 178, 195).ToArgb() &&
                preview?.PipBorderColor.ToArgb() == colorButton.BackColor.ToArgb(), "Valid hex immediately updates the swatch and preview");
            colorText.Text = "BADHEX";
            Check(colorButton.BackColor.ToArgb() == Color.FromArgb(161, 178, 195).ToArgb(), "Invalid hex does not change the swatch");
            Call(options, "CommitColorText");
            Check(colorText.Text == "A1B2C3", "Leaving invalid hex restores the last valid value");
            var toolbar = (ToolStrip)Field(options, "toolbar")!;
            Check(toolbar.Items.Count == 4 && toolbar.Items.Cast<ToolStripItem>().All(item => item.Image is not null) &&
                toolbar.Items[0].ToolTipText == "Open containing folder" && toolbar.Items[1].ToolTipText == "Reload from disk" &&
                toolbar.Items[2].ToolTipText == "Export settings to file" && toolbar.Items[3].ToolTipText == "Import settings from file",
                "Toolbar embeds all four supplied icons with the requested tooltips");
            Check(options.BackColor == SystemColors.Control && options.ForeColor == SystemColors.ControlText,
                "Dialog uses Windows system colors");
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
            options.ActiveControl = null;
            var groupPanels = Descendants(options).OfType<GroupBox>().Where(group => group.Text.StartsWith("PIP ")).ToArray();
            Check(groupPanels.Length == 2 && Math.Abs(groupPanels[0].Width - groupPanels[1].Width) <= 1,
                "Size/border and position groups occupy equal widths");
            using var bitmap = new Bitmap(options.Width, options.Height);
            options.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "settings-preview.png"));
            Call(options, "CancelAndClose");
            Check(cancelled && File.ReadAllText(config.Path) == initialFile, "Cancel leaves saved percentage settings unchanged");
        }
        using var saveOptions = new OptionsForm(AppSettings.FromConfig(config), config,
            _ => {}, config.ApplySettings, () => {});
        ((NumericUpDown)Field(saveOptions, "pipLeftInput")!).Value = 12.5M;
        ((NumericUpDown)Field(saveOptions, "pipTopInput")!).Value = 7.25M;
        Call(saveOptions, "SaveAndClose");
        Check(config.PipLeftPercent == 12.5 && config.PipTopPercent == 7.25,
            "Save persists the chosen percentages");
    }

    private static void TestSettingsFiles(string directory)
    {
        using var config = Config(directory, "files", "pip.left.percent=10\npip.top.percent=20\npip.border.size=6\ncustom.setting=original\n");
        var initial = AppSettings.FromConfig(config);
        AppSettings? preview = null;
        using var options = new OptionsForm(initial, config, settings => preview = settings, config.ApplySettings, () => {});
        var originalFile = File.ReadAllText(config.Path);
        var importPath = Path.Combine(directory, "import.properties");
        File.WriteAllText(importPath, "cctv1.url=rtsp://imported/source\npip.left.percent=33.25\npip.border.color=#123456\npip.border.size=99\ncustom.setting=unwanted\n");
        var importFile = File.ReadAllText(importPath);
        Call(options, "ImportFromFile", importPath);
        Check(preview is { Cctv1Url: "rtsp://imported/source", PipLeftPercent: 33.25, PipTopPercent: 20, PipBorderSize: 6 } &&
            preview.PipBorderColor.ToArgb() == Color.FromArgb(18, 52, 86).ToArgb(),
            "Import changes only visible controls and keeps missing and hidden settings");
        Check(File.ReadAllText(config.Path) == originalFile && File.ReadAllText(importPath) == importFile,
            "Import writes neither the active file nor its source");

        File.AppendAllText(config.Path, "\ncustom.setting=latest-on-disk\nlibvlc.options=--custom-latest\npip.border.size=8\n");
        var externalEdits = File.ReadAllText(config.Path);
        var exportPath = Path.Combine(directory, "backup.properties");
        Call(options, "ExportToFile", exportPath);
        var exported = PropertiesFile.Load(exportPath);
        Check(exported.Get("cctv1.url") == "rtsp://imported/source" && exported.Get("pip.left.percent") == "33.25" &&
            exported.Get("custom.setting") == "latest-on-disk" && exported.Get("libvlc.options") == "--custom-latest" &&
            exported.Get("pip.border.size") == "8", "Export combines visible draft values with all other current disk settings");
        Check(File.ReadAllText(config.Path) == externalEdits, "Backup export leaves the active configuration unchanged");
        try { Call(options, "ExportToFile", config.Path); throw new InvalidOperationException("Active-file export should fail"); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) {}
        Check(File.ReadAllText(config.Path) == externalEdits, "Export cannot overwrite the active file");
        var wrongExtension = Path.Combine(directory, "backup.txt");
        try { Call(options, "ExportToFile", wrongExtension); throw new InvalidOperationException("Wrong extension should fail"); }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) {}
        Check(!File.Exists(wrongExtension), "Export rejects unsupported file extensions");
        var configBackup = Path.Combine(directory, "backup.config");
        Call(options, "ExportToFile", configBackup);
        Check(PropertiesFile.Load(configBackup).Get("pip.left.percent") == "33.25",
            "Export supports the new .config backup format");

        Call(options, "ReloadFromDisk");
        Check(preview is { PipLeftPercent: 10, PipTopPercent: 20 } && preview.Cctv1Url == initial.Cctv1Url,
            "Reload replaces unsaved controls with current disk values");
        var reloadWithoutPosition = PropertiesFile.Load(config.Path);
        reloadWithoutPosition.Remove("pip.left.percent");
        reloadWithoutPosition.Save(config.Path);
        ((NumericUpDown)Field(options, "pipLeftInput")!).Value = 99;
        Call(options, "ReloadFromDisk");
        Check(preview?.PipLeftPercent == initial.PipLeftPercent,
            "Reload discards unsaved values even when a disk key is missing");
        var beforeInvalid = preview;
        File.WriteAllText(importPath, "cctv1.url=rtsp://must-not-apply\npip.top.percent=not-a-number\n");
        try { Call(options, "ImportFromFile", importPath); throw new InvalidOperationException("Invalid import should fail"); }
        catch (TargetInvocationException ex) when (ex.InnerException is FormatException) {}
        Check(preview == beforeInvalid, "Invalid import applies no partial changes");

        ((NumericUpDown)Field(options, "pipScaleInput")!).Value = 12.34M;
        ((NumericUpDown)Field(options, "pipLeftInput")!).Value = 44.5M;
        Call(options, "SaveAndClose");
        var saved = PropertiesFile.Load(config.Path);
        Check(config.PipLeftPercent == 44.5 && config.PipScale == .1234 && config.PipBorderSize == 8 &&
            saved.Get("custom.setting") == "latest-on-disk", "Save persists visible values and preserves hidden settings edited on disk");
        File.AppendAllText(config.Path, "\ncustom.setting=after-dialog\n");
        var afterDialog = File.ReadAllText(config.Path);
        config.Dispose();
        Check(File.ReadAllText(config.Path) == afterDialog, "Closing configuration does not overwrite Notepad edits");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void TestConfigFileName(string directory)
    {
        var configDirectory = Path.Combine(directory, "filename-migration");
        Directory.CreateDirectory(configDirectory);
        var oldPath = Path.Combine(configDirectory, "cctv-pip.properties");
        var newPath = Path.Combine(configDirectory, "stream-pip.config");
        File.WriteAllText(oldPath, "# preserved\npip.left.percent=23\ncustom.setting=keep\n");
        try
        {
            AppConfig Load() => (AppConfig)typeof(AppConfig).GetMethod("LoadFromDirectory",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { configDirectory })!;
            using (var migrated = Load())
            {
                Check(migrated.Path == newPath && migrated.PipLeftPercent == 23 && !File.Exists(oldPath),
                    "Legacy filename migrates to stream-pip.config without losing settings");
                Check(File.ReadAllText(newPath).Contains("# preserved") && PropertiesFile.Load(newPath).Get("custom.setting") == "keep",
                    "Filename migration preserves comments and hidden settings");
            }
            File.WriteAllText(oldPath, "pip.left.percent=99\n");
            using var existing = Load();
            Check(existing.Path == newPath && existing.PipLeftPercent == 23 && File.Exists(oldPath),
                "Existing stream-pip.config takes precedence over the legacy file");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(configDirectory)) File.Delete(file);
            Directory.Delete(configDirectory);
        }
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
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, PrivateInstance)!.Invoke(target, args);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}");
    }
}
