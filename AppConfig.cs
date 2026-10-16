using System.Diagnostics;
using System.Globalization;

namespace CctvPip.App;

internal sealed class AppConfig : IDisposable
{
    public const string FileName = "stream-pip.config";
    private const string LegacyFileName = "cctv-pip.properties";

    private PropertiesFile properties;

    /// <summary>
    /// Creates an application configuration wrapper around a loaded properties file, ensures missing defaults, and saves the result.
    /// </summary>
    /// <param name="path">The full path to the configuration file represented by this instance.</param>
    /// <param name="properties">The parsed property entries loaded from disk or an empty property set.</param>
    private AppConfig(string path, PropertiesFile properties)
    {
        Path = path;
        this.properties = properties;
        EnsureDefaults();
        Save();
    }

    public string Path { get; }

    public string Cctv1Url
    {
        get => Get("cctv1.url");
        set => Set("cctv1.url", value);
    }

    public string Cctv2Url
    {
        get => Get("cctv2.url");
        set => Set("cctv2.url", value);
    }

    public string Cctv1PlayerOptions
    {
        get => Get("cctv1.player.options");
        set => Set("cctv1.player.options", value);
    }

    public string Cctv2PlayerOptions
    {
        get => Get("cctv2.player.options");
        set => Set("cctv2.player.options", value);
    }

    public Color PipBorderColor
    {
        get => ParseColor(Get("pip.border.color"), Color.White);
        set => Set("pip.border.color", ColorTranslator.ToHtml(value));
    }

