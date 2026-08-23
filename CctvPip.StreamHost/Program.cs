using LibVLCSharp.Shared;

namespace CctvPip.StreamHost;

internal static class Program
{
    /// <summary>
    /// Parses host command-line options, initializes LibVLC, and runs a single-stream host form.
    /// </summary>
    /// <param name="args">Command-line arguments such as <c>--stream</c>, <c>--config</c>, <c>--hosted</c>, and <c>--unmuted</c>.</param>
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
