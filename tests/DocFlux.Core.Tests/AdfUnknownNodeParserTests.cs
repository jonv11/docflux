using DocFlux.Abstractions.Documents;
using DocFlux.Core.Adapters.Adf;

namespace DocFlux.Core.Tests;

public sealed class AdfUnknownNodeParserTests
{
    private readonly AdfUnknownNodeParser _parser = new();

    [Fact]
    public void TryParse_ValidAdfUnknownBlock_ReturnsDictionary()
    {
        var unknown = new UnknownBlock("adf", "panel", "{\"type\":\"panel\",\"attrs\":{\"panelType\":\"info\"}}");

        var ok = _parser.TryParse(unknown, out var node);

        Assert.True(ok);
        Assert.Equal("panel", node["type"]);
    }

    [Fact]
    public void TryParse_InvalidPayload_ReturnsFalse()
    {
        var unknown = new UnknownInline("adf", "x", "not-json");

        var ok = _parser.TryParse(unknown, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_NonAdfUnknown_ReturnsFalse()
    {
        var unknown = new UnknownBlock("html", "div", "{\"type\":\"div\"}");

        var ok = _parser.TryParse(unknown, out _);

        Assert.False(ok);
    }
}
