namespace CctvPip.StreamHost;

internal sealed record HostOptions(
    string Label,
    string Url,
    string[] LibVlcOptions,
    string[] MediaOptions,
    bool StartMuted,
    bool Hosted)
{
    private const string DefaultCctv1Url = "rtsp://192.168.0.999:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif";
    private const string DefaultCctv2Url = "rtsp://192.168.0.998:554/cam/realmonitor?channel=1&subtype=0&unicast=true&proto=Onvif";

    public static HostOptions Parse(string[] args)
    {
        var values = Args.Parse(args);
        var stream = values.GetValueOrDefault("stream", "cctv1").Trim().ToLowerInvariant();
        var configPath = values.GetValueOrDefault("config", Path.Combine(AppContext.BaseDirectory, "cctv-pip.properties"));
        var properties = File.Exists(configPath)
            ? PropertiesReader.Load(configPath)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var label = values.GetValueOrDefault("label", stream.Equals("cctv2", StringComparison.OrdinalIgnoreCase) ? "CCTV2 Host" : "CCTV1 Host");
        var url = values.GetValueOrDefault("url", GetStreamValue(properties, stream, "url", stream == "cctv2" ? DefaultCctv2Url : DefaultCctv1Url));
        var libVlcOptions = SplitOptions(values.GetValueOrDefault("libvlc-options", GetProperty(properties, "libvlc.options", "--no-video-title-show,--avcodec-hw=any")));
        var mediaOptions = SplitOptions(values.GetValueOrDefault("media-options", GetStreamValue(properties, stream, "player.options", ":rtsp-tcp,:network-caching=300,:live-caching=300")));
        var startMuted = !values.ContainsKey("unmuted");
        var hosted = values.ContainsKey("hosted");

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("A stream URL is required. Pass --url <rtsp-url> or provide cctv1.url/cctv2.url in config.");
        }

        return new HostOptions(label, url, libVlcOptions, mediaOptions, startMuted, hosted);
    }

    private static string GetStreamValue(IReadOnlyDictionary<string, string> properties, string stream, string keySuffix, string fallback)
    {
        return GetProperty(properties, $"{stream}.{keySuffix}", fallback);
    }

    private static string GetProperty(IReadOnlyDictionary<string, string> properties, string key, string fallback)
    {
        return properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string[] SplitOptions(string options)
    {
        return options
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .ToArray();
    }
}

internal static class Args
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (!values.ContainsKey("url"))
                {
                    values["url"] = arg;
                }

                continue;
            }

            var key = arg[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = "true";
                continue;
            }

            values[key] = args[++i];
        }

        return values;
    }
}

internal static class PropertiesReader
{
    public static Dictionary<string, string> Load(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            values[key] = value;
        }

        return values;
    }
}
