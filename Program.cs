using LibVLCSharp.Shared;

namespace CctvPip.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            Core.Initialize();
            using var config = AppConfig.LoadOrCreate();
            Application.Run(new MainForm(config));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Stream PIP Viewer could not start.{Environment.NewLine}{ex.Message}",
                "Stream PIP Viewer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
