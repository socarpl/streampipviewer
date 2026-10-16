using System.Globalization;

namespace CctvPip.App;

internal static class SettingsFileOperations
{
    private static readonly string[] VisibleKeys =
    {
        "cctv1.url", "cctv2.url", "pip.scale", "pip.border.color", "pip.left.percent", "pip.top.percent"
    };

    /// <summary>Reads only the settings exposed in the dialog. Missing keys retain the supplied values.</summary>
    public static AppSettings ReadVisibleSettings(string path, AppSettings fallback)
    {
        var file = ReadRequired(path);
        if (!VisibleKeys.Any(key => file.Get(key) is not null))
        {
            throw new FormatException("This file does not contain any supported dialog settings.");
        }

        var color = fallback.PipBorderColor;
        if (file.Get("pip.border.color") is { } colorText)
        {
            if (!TryParseHexColor(colorText, out color))
            {
                try { color = ColorTranslator.FromHtml(colorText); }
                catch { throw new FormatException("pip.border.color must contain a valid RGB color."); }
                if (color.A != 255) throw new FormatException("pip.border.color must contain an opaque RGB color.");
            }
        }

        return fallback with
        {
            Cctv1Url = file.Get("cctv1.url")?.Trim() ?? fallback.Cctv1Url,
            Cctv2Url = file.Get("cctv2.url")?.Trim() ?? fallback.Cctv2Url,
            PipScale = ReadNumber(file, "pip.scale", fallback.PipScale, 1),
            PipLeftPercent = ReadNumber(file, "pip.left.percent", fallback.PipLeftPercent, 100),
            PipTopPercent = ReadNumber(file, "pip.top.percent", fallback.PipTopPercent, 100),
            PipBorderColor = color
        };
    }

    /// <summary>Uses the latest disk contents as the base, preserving all settings absent from the dialog.</summary>
    public static PropertiesFile MergeVisibleSettings(string activePath, AppSettings settings)
    {
        var file = ReadRequired(activePath);
        file.Set("cctv1.url", settings.Cctv1Url);
        file.Set("cctv2.url", settings.Cctv2Url);
        file.Set("pip.scale", settings.PipScale.ToString("0.####", CultureInfo.InvariantCulture));
        file.Set("pip.left.percent", settings.PipLeftPercent.ToString("0.##", CultureInfo.InvariantCulture));
        file.Set("pip.top.percent", settings.PipTopPercent.ToString("0.##", CultureInfo.InvariantCulture));
        file.Set("pip.border.color", "#" + HexColor(settings.PipBorderColor));
        file.Remove("pip.x");
        file.Remove("pip.y");
        return file;
    }

    public static void Export(string activePath, string destinationPath, AppSettings settings)
    {
        if (!string.Equals(Path.GetExtension(destinationPath), ".properties", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The backup file must use the .properties extension.");
        }
        if (string.Equals(Path.GetFullPath(activePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Choose a backup file different from the active properties file.");
        }

        MergeVisibleSettings(activePath, settings).Save(destinationPath);
    }

    public static bool TryParseHexColor(string text, out Color color)
    {
        var hex = text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    public static string HexColor(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static PropertiesFile ReadRequired(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The properties file could not be found.", path);
        return PropertiesFile.Load(path);
    }

    private static double ReadNumber(PropertiesFile file, string key, double fallback, double maximum)
    {
        if (file.Get(key) is not { } text) return fallback;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number) || number < 0 || number > maximum)
        {
            throw new FormatException($"{key} must be a number between 0 and {maximum}.");
        }
        return number;
    }
}
