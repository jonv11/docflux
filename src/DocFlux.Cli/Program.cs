using System.CommandLine;
using System.CommandLine.Parsing;
using DocFlux.Core.Conversion;

namespace DocFlux.Cli;

public static class Program
{
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

        var root = new RootCommand("DocFlux document format converter.");
        root.Add(sourceFormatArgument);
        root.Add(targetFormatArgument);
        root.Add(contentArgument);
        root.Add(inputFileOption);
        root.Add(outputFileOption);
        root.Add(CreateListFormatsCommand());
        root.SetAction((ParseResult result) =>
            Execute(
                result.GetRequiredValue(sourceFormatArgument),
                result.GetRequiredValue(targetFormatArgument),
                result.GetValue(contentArgument) ?? [],
                result.GetValue(inputFileOption),
                result.GetValue(outputFileOption)));

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
        string sourceFormat,
        string targetFormat,
        IReadOnlyList<string> contentParts,
        string? inputFilePath,
        string? outputFilePath)
    {
        var inlineContent = string.Join(" ", contentParts);

        if (!string.IsNullOrWhiteSpace(inlineContent) && !string.IsNullOrWhiteSpace(inputFilePath))
        {
            Console.Error.WriteLine("Provide either inline content or --input-file, not both.");
            return 1;
        }

        string content;
        if (!string.IsNullOrWhiteSpace(inputFilePath))
        {
            try
            {
                content = File.ReadAllText(inputFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Unable to read input file '{inputFilePath}': {ex.Message}");
                return 3;
            }
        }
        else if (!string.IsNullOrWhiteSpace(inlineContent))
        {
            content = inlineContent;
        }
        else
        {
            Console.Error.WriteLine("No input content provided. Pass inline content or use --input-file.");
            return 1;
        }

        var converter = new DocFluxConverter();
        try
        {
            var converted = converter.Convert(content, sourceFormat, targetFormat);
            if (!string.IsNullOrWhiteSpace(outputFilePath))
            {
                try
                {
                    File.WriteAllText(outputFilePath, converted);
                    Console.WriteLine($"Wrote converted output to '{outputFilePath}'.");
                    return 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Unable to write output file '{outputFilePath}': {ex.Message}");
                    return 3;
                }
            }

            Console.WriteLine(converted);
            return 0;
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
}
