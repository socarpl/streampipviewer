using NLog;
using NLog.Config;

namespace CctvPip;

internal static class AppLog
{
    internal const string DirectoryEnvironment = "STREAM_PIP_LOG_DIRECTORY";
    internal const string SessionEnvironment = "STREAM_PIP_LOG_SESSION";
    public static string DirectoryPath { get; private set; } = "";
    public static string SessionFile { get; private set; } = "";

    public static void Initialize(string component)
    {
        var inheritedDirectory = Environment.GetEnvironmentVariable(DirectoryEnvironment);
        DirectoryPath = Path.GetFullPath(inheritedDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs"));
        var configDirectory = inheritedDirectory is null ? AppContext.BaseDirectory : Path.GetDirectoryName(DirectoryPath)!;
        LogManager.Setup().SetupExtensions(e => e.RegisterTarget<BudgetFileTarget>("BudgetFile"));
        var path = Path.Combine(configDirectory, "logging.config");
        LoggingConfiguration configuration;
        Exception? configError = null;
        try
        {
            configuration = File.Exists(path) ? new XmlLoggingConfiguration(path) : new LoggingConfiguration();
        }
        catch (Exception error) when (error is NLogConfigurationException or System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            configuration = new LoggingConfiguration();
            configError = error;
        }
        var target = configuration.FindTargetByName<BudgetFileTarget>("session");
        if (target is null)
        {
            target = new BudgetFileTarget { Name = "session" };
            configuration.AddRule(LogLevel.Info, LogLevel.Fatal, target);
        }
        target.DirectoryPath = DirectoryPath;
        target.SessionFile = Environment.GetEnvironmentVariable(SessionEnvironment) ?? "";
        LogManager.Configuration = configuration;
        SessionFile = target.SessionFile;
        if (configError is not null) Error(component, "Invalid logging.config; using default logging", configError);
        Info(component, $"Started; version {typeof(AppLog).Assembly.GetName().Version}; PID {Environment.ProcessId}");
    }

    public static void Info(string component, string message) => LogManager.GetLogger(component).Info(message);
    public static void Warn(string component, string message) => LogManager.GetLogger(component).Warn(message);

    // Exception messages can contain camera URLs/passwords. Record type, error code and call stack only.
    public static void Error(string component, string message, Exception exception) =>
        LogManager.GetLogger(component).Error($"{message}: {exception.GetType().FullName} (0x{exception.HResult:X8}){Environment.NewLine}{exception.StackTrace}");

    public static void Shutdown(string component)
    {
        Info(component, "Stopped");
        LogManager.Shutdown();
    }
}
