using System.CommandLine;
using System.CommandLine.Parsing;
using DocFlux.Core.Conversion;

namespace DocFlux.Cli;

public static class Program
{
    private static readonly CliRunner Runner = new(new CliFileSystem());

    public static int Main(string[] args)
    {
        var root = BuildCommand();
        var parseResult = root.Parse(args);
        return parseResult.Invoke();
    }

    private static RootCommand BuildCommand()
    {
        var sourceFormatArgument = new Argument<string>("source-format")
        {
            Description = "Input format id (e.g. markdown, html, adf).",
        };

        var targetFormatArgument = new Argument<string>("target-format")
        {
            Description = "Output format id (e.g. markdown, html, adf).",
        };

        var contentArgument = new Argument<string[]>("content")
        {
            Description = "Inline content to convert.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var inputFileOption = new Option<string?>("--input-file", "-i")
        {
            Description = "Read input content from file.",
        };

        var outputFileOption = new Option<string?>("--output-file", "-o")
        {
            Description = "Write converted output to file.",
        };

        var preserveUnknownOption = new Option<string>("--preserve-unknown")
        {
            Description = "Preserve unknown nodes while reading/writing (true|false).",
            DefaultValueFactory = _ => "true",
        };

        var emitUnknownAsPlainTextOption = new Option<string>("--emit-unknown-as-plain-text")
        {
            Description = "Emit unknown nodes as plain text markers when writing (true|false).",
            DefaultValueFactory = _ => "true",
        };

        var lineEndingOption = new Option<string>("--line-ending")
        {
            Description = "Output line ending style (lf|crlf).",
            DefaultValueFactory = _ => "lf",
        };

        var compactOption = new Option<bool>("--compact")
        {
            Description = "Emit compact single-line output when supported.",
        };

        var prettyOption = new Option<bool>("--pretty")
        {
            Description = "Emit pretty indented output when supported.",
        };

        var root = new RootCommand("docflux document format converter.");
        root.Add(sourceFormatArgument);
        root.Add(targetFormatArgument);
        root.Add(contentArgument);
        root.Add(inputFileOption);
        root.Add(outputFileOption);
        root.Add(preserveUnknownOption);
        root.Add(emitUnknownAsPlainTextOption);
        root.Add(lineEndingOption);
        root.Add(compactOption);
        root.Add(prettyOption);
        root.Add(CreateListFormatsCommand());
        root.SetAction((ParseResult result) => Execute(result, sourceFormatArgument, targetFormatArgument, contentArgument, inputFileOption, outputFileOption, preserveUnknownOption, emitUnknownAsPlainTextOption, lineEndingOption, compactOption, prettyOption));

        return root;
    }

    private static Command CreateListFormatsCommand()
    {
        var command = new Command("list-formats", "List available format ids.");
        command.SetAction(_ =>
        {
            var registry = FormatRegistry.CreateDefault();
            var knownFormats = new[] { "txt", "markdown", "html", "xml", "adf" }
                .Where(format => registry.TryGet(format, out var _adapter))
                .OrderBy(format => format, StringComparer.Ordinal)
                .ToArray();

            foreach (var format in knownFormats)
            {
                Console.WriteLine(format);
            }

            return 0;
        });
        return command;
    }

    private static int Execute(
        ParseResult result,
        Argument<string> sourceFormatArgument,
        Argument<string> targetFormatArgument,
        Argument<string[]> contentArgument,
        Option<string?> inputFileOption,
        Option<string?> outputFileOption,
        Option<string> preserveUnknownOption,
        Option<string> emitUnknownAsPlainTextOption,
        Option<string> lineEndingOption,
        Option<bool> compactOption,
        Option<bool> prettyOption)
    {
        var request = new CliRunRequest(
            result.GetRequiredValue(sourceFormatArgument),
            result.GetRequiredValue(targetFormatArgument),
            result.GetValue(contentArgument) ?? [],
            result.GetValue(inputFileOption),
            result.GetValue(outputFileOption),
            result.GetValue(preserveUnknownOption) ?? "true",
            result.GetValue(emitUnknownAsPlainTextOption) ?? "true",
            result.GetValue(lineEndingOption) ?? "lf",
            result.GetValue(compactOption),
            result.GetValue(prettyOption));
        return Runner.Run(request, Console.In, Console.Out, Console.Error);
    }
}
