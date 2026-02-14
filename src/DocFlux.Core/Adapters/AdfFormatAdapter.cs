using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Adf;

namespace DocFlux.Core.Adapters;

public sealed class AdfFormatAdapter : IFormatAdapter
{
    private readonly AdfReader _reader = new();
    private readonly AdfUnknownNodeParser _unknownNodeParser = new();
    private readonly AdfCanonicalizer _canonicalizer = new();
    private readonly AdfWriter _writer;

    public AdfFormatAdapter()
    {
        _writer = new AdfWriter(_unknownNodeParser, _canonicalizer);
    }

    public string FormatId => "adf";

    public IReadOnlyCollection<string> MimeTypes { get; } = ["application/json"];

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
