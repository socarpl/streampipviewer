using System.Text;

namespace CctvPip.App;

internal sealed class PropertiesFile
{
    private readonly List<Entry> entries = new();
    private readonly Dictionary<string, Entry> byKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads a simple properties file while preserving comment and blank lines for later saving.
    /// </summary>
    /// <param name="path">The full path to the properties file to read.</param>
    /// <returns>A parsed properties file, or an empty file representation when the path does not exist.</returns>
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

    /// <summary>
    /// Reads a property value by key.
    /// </summary>
    /// <param name="key">The property key to look up.</param>
    /// <returns>The stored value, or <see langword="null"/> when the key is not present.</returns>
    public string? Get(string key)
    {
        return byKey.TryGetValue(key, out var entry) ? entry.Value : null;
    }

    /// <summary>
    /// Adds a default property only when the key is not already present.
    /// </summary>
    /// <param name="key">The property key that must exist.</param>
    /// <param name="value">The default value to write when the key is missing.</param>
    public void Ensure(string key, string value)
    {
        if (!byKey.ContainsKey(key))
        {
            Set(key, value);
        }
    }

    /// <summary>
    /// Sets a property value, updating an existing entry or appending a new property line.
    /// </summary>
    /// <param name="key">The property key to add or update.</param>
    /// <param name="value">The value to store for the key.</param>
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

    /// <summary>
    /// Saves the properties file with UTF-8 encoding, preserving original comments and blank lines.
    /// </summary>
    /// <param name="path">The full path where the properties file should be written.</param>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (var entry in entries)
        {
            writer.WriteLine(entry.IsProperty ? $"{entry.Key}={entry.Value}" : entry.RawLine);
        }
    }

    /// <summary>
    /// Finds the first key/value separator supported by this parser.
    /// </summary>
    /// <param name="line">The raw properties-file line to inspect.</param>
    /// <returns>The zero-based separator index, or <c>-1</c> when the line is not a property.</returns>
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
        /// <summary>
        /// Creates a parsed line entry representing either a property or a preserved non-property line.
        /// </summary>
        /// <param name="isProperty">Whether this entry represents a key/value property.</param>
        /// <param name="key">The property key, or an empty string for comments and blank lines.</param>
        /// <param name="value">The property value, or an empty string for comments and blank lines.</param>
        /// <param name="rawLine">The original raw line when preserving a comment, blank line, or unsupported line.</param>
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

        /// <summary>
        /// Creates a property entry that will be written as <c>key=value</c>.
        /// </summary>
        /// <param name="key">The property key.</param>
        /// <param name="value">The property value.</param>
        /// <returns>A property entry for the parser's ordered entry list.</returns>
        public static Entry Property(string key, string value)
        {
            return new Entry(true, key, value, "");
        }

        /// <summary>
        /// Creates a preserved non-property entry such as a comment, blank line, or unsupported line.
        /// </summary>
        /// <param name="rawLine">The exact source line to preserve.</param>
        /// <returns>A non-property entry for the parser's ordered entry list.</returns>
        public static Entry Comment(string rawLine)
        {
            return new Entry(false, "", "", rawLine);
        }
    }
}
