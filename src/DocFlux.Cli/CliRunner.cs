using DocFlux.Abstractions.Contracts;
using DocFlux.Core.Conversion;

namespace DocFlux.Cli;

internal sealed class CliRunner
{
    private readonly ICliFileSystem _fileSystem;
    private readonly Func<DocFluxConverter> _converterFactory;

    public CliRunner(ICliFileSystem fileSystem, Func<DocFluxConverter>? converterFactory = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _converterFactory = converterFactory ?? (() => new DocFluxConverter());
    }

    public int Run(CliRunRequest request, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var inlineContent = string.Join(" ", request.ContentParts);

        if (!string.IsNullOrWhiteSpace(inlineContent) && !string.IsNullOrWhiteSpace(request.InputFilePath))
        {
            stderr.WriteLine("Provide either inline content or --input-file, not both.");
            return 1;
        }

        if (request.Compact && request.Pretty)
        {
            stderr.WriteLine("Use either --compact or --pretty, not both.");
            return 1;
        }

        if (!TryParseBooleanOption(request.PreserveUnknown, out var preserveUnknownNodes))
        {
            stderr.WriteLine("Invalid value for --preserve-unknown. Use true or false.");
            return 1;
        }

        if (!TryParseBooleanOption(request.EmitUnknownAsPlainText, out var emitUnknownNodesAsPlainText))
        {
            stderr.WriteLine("Invalid value for --emit-unknown-as-plain-text. Use true or false.");
            return 1;
        }

        if (!TryParseLineEnding(request.LineEnding, out var resolvedLineEnding))
        {
            stderr.WriteLine("Invalid value for --line-ending. Use lf or crlf.");
            return 1;
        }

        string content;
        if (!string.IsNullOrWhiteSpace(request.InputFilePath))
        {
            try
            {
                content = _fileSystem.ReadAllText(request.InputFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"Unable to read input file '{request.InputFilePath}': {ex.Message}");
                return 3;
            }
        }
        else if (!string.IsNullOrWhiteSpace(inlineContent))
        {
            content = inlineContent;
        }
        else
        {
            content = stdin.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                stderr.WriteLine("No input content provided. Pass inline content, use --input-file, or pipe stdin.");
                return 1;
            }
        }

        var converter = _converterFactory();
        try
        {
            var preferSingleLine = request.Compact
                || (!request.Pretty && request.TargetFormat.Equals("adf", StringComparison.OrdinalIgnoreCase));
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

            var converted = converter.Convert(content, request.SourceFormat, request.TargetFormat, conversionOptions);
            if (!string.IsNullOrWhiteSpace(request.OutputFilePath))
            {
                try
                {
                    _fileSystem.WriteAllText(request.OutputFilePath, converted);
                    stdout.WriteLine($"Wrote converted output to '{request.OutputFilePath}'.");
                    return 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    stderr.WriteLine($"Unable to write output file '{request.OutputFilePath}': {ex.Message}");
                    return 3;
                }
            }

            stdout.WriteLine(converted);
            return 0;
        }
        catch (NotSupportedException ex)
        {
            stderr.WriteLine(ex.Message);
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
