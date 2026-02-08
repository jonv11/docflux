using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;

namespace DocFlux.Core.Tests;

internal sealed class InMemoryFormatRegistry : IFormatRegistry
{
    private readonly Dictionary<string, IFormatAdapter> _adapters;

    public InMemoryFormatRegistry(IEnumerable<IFormatAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(
            adapter => adapter.FormatId,
            adapter => adapter,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string formatId, out IFormatAdapter adapter)
    {
        if (string.IsNullOrWhiteSpace(formatId))
        {
            adapter = null!;
            return false;
        }

        return _adapters.TryGetValue(formatId, out adapter!);
    }
}

internal sealed class FakeFormatAdapter : IFormatAdapter
{
    public required string FormatId { get; init; }

    public IReadOnlyCollection<string> MimeTypes { get; init; } = [];

    public bool CanRead { get; init; } = true;

    public bool CanWrite { get; init; } = true;

    public Func<string, FormatReadOptions, DocDocument>? ReadImpl { get; init; }

    public Func<DocDocument, FormatWriteOptions, string>? WriteImpl { get; init; }

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        if (ReadImpl is null)
        {
            return new DocDocument([new ParagraphBlock([new TextRun(input.ToString())])]);
        }

        return ReadImpl(input.ToString(), options);
    }

    public string Write(DocDocument document, FormatWriteOptions options)
    {
        if (WriteImpl is null)
        {
            return "ok";
        }

        return WriteImpl(document, options);
    }
}
