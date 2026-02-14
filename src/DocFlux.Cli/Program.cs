using System.CommandLine;
using System.CommandLine.Parsing;
using DocFlux.Abstractions.Contracts;
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
        root.SetAction((ParseResult result) =>
            Execute(
                result.GetRequiredValue(sourceFormatArgument),
                result.GetRequiredValue(targetFormatArgument),
                result.GetValue(contentArgument) ?? [],
                result.GetValue(inputFileOption),
                result.GetValue(outputFileOption),
                result.GetValue(preserveUnknownOption) ?? "true",
                result.GetValue(emitUnknownAsPlainTextOption) ?? "true",
                result.GetValue(lineEndingOption) ?? "lf",
                result.GetValue(compactOption),
                result.GetValue(prettyOption)));

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
        string? outputFilePath,
        string preserveUnknown,
        string emitUnknownAsPlainText,
        string lineEnding,
        bool compact,
        bool pretty)
    {
        var inlineContent = string.Join(" ", contentParts);

        if (!string.IsNullOrWhiteSpace(inlineContent) && !string.IsNullOrWhiteSpace(inputFilePath))
        {
            Console.Error.WriteLine("Provide either inline content or --input-file, not both.");
            return 1;
        }

        if (compact && pretty)
        {
            Console.Error.WriteLine("Use either --compact or --pretty, not both.");
            return 1;
        }

        if (!TryParseBooleanOption(preserveUnknown, out var preserveUnknownNodes))
        {
            Console.Error.WriteLine("Invalid value for --preserve-unknown. Use true or false.");
            return 1;
        }

        if (!TryParseBooleanOption(emitUnknownAsPlainText, out var emitUnknownNodesAsPlainText))
        {
            Console.Error.WriteLine("Invalid value for --emit-unknown-as-plain-text. Use true or false.");
            return 1;
        }

        if (!TryParseLineEnding(lineEnding, out var resolvedLineEnding))
        {
            Console.Error.WriteLine("Invalid value for --line-ending. Use lf or crlf.");
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
            content = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                Console.Error.WriteLine("No input content provided. Pass inline content, use --input-file, or pipe stdin.");
                return 1;
            }
        }

        var converter = new DocFluxConverter();
        try
        {
            var preferSingleLine = compact
                || (!pretty && targetFormat.Equals("adf", StringComparison.OrdinalIgnoreCase));
            var conversionOptions = new ConversionOptions
            {
                ReadOptions = new FormatReadOptions
                {
                    PreserveUnknownNodes = preserveUnknownNodes,
                },
                WriteOptions = new FormatWriteOptions
                {
                    LineEnding = resolvedLineEnding,
                    PreferSingleLine = preferSingleLine,
                    EmitUnknownNodesAsPlainText = emitUnknownNodesAsPlainText,
                    PreserveUnknownNodes = preserveUnknownNodes,
                },
            };

            var converted = converter.Convert(content, sourceFormat, targetFormat, conversionOptions);
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

    private static bool TryParseBooleanOption(string value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value.Trim(), out parsed);
    }

    private static bool TryParseLineEnding(string value, out string lineEnding)
    {
        lineEnding = "\n";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("lf", StringComparison.OrdinalIgnoreCase))
        {
            lineEnding = "\n";
            return true;
        }

        if (value.Equals("crlf", StringComparison.OrdinalIgnoreCase))
        {
            lineEnding = "\r\n";
            return true;
        }

        return false;
    }
}
