using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Html;

namespace DocFlux.Core.Tests;

public sealed class HtmlReaderTests
{
    private readonly HtmlReader _reader;

    public HtmlReaderTests()
    {
        var inlineMapper = new HtmlInlineMapper();
        var blockMapper = new HtmlBlockMapper(inlineMapper);
        _reader = new HtmlReader(blockMapper);
    }

    [Fact]
    public void Read_MapsHeadingAndStrongInline()
    {
        var document = _reader.Read("<h2>T</h2><p>Hello <strong>W</strong></p>".AsSpan(), FormatReadOptions.Default);

        Assert.Equal(2, document.Blocks.Count);
        var paragraph = Assert.IsType<ParagraphBlock>(document.Blocks[1]);
        Assert.Contains(paragraph.Inlines, inline => inline is StrongInline);
    }

    [Fact]
    public void Read_UnknownNode_RespectsPreserveUnknownNodes()
    {
        var preserved = _reader.Read("<widget>v</widget>".AsSpan(), new FormatReadOptions { PreserveUnknownNodes = true });
        var dropped = _reader.Read("<widget>v</widget>".AsSpan(), new FormatReadOptions { PreserveUnknownNodes = false });

        Assert.IsType<UnknownBlock>(Assert.Single(preserved.Blocks));
        Assert.IsType<ParagraphBlock>(Assert.Single(dropped.Blocks));
    }
}
