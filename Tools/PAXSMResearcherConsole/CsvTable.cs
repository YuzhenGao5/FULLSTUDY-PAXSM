using System.Globalization;
using System.Text;

namespace PAXSMResearcherConsole;

/// <summary>
/// Small, dependency-free CSV reader for the flat Unity exports used by the Console.
/// It intentionally keeps the original column names so every derived record can cite them.
/// </summary>
internal sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;

    private CsvTable(string[] header, IReadOnlyList<string[]> rows)
    {
        Header = header;
        Rows = rows;
        _columns = header
            .Select((name, index) => new { Name = name.Trim(), Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
    }

    public string[] Header { get; }
    public IReadOnlyList<string[]> Rows { get; }

    public IEnumerable<CsvRecord> Records => Rows.Select(row => new CsvRecord(this, row));

    public bool HasColumn(string name) => _columns.ContainsKey(name);

    internal string GetValue(string[] row, string column)
    {
        if (!_columns.TryGetValue(column, out int index) || index >= row.Length)
            return "";
        return row[index].Trim();
    }

    public static CsvTable Read(string path)
    {
        var rows = new List<string[]>();
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
            rows.Add(ParseLine(line));
        if (rows.Count == 0)
            return new CsvTable(Array.Empty<string>(), Array.Empty<string[]>());
        return new CsvTable(rows[0], rows.Skip(1).ToList());
    }

    public static string Escape(string? value)
    {
        string text = value ?? "";
        return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string[] ParseLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}

internal sealed class CsvRecord
{
    private readonly CsvTable _table;
    private readonly string[] _row;

    internal CsvRecord(CsvTable table, string[] row)
    {
        _table = table;
        _row = row;
    }

    public string Get(string column) => _table.GetValue(_row, column);

    public int GetInt(string column, int fallback = 0) =>
        int.TryParse(Get(column), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    public double GetDouble(string column, double fallback = double.NaN) =>
        double.TryParse(Get(column), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    public bool GetBool(string column) =>
        Get(column).Equals("1", StringComparison.OrdinalIgnoreCase) ||
        Get(column).Equals("true", StringComparison.OrdinalIgnoreCase);
}
