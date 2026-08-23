using System.Diagnostics;
using System.Globalization;

namespace CctvPip.App;

internal sealed class AppConfig : IDisposable
{
    public const string FileName = "cctv-pip.properties";

    private readonly PropertiesFile properties;

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

    public int PipX
    {
        get => GetInt("pip.x", 1450);
        set => Set("pip.x", value.ToString(CultureInfo.InvariantCulture));
    }

    public int PipY
    {
        get => GetInt("pip.y", 40);
        set => Set("pip.y", value.ToString(CultureInfo.InvariantCulture));
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
        var appPath = System.IO.Path.Combine(basePath, FileName);

        try
        {
            Directory.CreateDirectory(basePath);
            return new AppConfig(appPath, PropertiesFile.Load(appPath));
        }
        catch (Exception)
        {
            var localPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CctvPip",
                FileName);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localPath)!);
            return new AppConfig(localPath, PropertiesFile.Load(localPath));
        }
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
    /// Applies a settings snapshot to the backing properties file and persists the combined changes once.
    /// </summary>
    /// <param name="settings">The settings selected in the options dialog.</param>
    public void ApplySettings(AppSettings settings)
    {
        SetWithoutSave("cctv1.url", settings.Cctv1Url);
        SetWithoutSave("cctv2.url", settings.Cctv2Url);
        SetWithoutSave("pip.border.color", ColorTranslator.ToHtml(settings.PipBorderColor));
        SetWithoutSave("pip.border.size", Math.Clamp(settings.PipBorderSize, 0, 100).ToString(CultureInfo.InvariantCulture));
        SetWithoutSave("pip.x", settings.PipX.ToString(CultureInfo.InvariantCulture));
        SetWithoutSave("pip.y", settings.PipY.ToString(CultureInfo.InvariantCulture));
        SetWithoutSave("pip.scale", settings.PipScale.ToString("0.###", CultureInfo.InvariantCulture));
        Save();
    }

    /// <summary>
    /// Writes the current in-memory properties to the active configuration file path.
    /// </summary>
    public void Save()
    {
        properties.Save(Path);
    }

    /// <summary>
    /// Opens the active configuration file in the operating system's default editor.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the operating system cannot launch the editor for the config file.</exception>
    public void OpenInDefaultEditor()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not open config file: {Path}{Environment.NewLine}{ex.Message}", ex);
        }
    }

    /// <summary>
    /// Persists any pending configuration values before releasing this configuration instance.
    /// </summary>
    public void Dispose()
    {
        Save();
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
        return double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
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
        properties.Ensure("pip.x", "1450");
        properties.Ensure("pip.y", "40");
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
