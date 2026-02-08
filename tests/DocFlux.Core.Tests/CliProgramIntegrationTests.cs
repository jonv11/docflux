using System.Text;
using DocFlux.Cli;

namespace DocFlux.Core.Tests;

public sealed class CliProgramIntegrationTests
{
    [Fact]
    public void Main_InlineConversionToOutputFile_WritesConvertedContent()
    {
        using var workspace = new TemporaryWorkspace();
        var outputPath = workspace.PathFor("out.html");

        var exitCode = Program.Main(
        [
            "markdown",
            "html",
            "# CLI Title",
            "--output-file",
            outputPath,
        ]);

        Assert.Equal(0, exitCode);
        var output = File.ReadAllText(outputPath);
        Assert.Contains("<h1>CLI Title</h1>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_InputFileToOutputFile_ConvertsSuccessfully()
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.PathFor("in.md");
        var outputPath = workspace.PathFor("out.adf.json");

        File.WriteAllText(inputPath, "Hello **DocFlux**", Encoding.UTF8);

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
        var output = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Contains("\"type\": \"doc\"", output, StringComparison.Ordinal);
        Assert.Contains("\"strong\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_ReturnsValidationError_WhenInlineAndInputFileProvided()
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.PathFor("in.md");
        File.WriteAllText(inputPath, "ignored", Encoding.UTF8);

        var exitCode = Program.Main(
        [
            "markdown",
            "html",
            "inline",
            "--input-file",
            inputPath,
        ]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Main_ReturnsValidationError_WhenNoInputProvided()
    {
        var exitCode = Program.Main(["markdown", "html"]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Main_ReturnsIoError_WhenInputFileIsMissing()
    {
        using var workspace = new TemporaryWorkspace();
        var missing = workspace.PathFor("missing.md");

        var exitCode = Program.Main(
        [
            "markdown",
            "html",
            "--input-file",
            missing,
        ]);

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void Main_ReturnsNotSupported_WhenFormatIdUnknown()
    {
        var exitCode = Program.Main(["unknown", "html", "content"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Main_ListFormatsCommand_PrintsKnownFormats()
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exitCode = Program.Main(["list-formats"]);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("adf", output, StringComparison.Ordinal);
        Assert.Contains("html", output, StringComparison.Ordinal);
        Assert.Contains("markdown", output, StringComparison.Ordinal);
        Assert.Contains("txt", output, StringComparison.Ordinal);
        Assert.Contains("xml", output, StringComparison.Ordinal);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _directoryPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "docflux-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace()
        {
            Directory.CreateDirectory(_directoryPath);
        }

        public string PathFor(string fileName)
        {
            return System.IO.Path.Combine(_directoryPath, fileName);
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
