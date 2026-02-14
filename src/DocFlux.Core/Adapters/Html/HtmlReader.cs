using AngleSharp.Html.Parser;
using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Internal;

namespace DocFlux.Core.Adapters.Html;

internal sealed class HtmlReader
{
    private readonly HtmlBlockMapper _blockMapper;

    public HtmlReader(HtmlBlockMapper blockMapper)
    {
        _blockMapper = blockMapper ?? throw new ArgumentNullException(nameof(blockMapper));
    }

    public DocDocument Read(ReadOnlySpan<char> input, FormatReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var parser = new HtmlParser();
        var text = AdapterUtilities.NormalizeInput(input, options);
        var parsed = parser.ParseDocument(text);
        var root = parsed.Body ?? parsed.DocumentElement;
        var blocks = _blockMapper.MapBlockNodes(root.ChildNodes, options);
        return new DocDocument(blocks);
    }
}
