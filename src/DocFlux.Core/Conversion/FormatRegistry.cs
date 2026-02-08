using DocFlux.Abstractions.Contracts;
using DocFlux.Core.Adapters;

namespace DocFlux.Core.Conversion;

public sealed class FormatRegistry : IFormatRegistry
{
    private readonly Dictionary<string, IFormatAdapter> _adapters;

    public FormatRegistry(IEnumerable<IFormatAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        _adapters = new Dictionary<string, IFormatAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            ArgumentNullException.ThrowIfNull(adapter);

            var key = NormalizeFormatId(adapter.FormatId);
            if (!_adapters.TryAdd(key, adapter))
            {
                throw new InvalidOperationException($"Adapter already registered for format '{key}'.");
            }
        }
    }

    public static FormatRegistry CreateDefault()
    {
        return new FormatRegistry(
        [
            new TxtFormatAdapter(),
            new MarkdownFormatAdapter(),
            new HtmlFormatAdapter(),
            new XmlFormatAdapter(),
            new AdfFormatAdapter(),
        ]);
    }

    public bool TryGet(string formatId, out IFormatAdapter adapter)
    {
        if (string.IsNullOrWhiteSpace(formatId))
        {
            adapter = null!;
            return false;
        }

        if (_adapters.TryGetValue(NormalizeFormatId(formatId), out var resolved))
        {
            adapter = resolved;
            return true;
        }

        adapter = null!;
        return false;
    }

    private static string NormalizeFormatId(string formatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        return formatId.Trim().ToLowerInvariant();
    }
}
