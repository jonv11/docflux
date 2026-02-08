using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;

namespace DocFlux.Core.Conversion;

public sealed class DocFluxConverter
{
    private readonly IFormatRegistry _registry;

    public DocFluxConverter(IFormatRegistry? registry = null)
    {
        _registry = registry ?? FormatRegistry.CreateDefault();
    }

    public string Convert(
        string input,
        string inFormatId,
        string outFormatId,
        ConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var conversionOptions = options ?? ConversionOptions.Default;
        var document = ConvertToDocument(input, inFormatId, conversionOptions.ReadOptions);
        return ConvertFromDocument(document, outFormatId, conversionOptions.WriteOptions);
    }

    public DocDocument ConvertToDocument(
        string input,
        string inFormatId,
        FormatReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var adapter = ResolveAdapter(inFormatId);
        if (!adapter.CanRead)
        {
            throw new NotSupportedException($"Format '{adapter.FormatId}' does not support reading.");
        }

        return adapter.Read(input.AsSpan(), options ?? FormatReadOptions.Default);
    }

    public string ConvertFromDocument(
        DocDocument document,
        string outFormatId,
        FormatWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var adapter = ResolveAdapter(outFormatId);
        if (!adapter.CanWrite)
        {
            throw new NotSupportedException($"Format '{adapter.FormatId}' does not support writing.");
        }

        return adapter.Write(document, options ?? FormatWriteOptions.Default);
    }

    private IFormatAdapter ResolveAdapter(string formatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);

        if (_registry.TryGet(formatId, out var adapter))
        {
            return adapter;
        }

        throw new NotSupportedException($"No adapter is registered for format '{formatId}'.");
    }
}
