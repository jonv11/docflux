using System.Text;
using System.Text.Json;
using DocFlux.Cli;
using DocFlux.Core.Conversion;

namespace DocFlux.Core.Tests;

public sealed class MarkdownBomHandlingTests
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    [Theory]
    [MemberData(nameof(Utf8BomMarkdownCases))]
    public void Convert_StringInputWithUtf8Bom_ProducesValidAdf_WithoutFeffLeak(
        string markdown,
        string expectedFirstNodeType)
    {
        var input = "\uFEFF" + markdown;
        var converter = new DocFluxConverter();

        var adf = converter.Convert(input, "markdown", "adf");

        using var json = ParseAdf(adf);
        var root = json.RootElement;
        AssertValidAdfRoot(root);
        AssertMeaningfulContent(root);
        AssertFirstNodeType(root, expectedFirstNodeType);
        AssertNoFeffLeak(root);
    }

    [Theory]
    [MemberData(nameof(Utf8BomMarkdownCases))]
    public void Main_InputFileWithUtf8Bom_ProducesValidAdf_WithoutFeffLeak(
        string markdown,
        string expectedFirstNodeType)
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = WriteBomPrefixedFile(workspace.DirectoryPath, "input-utf8.md", Utf8Bom, markdown, Encoding.UTF8);
        var outputPath = workspace.PathFor("output-utf8.adf.json");

        var exitCode = Program.Main(
        [
            "markdown",
            "adf",
            "--input-file",
            inputPath,
            "--output-file",
            outputPath,
        ]);

        Assert.Equal(0, exitCode);
        var adf = File.ReadAllText(outputPath, Encoding.UTF8);
        using var json = ParseAdf(adf);
        var root = json.RootElement;

        AssertValidAdfRoot(root);
        AssertMeaningfulContent(root);
        AssertFirstNodeType(root, expectedFirstNodeType);
        AssertNoFeffLeak(root);
    }

    [Theory]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    public void Main_InputFileWithUtf16Bom_ProducesValidAdf_WithoutFeffLeak(string encodingCase)
    {
        using var workspace = new TemporaryWorkspace();
        var markdown = "# Title\n\nHello";

        var (bom, encoding, fileName) = encodingCase switch
        {
            "utf16-le" => (Utf16LeBom, Encoding.Unicode, "input-utf16le.md"),
            "utf16-be" => (Utf16BeBom, Encoding.BigEndianUnicode, "input-utf16be.md"),
            _ => throw new InvalidOperationException($"Unknown encoding case '{encodingCase}'."),
        };

        var inputPath = WriteBomPrefixedFile(workspace.DirectoryPath, fileName, bom, markdown, encoding);
        var outputPath = workspace.PathFor("output-utf16.adf.json");

        var exitCode = Program.Main(
        [
            "markdown",
            "adf",
            "--input-file",
            inputPath,
            "--output-file",
            outputPath,
        ]);

        Assert.Equal(0, exitCode);
        var adf = File.ReadAllText(outputPath, Encoding.UTF8);
        using var json = ParseAdf(adf);
        var root = json.RootElement;

        AssertValidAdfRoot(root);
        AssertMeaningfulContent(root);
        AssertFirstNodeType(root, "heading");
        AssertNoFeffLeak(root);
    }

    public static IEnumerable<object[]> Utf8BomMarkdownCases()
    {
        yield return new object[] { "# Title\n\nHello", "heading" };
        yield return new object[] { "Hello\n\nWorld", "paragraph" };
        yield return new object[] { "# T\n", "heading" };
    }

    private static string WriteBomPrefixedFile(
        string directoryPath,
        string fileName,
        byte[] bom,
        string content,
        Encoding encoding)
    {
        var filePath = Path.Combine(directoryPath, fileName);
        var contentBytes = encoding.GetBytes(content);
        var combinedBytes = new byte[bom.Length + contentBytes.Length];

        Buffer.BlockCopy(bom, 0, combinedBytes, 0, bom.Length);
        Buffer.BlockCopy(contentBytes, 0, combinedBytes, bom.Length, contentBytes.Length);
        File.WriteAllBytes(filePath, combinedBytes);

        return filePath;
    }

    private static JsonDocument ParseAdf(string adfJson)
    {
        return JsonDocument.Parse(adfJson);
    }

    private static void AssertValidAdfRoot(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("doc", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());

        var content = root.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.NotEmpty(content.EnumerateArray());
    }

    private static void AssertMeaningfulContent(JsonElement root)
    {
        var texts = GetAllTextNodeValues(root);
        Assert.Contains(texts, value => !string.IsNullOrWhiteSpace(value));
    }

    private static void AssertFirstNodeType(JsonElement root, string expectedType)
    {
        var firstNode = root.GetProperty("content").EnumerateArray().First();
        Assert.Equal(expectedType, firstNode.GetProperty("type").GetString());
    }

    private static void AssertNoFeffLeak(JsonElement root)
    {
        foreach (var text in GetAllTextNodeValues(root))
        {
            Assert.DoesNotContain('\uFEFF', text);
        }
    }

    private static IReadOnlyList<string> GetAllTextNodeValues(JsonElement root)
    {
        var texts = new List<string>();
        CollectTextNodes(root, texts);
        return texts;
    }

    private static void CollectTextNodes(JsonElement node, List<string> texts)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.TryGetProperty("type", out var typeProperty)
                    && typeProperty.ValueKind == JsonValueKind.String
                    && string.Equals(typeProperty.GetString(), "text", StringComparison.Ordinal)
                    && node.TryGetProperty("text", out var textProperty)
                    && textProperty.ValueKind == JsonValueKind.String)
                {
                    texts.Add(textProperty.GetString() ?? string.Empty);
                }

                foreach (var property in node.EnumerateObject())
                {
                    CollectTextNodes(property.Value, texts);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    CollectTextNodes(item, texts);
                }

                break;
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "docflux-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace()
        {
            Directory.CreateDirectory(_directoryPath);
        }

        public string DirectoryPath => _directoryPath;

        public string PathFor(string fileName)
        {
            return Path.Combine(_directoryPath, fileName);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
        }
    }
}
