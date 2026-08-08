using System.Text.Json;

namespace WDLConsoleCompanion.Services;

internal sealed class LocalizationCatalog
{
    private sealed class CatalogFile
    {
        public Dictionary<string, string> Names { get; set; } = [];
        public Dictionary<string, string> Surnames { get; set; } = [];
    }

    private readonly Dictionary<int, string> _names;
    private readonly Dictionary<int, string> _surnames;
    private readonly Dictionary<string, List<int>> _nameIds;
    private readonly Dictionary<string, List<int>> _surnameIds;

    private LocalizationCatalog(Dictionary<int, string> names, Dictionary<int, string> surnames)
    {
        _names = names; _surnames = surnames;
        _nameIds = Reverse(names); _surnameIds = Reverse(surnames);
    }

    internal static LocalizationCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), JsonOptions())
                   ?? throw new InvalidOperationException("Localization catalog is empty.");
        return new LocalizationCatalog(Parse(file.Names), Parse(file.Surnames));
    }

    internal string Name(int id) => _names.TryGetValue(id, out string? value) ? value : $"<loc:{id}>";
    internal string Surname(int id) => _surnames.TryGetValue(id, out string? value) ? value : $"<loc:{id}>";
    internal int ResolveName(string text, int currentId) => Resolve(text, currentId, _names, _nameIds, "first name");
    internal int ResolveSurname(string text, int currentId) => Resolve(text, currentId, _surnames, _surnameIds, "surname");

    private static int Resolve(string text, int currentId, Dictionary<int, string> values,
        Dictionary<string, List<int>> reverse, string label)
    {
        text = text.Trim();
        if (values.TryGetValue(currentId, out string? current) && string.Equals(current, text, StringComparison.OrdinalIgnoreCase)) return currentId;
        if (!reverse.TryGetValue(text, out var ids)) throw new InvalidOperationException($"Unknown {label} '{text}'. Enter a value present in localization.json.");
        if (ids.Count > 1) throw new InvalidOperationException($"{label} '{text}' maps to multiple localization IDs ({string.Join(", ", ids)}). Edit localization.json to remove the ambiguity or keep the current value.");
        return ids[0];
    }

    private static Dictionary<int, string> Parse(Dictionary<string, string> source) =>
        source.ToDictionary(pair => int.Parse(pair.Key, System.Globalization.CultureInfo.InvariantCulture), pair => pair.Value);

    private static Dictionary<string, List<int>> Reverse(Dictionary<int, string> source)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (!result.TryGetValue(pair.Value, out var ids)) result[pair.Value] = ids = [];
            ids.Add(pair.Key);
        }
        return result;
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
}
