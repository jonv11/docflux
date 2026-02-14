using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Markdown;

namespace DocFlux.Core.Adapters;

public sealed class MarkdownFormatAdapter : IFormatAdapter
{
    private readonly MarkdownReader _reader = new();
    private readonly MarkdownWriter _writer = new();

    public string FormatId => "markdown";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["text/markdown", "text/x-markdown"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        return _reader.Read(input, options);
    }

    public string Write(DocDocument document, FormatWriteOptions options)
    {
        return _writer.Write(document, options);
    }
}
