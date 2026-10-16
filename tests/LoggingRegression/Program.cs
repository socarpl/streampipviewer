using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CctvPip;
using NLog;

if (args.Length > 0 && args[0] == "--writer")
{
    AppLog.Initialize("Worker");
    for (var i = 0; i < 350; i++) AppLog.Info("Worker", new string('x', 32000));
    AppLog.Shutdown("Worker");
    return;
}

var root = Path.Combine(AppContext.BaseDirectory, "sandbox", Guid.NewGuid().ToString("N"));
var logs = Path.Combine(root, "logs");
Directory.CreateDirectory(logs);
Environment.SetEnvironmentVariable(AppLog.DirectoryEnvironment, logs);
Environment.SetEnvironmentVariable(AppLog.SessionEnvironment, null);
LogManager.ThrowExceptions = true;
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
long Total()
{
    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(logs).ToUpperInvariant())));
    using var mutex = new Mutex(false, @"Local\StreamPipLogs_" + key);
    mutex.WaitOne();
    try { return Directory.GetFiles(logs, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
    finally { mutex.ReleaseMutex(); }
}
void Sparse(string name, long length, int age)
{
    var path = Path.Combine(logs, name);
    using (var stream = File.Create(path)) stream.SetLength(length);
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-age));
}

try
{
    Sparse("20200101000000_streampipviewer_log.txt", 3 * 1024 * 1024, 3);
    Sparse("20200102000000_streampipviewer_log.txt", 23 * 1024 * 1024, 2);
    AppLog.Initialize("Test");
    Check(!File.Exists(Path.Combine(logs, "20200101000000_streampipviewer_log.txt")), "Oldest file was not removed on startup");
    Check(File.Exists(Path.Combine(logs, "20200102000000_streampipviewer_log.txt")), "Newer file unnecessarily removed");
    Check(Total() <= BudgetFileTarget.MaximumDirectoryBytes, "Startup cap exceeded");
    var session = AppLog.SessionFile;
    Check(Regex.IsMatch(session, @"^\d{14}_streampipviewer_log\.txt$"), "Wrong filename");
    Check(session.StartsWith(DateTime.Now.ToString("yyyyMMdd")), "Session date incorrect");
    AppLog.Info("Test", "Unicode: 日本語 🐈");
    var secret = "rtsp://private:password@camera/stream";
    AppLog.Error("Test", "Safe failure", new InvalidOperationException(secret));
    var content = File.ReadAllText(Path.Combine(logs, session));
    Check(content.Contains("日本語 🐈"), "UTF-8 lost");
    Check(!content.Contains(secret) && content.Contains("InvalidOperationException"), "Exception secret leaked or diagnostics missing");
    Check(content.Contains("|INFO|") && content.Contains("|PID "), "Missing event metadata");
    for (var i = 0; i < 100; i++)
    {
        AppLog.Info("Test", new string('é', 16000));
        if (Total() > BudgetFileTarget.MaximumDirectoryBytes) throw new Exception("Cap exceeded while writing");
    }
    checks++;
    Check(!File.Exists(Path.Combine(logs, "20200102000000_streampipviewer_log.txt")), "Old logs not removed as new logs grew");
    Check(Directory.GetFiles(logs).Length > 1, "No rotation");
    Check(Directory.GetFiles(logs).All(f => new FileInfo(f).Length <= 1024 * 1024), "Rotation exceeded 1 MB");

    // Three processes append to the same initial file and rotate concurrently under one directory mutex.
    Environment.SetEnvironmentVariable(AppLog.SessionEnvironment, session);
    var workers = Enumerable.Range(0, 3).Select(_ => Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false, ArgumentList = { "--writer" }
    })!).ToArray();
    while (workers.Any(p => !p.HasExited))
    {
        Check(Total() <= BudgetFileTarget.MaximumDirectoryBytes, "Concurrent writers exceeded cap");
        Thread.Sleep(20);
    }
    Check(workers.All(p => p.ExitCode == 0), "Worker failed");
    foreach (var worker in workers) worker.Dispose();
    Check(Total() <= BudgetFileTarget.MaximumDirectoryBytes, "Final cap exceeded");
    Check(Directory.GetFiles(logs).All(f => Regex.IsMatch(Path.GetFileName(f), @"^\d{14}_streampipviewer_log\.txt$")), "Rotation filename incorrect");
    Check(Directory.GetFiles(logs).SelectMany(File.ReadLines).Any(l => l.Contains("|Worker|")), "Helper logs missing");
    AppLog.Shutdown("Test");

    Environment.SetEnvironmentVariable(AppLog.SessionEnvironment, null);
    AppLog.Initialize("Test");
    Check(session != AppLog.SessionFile, "New launch reused existing session filename");
    AppLog.Shutdown("Test");

    File.WriteAllText(Path.Combine(root, "logging.config"), """
        <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <targets><target xsi:type="BudgetFile" name="session" /></targets>
          <rules><logger name="*" minlevel="Error" writeTo="session" /></rules>
        </nlog>
        """);
    AppLog.Initialize("Test");
    AppLog.Info("Test", "Filtered marker");
    AppLog.Error("Test", "Error marker", new Exception("private"));
    var filtered = File.ReadAllText(Path.Combine(logs, AppLog.SessionFile));
    Check(!filtered.Contains("Filtered marker") && filtered.Contains("Error marker"), "Separate logging config not applied");
    AppLog.Shutdown("Test");

    File.WriteAllText(Path.Combine(root, "logging.config"), "<invalid");
    AppLog.Initialize("Test");
    Check(File.ReadAllText(Path.Combine(logs, AppLog.SessionFile)).Contains("using default logging"), "Malformed config did not fall back");
    AppLog.Shutdown("Test");

    // Fill the entire budget with a file that cannot be deleted while open.
    logs = Path.Combine(root, "locked-logs");
    Directory.CreateDirectory(logs);
    Environment.SetEnvironmentVariable(AppLog.DirectoryEnvironment, logs);
    using (var locked = new FileStream(Path.Combine(logs, "locked.txt"), FileMode.Create, FileAccess.Write, FileShare.Read))
    {
        locked.SetLength(BudgetFileTarget.MaximumDirectoryBytes);
        AppLog.Initialize("Test");
        AppLog.Info("Test", "Must be skipped");
        Check(Total() == BudgetFileTarget.MaximumDirectoryBytes, "Locked file caused budget overflow");
    }
    AppLog.Info("Test", "Logging resumes");
    Check(File.ReadAllText(Path.Combine(logs, AppLog.SessionFile)).Contains("Logging resumes"), "Logging did not resume after file unlocked");
    Check(Total() <= BudgetFileTarget.MaximumDirectoryBytes, "Recovered logging exceeded cap");
    AppLog.Shutdown("Test");
    Console.WriteLine($"Logging regression passed ({checks} checks, including live concurrent cap checks).");
}
finally
{
    LogManager.Shutdown();
    Environment.SetEnvironmentVariable(AppLog.DirectoryEnvironment, null);
    Environment.SetEnvironmentVariable(AppLog.SessionEnvironment, null);
    Directory.Delete(root, recursive: true);
}
