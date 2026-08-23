using System.Text;

namespace CctvPip.App;

internal sealed class PropertiesFile
{
    private readonly List<Entry> entries = new();
    private readonly Dictionary<string, Entry> byKey = new(StringComparer.OrdinalIgnoreCase);

    public static PropertiesFile Load(string path)
    {
        var file = new PropertiesFile();

        if (!File.Exists(path))
        {
            return file;
        }

        foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
            {
                file.entries.Add(Entry.Comment(rawLine));
                continue;
            }

            var separator = FindSeparator(rawLine);
            if (separator < 0)
            {
                file.entries.Add(Entry.Comment(rawLine));
                continue;
            }

            var key = rawLine[..separator].Trim();
            var value = rawLine[(separator + 1)..].Trim();
            var entry = Entry.Property(key, value);
            file.entries.Add(entry);
            file.byKey[key] = entry;
        }

        return file;
    }

    public string? Get(string key)
    {
        return byKey.TryGetValue(key, out var entry) ? entry.Value : null;
    }

    public void Ensure(string key, string value)
    {
        if (!byKey.ContainsKey(key))
        {
            Set(key, value);
        }
    }

    public void Set(string key, string value)
    {
        if (byKey.TryGetValue(key, out var entry))
        {
            entry.Value = value;
            return;
        }

        var newEntry = Entry.Property(key, value);
        entries.Add(newEntry);
        byKey[key] = newEntry;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (var entry in entries)
        {
            writer.WriteLine(entry.IsProperty ? $"{entry.Key}={entry.Value}" : entry.RawLine);
        }
    }

    private static int FindSeparator(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is '=' or ':')
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class Entry
    {
        private Entry(bool isProperty, string key, string value, string rawLine)
        {
            IsProperty = isProperty;
            Key = key;
            Value = value;
            RawLine = rawLine;
        }

        public bool IsProperty { get; }
        public string Key { get; }
        public string Value { get; set; }
        public string RawLine { get; }

        public static Entry Property(string key, string value)
        {
            return new Entry(true, key, value, "");
        }

        public static Entry Comment(string rawLine)
        {
            return new Entry(false, "", "", rawLine);
        }
    }
}
