using DocFlux.Core.Adapters.Adf;

namespace DocFlux.Core.Tests;

public sealed class AdfCanonicalizerTests
{
    [Fact]
    public void NormalizeSerializedAdf_OrdersPropertiesAndNormalizesTypes()
    {
        var canonicalizer = new AdfCanonicalizer();
        const string input = """
                             {"version":99,"type":"document","content":[{"content":[],"type":"paragraph"}]}
                             """;

        var output = canonicalizer.NormalizeSerializedAdf(input, indented: false);

        Assert.StartsWith("{\"type\":\"doc\",\"version\":1,", output, StringComparison.Ordinal);
        Assert.Contains("\"content\":[", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeSerializedAdf_RespectsIndentationFlag()
    {
        var canonicalizer = new AdfCanonicalizer();
        const string input = """{"type":"doc","version":1,"content":[]}""";

        var pretty = canonicalizer.NormalizeSerializedAdf(input, indented: true);
        var compact = canonicalizer.NormalizeSerializedAdf(input, indented: false);

        Assert.Contains('\n', pretty);
        Assert.DoesNotContain('\n', compact);
    }
}
