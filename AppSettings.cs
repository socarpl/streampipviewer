namespace CctvPip.App;

internal sealed record AppSettings(
    string Cctv1Url,
    string Cctv2Url,
    int PipX,
    int PipY,
    double PipScale,
    Color PipBorderColor)
{
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
