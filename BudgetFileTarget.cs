using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NLog.Targets;

namespace CctvPip;

/// <summary>NLog target with a shared, cross-process disk budget (not a per-file approximation).</summary>
[Target("BudgetFile")]
public sealed class BudgetFileTarget : TargetWithLayout
{
    public const long MaximumDirectoryBytes = 25 * 1024 * 1024;
    private const long RollBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly Regex FilePattern = new(@"^\d{14}_streampipviewer_log\.txt$", RegexOptions.CultureInvariant);
    private Mutex? mutex;
    public string DirectoryPath { get; set; } = "";
    public string SessionFile { get; set; } = "";

    public BudgetFileTarget()
    {
        Layout = "${longdate}|${level:uppercase=true}|PID ${processid}|${logger}|${message}";
    }

    protected override void InitializeTarget()
    {
        base.InitializeTarget();
        DirectoryPath = Path.GetFullPath(DirectoryPath);
        var key = Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(DirectoryPath.ToUpperInvariant())));
        mutex = new Mutex(false, @"Local\StreamPipLogs_" + key);
        WithLock(() =>
        {
            Directory.CreateDirectory(DirectoryPath);
            MakeRoom(0);
            if (!FilePattern.IsMatch(SessionFile)) SessionFile = CreateFile();
        });
    }

    protected override void Write(LogEventInfo logEvent)
    {
        var bytes = Utf8.GetBytes(RenderLogEvent(Layout, logEvent) + Environment.NewLine);
        // A single huge event must never exceed either the rotation size or the folder budget.
        if (bytes.Length > RollBytes) bytes = Utf8.GetBytes("Log event omitted: exceeds 1 MB." + Environment.NewLine);
        WithLock(() =>
        {
            var path = Path.Combine(DirectoryPath, SessionFile);
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > RollBytes)
            {
                SessionFile = CreateFile();
                path = Path.Combine(DirectoryPath, SessionFile);
            }
            if (!MakeRoom(bytes.Length)) return; // Locked files must not cause the budget to be exceeded.
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        });
    }

    private string CreateFile()
    {
        var timestamp = DateTime.Now;
        while (true)
        {
            var name = timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_streampipviewer_log.txt";
            var path = Path.Combine(DirectoryPath, name);
            if (!File.Exists(path))
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                return name;
            }
            // Preserve the required filename shape even for starts/rollovers in the same second.
            timestamp = timestamp.AddSeconds(1);
        }
    }

    private bool MakeRoom(int incomingBytes)
    {
        var files = new DirectoryInfo(DirectoryPath).EnumerateFiles("*", new EnumerationOptions
        {
            RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint
        }).OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total + incomingBytes <= MaximumDirectoryBytes) return true;
            try
            {
                var length = file.Length;
                file.Delete();
                total -= length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return total + incomingBytes <= MaximumDirectoryBytes;
    }

    private void WithLock(Action action)
    {
        var acquired = false;
        try
        {
            try { acquired = mutex!.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) action();
        }
        finally { if (acquired) mutex!.ReleaseMutex(); }
    }

    protected override void CloseTarget()
    {
        mutex?.Dispose();
        base.CloseTarget();
    }
}
