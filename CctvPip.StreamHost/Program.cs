using LibVLCSharp.Shared;

namespace CctvPip.StreamHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var options = HostOptions.Parse(args);
            Core.Initialize();
            Application.Run(new StreamHostForm(options));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Stream host could not start.{Environment.NewLine}{ex.Message}",
                "CCTV Stream Host",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
