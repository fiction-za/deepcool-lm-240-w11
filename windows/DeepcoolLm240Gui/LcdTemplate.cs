using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepcoolLm240Gui;

public sealed class LcdTemplate
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("background")] public string Background { get; set; } = "#0E0E12";
    [JsonPropertyName("elements")] public List<LcdElement> Elements { get; set; } = [];

    public string SourcePath { get; set; } = "";
    [JsonIgnore] public string Detail => string.IsNullOrWhiteSpace(Description) ? Id : Description;
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    public static LcdTemplate Load(string path)
    {
        var json = File.ReadAllText(path);
        var template = JsonSerializer.Deserialize<LcdTemplate>(json, LcdJson.Options)
                       ?? throw new InvalidDataException($"Could not read template {path}");
        template.SourcePath = path;
        if (string.IsNullOrWhiteSpace(template.Id))
            template.Id = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(template.Name))
            template.Name = template.Id;
        return template;
    }

    public static IReadOnlyList<LcdTemplate> LoadAll(string directory)
    {
        if (!Directory.Exists(directory))
            return [];
        return Directory.GetFiles(directory, "*.json")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(Load)
            .ToList();
    }
}

public sealed class LcdElement
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }
    [JsonPropertyName("radius")] public double Radius { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("fill")] public string? Fill { get; set; }
    [JsonPropertyName("stroke")] public string? Stroke { get; set; }
    [JsonPropertyName("strokeWidth")] public double StrokeWidth { get; set; } = 1;
    [JsonPropertyName("track")] public string? Track { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("bind")] public string? Bind { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("size")] public double Size { get; set; } = 16;
    [JsonPropertyName("weight")] public string? Weight { get; set; }
    [JsonPropertyName("align")] public string? Align { get; set; }
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1;
    [JsonPropertyName("max")] public double Max { get; set; } = 100;
    [JsonPropertyName("colorBy")] public string? ColorBy { get; set; }
}

static class LcdJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
