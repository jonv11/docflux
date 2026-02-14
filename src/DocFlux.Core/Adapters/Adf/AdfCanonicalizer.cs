using System.Text;
using System.Text.Json;

namespace DocFlux.Core.Adapters.Adf;

internal sealed class AdfCanonicalizer
{
    public string NormalizeSerializedAdf(string json, bool indented)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var parsed = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            WriteCanonicalElement(parsed.RootElement, writer, isRoot: true);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalElement(JsonElement element, Utf8JsonWriter writer, bool isRoot = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteCanonicalObject(element, writer, isRoot);
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(item, writer);
                }

                writer.WriteEndArray();
                return;
            default:
                element.WriteTo(writer);
                return;
        }
    }

    private static void WriteCanonicalObject(JsonElement element, Utf8JsonWriter writer, bool isRoot)
    {
        writer.WriteStartObject();

        var properties = element.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        if (properties.TryGetValue("type", out var typeProperty) && typeProperty.ValueKind == JsonValueKind.String)
        {
            var normalized = NormalizeTypeName(typeProperty.GetString() ?? string.Empty);
            if (isRoot && normalized.Equals("document", StringComparison.Ordinal))
            {
                normalized = "doc";
            }

            writer.WriteString("type", normalized);
        }

        if (isRoot)
        {
            writer.WriteNumber("version", 1);
        }
        else if (properties.TryGetValue("version", out var versionProperty))
        {
            writer.WritePropertyName("version");
            WriteCanonicalElement(versionProperty, writer);
        }

        var remaining = properties.Keys
            .Where(name => !name.Equals("type", StringComparison.Ordinal)
                && !name.Equals("version", StringComparison.Ordinal))
            .OrderBy(name => GetPropertyOrder(name))
            .ThenBy(name => name, StringComparer.Ordinal);
        foreach (var name in remaining)
        {
            writer.WritePropertyName(name);
            WriteCanonicalElement(properties[name], writer);
        }

        writer.WriteEndObject();
    }

    private static int GetPropertyOrder(string propertyName)
    {
        return propertyName switch
        {
            "attrs" => 0,
            "text" => 1,
            "marks" => 2,
            "content" => 3,
            _ => 10,
        };
    }

    private static string NormalizeTypeName(string type)
    {
        return type switch
        {
            "document" => "doc",
            "bold" => "strong",
            "italic" => "em",
            _ => type,
        };
    }
}
