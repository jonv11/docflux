using DocFlux.Cli;

namespace DocFlux.Core.Tests;

public sealed class CliRunnerTests
{
    [Fact]
    public void Run_ReturnsValidationError_WhenPrettyAndCompactBothSet()
    {
        var runner = new CliRunner(new FakeFileSystem());
        var request = new CliRunRequest("markdown", "adf", ["# t"], null, null, "true", "true", "lf", Compact: true, Pretty: true);

        var exitCode = runner.Run(request, new StringReader(string.Empty), new StringWriter(), new StringWriter());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_UsesInputFile_OverStdin()
    {
        var fs = new FakeFileSystem
        {
            Files = { ["in.md"] = "# from-file" },
        };
        var runner = new CliRunner(fs);
        var request = new CliRunRequest("markdown", "html", [], "in.md", null, "true", "true", "lf", Compact: false, Pretty: false);
        var stdout = new StringWriter();

        var exitCode = runner.Run(request, new StringReader("# stdin"), stdout, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("<h1>from-file</h1>", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ReturnsValidationError_WhenNoInputAndEmptyStdin()
    {
        var runner = new CliRunner(new FakeFileSystem());
        var request = new CliRunRequest("markdown", "html", [], null, null, "true", "true", "lf", Compact: false, Pretty: false);

        var exitCode = runner.Run(request, new StringReader(" "), new StringWriter(), new StringWriter());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Run_ReturnsIoError_WhenReadFails()
    {
        var fs = new FakeFileSystem { ThrowOnRead = true };
        var runner = new CliRunner(fs);
        var request = new CliRunRequest("markdown", "html", [], "missing.md", null, "true", "true", "lf", Compact: false, Pretty: false);

        var exitCode = runner.Run(request, new StringReader(string.Empty), new StringWriter(), new StringWriter());

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void Run_ReturnsIoError_WhenWriteFails()
    {
        var fs = new FakeFileSystem
        {
            Files = { ["in.md"] = "# hello" },
            ThrowOnWrite = true,
        };
        var runner = new CliRunner(fs);
        var request = new CliRunRequest("markdown", "html", [], "in.md", "out.html", "true", "true", "lf", Compact: false, Pretty: false);

        var exitCode = runner.Run(request, new StringReader(string.Empty), new StringWriter(), new StringWriter());

        Assert.Equal(3, exitCode);
    }

    private sealed class FakeFileSystem : ICliFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public bool ThrowOnRead { get; init; }

        public bool ThrowOnWrite { get; init; }

        public string ReadAllText(string path)
        {
            if (ThrowOnRead)
            {
                throw new IOException("read error");
            }

            if (!Files.TryGetValue(path, out var content))
            {
                throw new IOException("not found");
            }

            return content;
        }

        public void WriteAllText(string path, string content)
        {
            if (ThrowOnWrite)
            {
                throw new IOException("write error");
            }

            Files[path] = content;
        }
    }
}
