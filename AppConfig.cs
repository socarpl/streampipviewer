using System.Diagnostics;
using System.Globalization;

namespace CctvPip.App;

internal sealed class AppConfig : IDisposable
{
    public const string FileName = "cctv-pip.properties";

    private readonly PropertiesFile properties;

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

    public StreamDefinition GetStream(StreamId id)
    {
        return id == StreamId.Cctv1
            ? new StreamDefinition("CCTV1", Cctv1Url, SplitOptions(Cctv1PlayerOptions))
            : new StreamDefinition("CCTV2", Cctv2Url, SplitOptions(Cctv2PlayerOptions));
    }

    public void ApplySettings(AppSettings settings)
    {
        SetWithoutSave("cctv1.url", settings.Cctv1Url);
        SetWithoutSave("cctv2.url", settings.Cctv2Url);
        SetWithoutSave("pip.border.color", ColorTranslator.ToHtml(settings.PipBorderColor));
        SetWithoutSave("pip.x", settings.PipX.ToString(CultureInfo.InvariantCulture));
        SetWithoutSave("pip.y", settings.PipY.ToString(CultureInfo.InvariantCulture));
        SetWithoutSave("pip.scale", settings.PipScale.ToString("0.###", CultureInfo.InvariantCulture));
        Save();
    }

    public void Save()
    {
        properties.Save(Path);
    }

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

    public void Dispose()
    {
        Save();
    }

    private string Get(string key)
    {
        return properties.Get(key) ?? "";
    }

    private void Set(string key, string value)
    {
        properties.Set(key, value);
        Save();
    }

    private void SetWithoutSave(string key, string value)
    {
        properties.Set(key, value);
    }

    private int GetInt(string key, int fallback)
    {
        return int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private double GetDouble(string key, double fallback)
    {
        return double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

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

    private static string[] SplitOptions(string options)
    {
        return options
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .ToArray();
    }

    private void EnsureDefaults()
    {
        properties.Ensure("cctv1.url", "rtsp://192.168.0.999:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif");
        properties.Ensure("cctv2.url", "rtsp://192.168.0.998:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif");
        properties.Ensure("pip.border.color", "#FFFFFF");
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
