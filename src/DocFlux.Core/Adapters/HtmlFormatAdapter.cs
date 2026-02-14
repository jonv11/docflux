using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Html;

namespace DocFlux.Core.Adapters;

public sealed class HtmlFormatAdapter : IFormatAdapter
{
    private readonly HtmlInlineMapper _inlineMapper = new();
    private readonly HtmlBlockMapper _blockMapper;
    private readonly HtmlReader _reader;
    private readonly HtmlWriter _writer = new();

    public HtmlFormatAdapter()
    {
        _blockMapper = new HtmlBlockMapper(_inlineMapper);
        _reader = new HtmlReader(_blockMapper);
    }

    public string FormatId => "html";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["text/html", "application/xhtml+xml"];

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
