namespace CctvPip.App;

internal sealed record AppSettings(
    string Cctv1Url,
    string Cctv2Url,
    int PipX,
    int PipY,
    double PipScale,
    Color PipBorderColor)
{
    /// <summary>
    /// Creates a runtime settings snapshot from the persisted application configuration.
    /// </summary>
    /// <param name="config">The loaded application configuration that supplies stream URLs and PIP layout values.</param>
    /// <returns>A settings snapshot that can be edited or previewed without immediately writing changes to disk.</returns>
    public static AppSettings FromConfig(AppConfig config)
    {
        return new AppSettings(
            config.Cctv1Url,
            config.Cctv2Url,
            config.PipX,
            config.PipY,
            config.PipScale,
            config.PipBorderColor);
    }
}