    public int PipBorderSize
    {
        get => Math.Clamp(GetInt("pip.border.size", 2), 0, 100);
        set => Set("pip.border.size", Math.Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture));
    }

    public double PipLeftPercent
    {
        get => Math.Clamp(GetDouble("pip.left.percent", 70), 0, 100);
        set => Set("pip.left.percent", Math.Clamp(value, 0, 100).ToString("0.###", CultureInfo.InvariantCulture));
    }

    public double PipTopPercent
    {
        get => Math.Clamp(GetDouble("pip.top.percent", 4), 0, 100);
        set => Set("pip.top.percent", Math.Clamp(value, 0, 100).ToString("0.###", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Converts legacy pixel coordinates once, preserving their clamped position in the initial video area.
    /// Existing percentage values take precedence; obsolete pixel keys are removed after conversion.
    /// </summary>
    public void InitializePipPosition(Size initialMainSize)
    {
        if (initialMainSize.Width <= 0 || initialMainSize.Height <= 0)
        {
            return;
        }

        var pipSize = PipLayout.GetBounds(initialMainSize, PipScale, 0, 0).Size;
        var changed = false;
        if (properties.Get("pip.left.percent") is null)
        {
            var x = Math.Clamp(GetInt("pip.x", 1450), 0, initialMainSize.Width - pipSize.Width);
            SetWithoutSave("pip.left.percent", (x * 100.0 / initialMainSize.Width).ToString("0.###", CultureInfo.InvariantCulture));
            changed = true;
        }
        if (properties.Get("pip.top.percent") is null)
        {
            var y = Math.Clamp(GetInt("pip.y", 40), 0, initialMainSize.Height - pipSize.Height);
            SetWithoutSave("pip.top.percent", (y * 100.0 / initialMainSize.Height).ToString("0.###", CultureInfo.InvariantCulture));
            changed = true;
        }
        changed |= properties.Remove("pip.x");
        changed |= properties.Remove("pip.y");
        if (changed)
        {
            Save();
        }
    }

    public double PipScale
    {
        get => GetDouble("pip.scale", 0.28);
        set => Set("pip.scale", value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    public int ReconnectAttempts
    {
        get => Math.Max(3, GetInt("reconnect.attempts", 3));
        set => Set("reconnect.attempts", Math.Max(3, value).ToString(CultureInfo.InvariantCulture));
    }

    public int ReconnectDelayMs
    {
        get => Math.Max(250, GetInt("reconnect.delay.ms", 1500));
        set => Set("reconnect.delay.ms", Math.Max(250, value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Loads the application configuration from the executable directory, or falls back to local application data if that path cannot be written.
    /// </summary>
    /// <returns>A ready-to-use configuration object with default keys present.</returns>
    public static AppConfig LoadOrCreate()
    {
        var basePath = AppContext.BaseDirectory;
        try
        {
            return LoadFromDirectory(basePath);
        }
        catch (Exception)
        {
            var localDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CctvPip");

            return LoadFromDirectory(localDirectory);
        }
    }

    private static AppConfig LoadFromDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, FileName);
        var legacyPath = System.IO.Path.Combine(directory, LegacyFileName);
        if (!File.Exists(path) && File.Exists(legacyPath))
        {
            File.Move(legacyPath, path);
        }
        return new AppConfig(path, PropertiesFile.Load(path));
    }

    /// <summary>
    /// Builds a stream definition for the requested camera, including label, URL, and parsed player options.
    /// </summary>
    /// <param name="id">The configured stream identifier to resolve.</param>
    /// <returns>The stream definition used by the playback layer.</returns>
    public StreamDefinition GetStream(StreamId id)
    {
        return id == StreamId.Cctv1
            ? new StreamDefinition("CCTV1", Cctv1Url, SplitOptions(Cctv1PlayerOptions))
            : new StreamDefinition("CCTV2", Cctv2Url, SplitOptions(Cctv2PlayerOptions));
    }

    /// <summary>
    /// Saves dialog settings while preserving the latest disk values for settings without controls.
    /// </summary>
    /// <param name="settings">The settings selected in the options dialog.</param>
    public void ApplySettings(AppSettings settings)
    {
        var updated = SettingsFileOperations.MergeVisibleSettings(Path, settings);
        updated.Save(Path);
        properties = updated;
    }

    /// <summary>
    /// Writes the current in-memory properties to the active configuration file path.
    /// </summary>
    public void Save()
    {
        properties.Save(Path);
    }

    /// <summary>
    /// Opens the active configuration file in Notepad.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the operating system cannot launch the editor for the config file.</exception>
    public void OpenInNotepad()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "notepad.exe",
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add(Path);
            using var _ = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not open config file: {Path}{Environment.NewLine}{ex.Message}", ex);
        }
    }

    /// <summary>
    /// Settings are saved explicitly; closing the app must not overwrite external file edits.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Reads a raw string property value.
    /// </summary>
    /// <param name="key">The property key to read.</param>
    /// <returns>The configured value, or an empty string when the key is missing.</returns>
    private string Get(string key)
    {
        return properties.Get(key) ?? "";
    }

    /// <summary>
    /// Updates a property value and immediately saves the configuration file.
    /// </summary>
    /// <param name="key">The property key to update.</param>
    /// <param name="value">The value to store for the key.</param>
    private void Set(string key, string value)
    {
        properties.Set(key, value);
        Save();
    }

    /// <summary>
    /// Updates a property value without saving, allowing a caller to batch several changes before one save.
    /// </summary>
    /// <param name="key">The property key to update.</param>
    /// <param name="value">The value to store for the key.</param>
    private void SetWithoutSave(string key, string value)
    {
        properties.Set(key, value);
    }

    /// <summary>
    /// Reads an integer property using invariant-culture parsing and a fallback for missing or invalid values.
    /// </summary>
    /// <param name="key">The property key containing an integer value.</param>
    /// <param name="fallback">The value to return when parsing fails.</param>
    /// <returns>The parsed integer or the fallback value.</returns>
    private int GetInt(string key, int fallback)
    {
        return int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    /// <summary>
    /// Reads a floating-point property using invariant-culture parsing and a fallback for missing or invalid values.
    /// </summary>
    /// <param name="key">The property key containing a floating-point value.</param>
    /// <param name="fallback">The value to return when parsing fails.</param>
    /// <returns>The parsed floating-point value or the fallback value.</returns>
    private double GetDouble(string key, double fallback)
    {
        return double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : fallback;
    }

    /// <summary>
    /// Converts a configured HTML color string into a WinForms color, protecting callers from invalid color text.
    /// </summary>
    /// <param name="value">The configured color value, usually an HTML color such as <c>#FFFFFF</c>.</param>
    /// <param name="fallback">The color to return when the configured value is blank or invalid.</param>
    /// <returns>The parsed color or the fallback color.</returns>
    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : ColorTranslator.FromHtml(value);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Splits a comma-separated player option string into individual non-empty options.
    /// </summary>
    /// <param name="options">The comma-separated options text from configuration.</param>
    /// <returns>An array of trimmed option values.</returns>
    private static string[] SplitOptions(string options)
    {
        return options
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .ToArray();
    }

    /// <summary>
    /// Adds every required configuration key with its default value when that key is missing.
    /// </summary>
    private void EnsureDefaults()
    {
        properties.Ensure("cctv1.url", "rtsp://192.168.0.999:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif");
        properties.Ensure("cctv2.url", "rtsp://192.168.0.998:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif");
        properties.Ensure("pip.border.color", "#FFFFFF");
        properties.Ensure("pip.border.size", "2");
        if (properties.Get("pip.x") is null)
        {
            properties.Ensure("pip.left.percent", "70");
        }
        if (properties.Get("pip.y") is null)
        {
            properties.Ensure("pip.top.percent", "4");
        }
        properties.Ensure("pip.scale", "0.28");
        properties.Ensure("reconnect.attempts", "3");
        properties.Ensure("reconnect.delay.ms", "1500");
        properties.Ensure("player.backend", "libvlc");
        properties.Ensure("libvlc.options", "--no-video-title-show,--avcodec-hw=any");
        properties.Ensure("cctv1.player.options", ":rtsp-tcp,:network-caching=300,:live-caching=300");
        properties.Ensure("cctv2.player.options", ":rtsp-tcp,:network-caching=300,:live-caching=300");
    }
}

internal readonly record struct StreamDefinition(string Label, string Url, string[] PlayerOptions);

internal enum StreamId
{
    Cctv1,
    Cctv2
}
