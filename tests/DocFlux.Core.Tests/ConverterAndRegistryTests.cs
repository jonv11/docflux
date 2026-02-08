using DocFlux.Abstractions.Contracts;
using DocFlux.Abstractions.Documents;
using DocFlux.Core.Conversion;

namespace DocFlux.Core.Tests;

public sealed class ConverterAndRegistryTests
{
    [Fact]
    public void Convert_Throws_WhenInputFormatMissing()
    {
        var converter = new DocFluxConverter(FormatRegistry.CreateDefault());

        var exception = Assert.Throws<NotSupportedException>(
            () => converter.Convert("content", "missing", "markdown"));

        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_Throws_WhenOutputFormatMissing()
    {
        var converter = new DocFluxConverter(FormatRegistry.CreateDefault());

        var exception = Assert.Throws<NotSupportedException>(
            () => converter.Convert("content", "markdown", "missing"));

        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertToDocument_Throws_WhenAdapterCannotRead()
    {
        var readOnlyAdapter = new FakeFormatAdapter
        {
            FormatId = "x",
            CanRead = false,
            CanWrite = true,
        };
        var converter = new DocFluxConverter(new InMemoryFormatRegistry([readOnlyAdapter]));

        var exception = Assert.Throws<NotSupportedException>(() => converter.ConvertToDocument("x", "x"));

        Assert.Contains("does not support reading", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertFromDocument_Throws_WhenAdapterCannotWrite()
    {
        var writeOnlyAdapter = new FakeFormatAdapter
        {
            FormatId = "x",
            CanRead = true,
            CanWrite = false,
        };
        var converter = new DocFluxConverter(new InMemoryFormatRegistry([writeOnlyAdapter]));

        var exception = Assert.Throws<NotSupportedException>(
            () => converter.ConvertFromDocument(new DocDocument([]), "x"));

        Assert.Contains("does not support writing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_IsCaseInsensitive_AndTrimsFormatId()
    {
        var registry = FormatRegistry.CreateDefault();

        var found = registry.TryGet("  MARKDOWN  ", out var adapter);

        Assert.True(found);
        Assert.Equal("markdown", adapter.FormatId);
    }

    [Fact]
    public void Registry_Throws_OnDuplicateFormatId()
    {
        var a = new FakeFormatAdapter { FormatId = "md" };
        var b = new FakeFormatAdapter { FormatId = "MD" };

        Assert.Throws<InvalidOperationException>(() => new FormatRegistry([a, b]));
    }

    [Fact]
    public void Registry_TryGet_ReturnsFalse_ForNullOrWhitespace()
    {
        var registry = FormatRegistry.CreateDefault();

        Assert.False(registry.TryGet("", out _));
        Assert.False(registry.TryGet(" ", out _));
    }

    [Fact]
    public void DefaultRegistry_ContainsAllBuiltInFormats()
    {
        var registry = FormatRegistry.CreateDefault();

        Assert.True(registry.TryGet("txt", out _));
        Assert.True(registry.TryGet("markdown", out _));
        Assert.True(registry.TryGet("html", out _));
        Assert.True(registry.TryGet("xml", out _));
        Assert.True(registry.TryGet("adf", out _));
    }

    [Fact]
    public void ConvertToDocument_And_ConvertFromDocument_UseProvidedOptions()
    {
        var adapter = new FakeFormatAdapter
        {
            FormatId = "x",
            CanRead = true,
            CanWrite = true,
            ReadImpl = (_, options) =>
            {
                var marker = options.PreserveUnknownNodes ? "preserve" : "drop";
                return new DocDocument([new ParagraphBlock([new TextRun(marker)])]);
            },
            WriteImpl = (document, options) =>
            {
                var text = Assert.IsType<TextRun>(Assert.IsType<ParagraphBlock>(document.Blocks.Single()).Inlines.Single()).Text;
                return $"{text}:{options.LineEnding.Length}";
            },
        };
        var converter = new DocFluxConverter(new InMemoryFormatRegistry([adapter]));

        var document = converter.ConvertToDocument(
            "ignored",
            "x",
            new FormatReadOptions { PreserveUnknownNodes = false });
        var output = converter.ConvertFromDocument(
            document,
            "x",
            new FormatWriteOptions { LineEnding = "\r\n" });

        Assert.Equal("drop:2", output);
    }
}
