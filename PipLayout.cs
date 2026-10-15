namespace CctvPip.App;

internal static class PipLayout
{
    private const double AspectRatio = 16.0 / 9.0;

    /// <summary>
    /// Positions the PIP's top-left corner as percentages of the main video area,
    /// clamping only the displayed bounds so resizing never changes the requested position.
    /// </summary>
    public static Rectangle GetBounds(Size mainSize, double scale, double leftPercent, double topPercent)
    {
        if (mainSize.Width <= 0 || mainSize.Height <= 0)
        {
            return Rectangle.Empty;
        }

        scale = double.IsFinite(scale) ? Math.Clamp(scale, 0, 1) : 0.28;
        var width = Math.Min(mainSize.Width, Math.Max(160, (int)Math.Round(mainSize.Width * scale)));
        var height = Math.Max(1, (int)Math.Round(width / AspectRatio));
        if (height > mainSize.Height)
        {
            height = mainSize.Height;
            width = Math.Min(mainSize.Width, Math.Max(1, (int)Math.Round(height * AspectRatio)));
        }

        leftPercent = double.IsFinite(leftPercent) ? Math.Clamp(leftPercent, 0, 100) : 0;
        topPercent = double.IsFinite(topPercent) ? Math.Clamp(topPercent, 0, 100) : 0;
        var x = Math.Clamp((int)Math.Round(mainSize.Width * leftPercent / 100), 0, mainSize.Width - width);
        var y = Math.Clamp((int)Math.Round(mainSize.Height * topPercent / 100), 0, mainSize.Height - height);
        return new Rectangle(x, y, width, height);
    }
}
