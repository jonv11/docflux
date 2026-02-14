using System.Text;
using System.Text.Json;

namespace DocFlux.Core.Tests.Helpers;

internal static class JsonAssertHelpers
{
    public static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(NormalizeLineEndings(json));
        var normalized = NormalizeJsonElement(document.RootElement);
        return JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
    }

    public static bool IsNodeType(JsonElement element, string type)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("type", out var typeProperty)
            && typeProperty.ValueKind == JsonValueKind.String
            && string.Equals(typeProperty.GetString(), type, StringComparison.Ordinal);
    }

    public static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    public static string CanonicalJsonNoIndent(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => NormalizeObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
            JsonValueKind.String => NormalizeLineEndings(element.GetString() ?? string.Empty),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private static Dictionary<string, object?> NormalizeObject(JsonElement objectElement)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (IsNonSemanticProperty(property.Name))
            {
                continue;
            }

            map[property.Name] = NormalizeJsonElement(property.Value);
        }

        return map;
    }

    private static bool IsNonSemanticProperty(string propertyName)
    {
        return propertyName.Equals("id", StringComparison.Ordinal)
            || propertyName.Equals("localId", StringComparison.Ordinal)
            || propertyName.Equals("timestamp", StringComparison.Ordinal);
    }
}
