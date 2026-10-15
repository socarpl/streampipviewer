namespace CctvPip.App;

internal sealed record AppSettings(
    string Cctv1Url,
    string Cctv2Url,
    double PipLeftPercent,
    double PipTopPercent,
    double PipScale,
    Color PipBorderColor,
    int PipBorderSize)
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
            config.PipLeftPercent,
            config.PipTopPercent,
            config.PipScale,
            config.PipBorderColor,
            config.PipBorderSize);
    }
}
