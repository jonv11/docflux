using System.Text.Json;
using DocFlux.Abstractions.Documents;

namespace DocFlux.Core.Adapters.Adf;

internal sealed class AdfUnknownNodeParser
{
    public bool TryParse(UnknownBlock unknown, out Dictionary<string, object?> node)
    {
        ArgumentNullException.ThrowIfNull(unknown);
        if (!unknown.OriginalFormatId.Equals("adf", StringComparison.Ordinal))
        {
            node = default!;
            return false;
        }

        return TryParse(unknown.RawPayload, out node);
    }

    public bool TryParse(UnknownInline unknown, out Dictionary<string, object?> node)
    {
        ArgumentNullException.ThrowIfNull(unknown);
        if (!unknown.OriginalFormatId.Equals("adf", StringComparison.Ordinal))
        {
            node = default!;
            return false;
        }

        return TryParse(unknown.RawPayload, out node);
    }

    public bool TryParse(string payload, out Dictionary<string, object?> node)
    {
        node = default!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var converted = ConvertJsonElement(doc.RootElement);
            if (converted is not Dictionary<string, object?> dictionary)
            {
                return false;
            }

            node = dictionary;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(item => item.Name, item => ConvertJsonElement(item.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }
}
