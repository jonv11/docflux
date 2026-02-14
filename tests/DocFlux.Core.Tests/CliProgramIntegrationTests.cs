using System.Text;
using DocFlux.Cli;

namespace DocFlux.Core.Tests;

public sealed class CliProgramIntegrationTests
{
    private static readonly object ConsoleLock = new();

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
        Assert.Contains("\"type\":\"doc\"", output, StringComparison.Ordinal);
        Assert.Contains("\"strong\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', output);
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
    public void Main_ReadsFromStdIn_WhenNoInlineOrInputFileProvided()
    {
        using var workspace = new TemporaryWorkspace();
        var outputPath = workspace.PathFor("out.html");

        int exitCode;
        lock (ConsoleLock)
        {
            var originalIn = Console.In;
            Console.SetIn(new StringReader("# Stdin Title"));
            try
            {
                exitCode = Program.Main(
                [
                    "markdown",
                    "html",
                    "--output-file",
                    outputPath,
                ]);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

        Assert.Equal(0, exitCode);
        var output = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Contains("<h1>Stdin Title</h1>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_ReturnsValidationError_WhenStdInIsEmpty()
    {
        int exitCode;
        lock (ConsoleLock)
        {
            var originalIn = Console.In;
            Console.SetIn(new StringReader("  \r\n  "));
            try
            {
                exitCode = Program.Main(["markdown", "html"]);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

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
    public void Main_ReturnsValidationError_WhenPrettyAndCompactAreBothSet()
    {
        var exitCode = Program.Main(
        [
            "markdown",
            "adf",
            "# title",
            "--pretty",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Main_AdfOutput_IsPretty_WhenPrettyOptionIsSet()
    {
        using var workspace = new TemporaryWorkspace();
        var outputPath = workspace.PathFor("pretty.adf.json");

        var exitCode = Program.Main(
        [
            "markdown",
            "adf",
            "# heading",
            "--pretty",
            "--output-file",
            outputPath,
        ]);

        Assert.Equal(0, exitCode);
        var output = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Contains("\"type\": \"doc\"", output, StringComparison.Ordinal);
        Assert.Contains('\n', output);
    }

    [Fact]
    public void Main_AdfOutput_IsCompact_WhenCompactOptionIsSet()
    {
        using var workspace = new TemporaryWorkspace();
        var outputPath = workspace.PathFor("compact.adf.json");

        var exitCode = Program.Main(
        [
            "markdown",
            "adf",
            "# heading",
            "--compact",
            "--output-file",
            outputPath,
        ]);

        Assert.Equal(0, exitCode);
        var output = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Contains("\"type\":\"doc\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', output);
    }

    [Fact]
    public void Main_LineEndingOption_AppliesToTextOutputs()
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.PathFor("in.md");
        var outputPath = workspace.PathFor("out.txt");
        File.WriteAllText(inputPath, "line1\n\nline2", Encoding.UTF8);

        var exitCode = Program.Main(
        [
            "markdown",
            "txt",
            "--input-file",
            inputPath,
            "--line-ending",
            "crlf",
            "--output-file",
            outputPath,
        ]);

        Assert.Equal(0, exitCode);
        var output = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Contains("line1\r\n\r\nline2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_UnknownNodeFlags_AffectOutputDeterministically()
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.PathFor("unknown.adf.json");
        var preservedOutputPath = workspace.PathFor("preserved.md");
        var droppedOutputPath = workspace.PathFor("dropped.md");
        File.WriteAllText(
            inputPath,
            """
            {
              "type": "doc",
              "version": 1,
              "content": [
                {
                  "type": "panel",
                  "content": [
                    {
                      "type": "paragraph",
                      "content": [
                        { "type": "text", "text": "inside panel" }
                      ]
                    }
                  ]
                }
              ]
            }
            """,
            Encoding.UTF8);

        var preservedExitCode = Program.Main(
        [
            "adf",
            "markdown",
            "--input-file",
            inputPath,
            "--output-file",
            preservedOutputPath,
            "--preserve-unknown",
            "true",
            "--emit-unknown-as-plain-text",
            "false",
        ]);
        var droppedExitCode = Program.Main(
        [
            "adf",
            "markdown",
            "--input-file",
            inputPath,
            "--output-file",
            droppedOutputPath,
            "--preserve-unknown",
            "false",
            "--emit-unknown-as-plain-text",
            "false",
        ]);

        Assert.Equal(0, preservedExitCode);
        Assert.Equal(0, droppedExitCode);

        var preserved = File.ReadAllText(preservedOutputPath, Encoding.UTF8);
        var dropped = File.ReadAllText(droppedOutputPath, Encoding.UTF8);
        Assert.Contains("```docflux-unknown", preserved, StringComparison.Ordinal);
        Assert.Equal(string.Empty, dropped);
    }

    [Fact]
    public void Main_ListFormatsCommand_PrintsKnownFormats()
    {
        string output;
        lock (ConsoleLock)
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

            output = writer.ToString();
        }

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
