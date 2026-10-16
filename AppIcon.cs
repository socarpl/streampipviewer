namespace CctvPip;

internal static class AppIcon
{
    public static Icon Value { get; } = Load();

    private static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("CctvPip.pip.ico")
            ?? throw new InvalidOperationException("The application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
