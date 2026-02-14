namespace DocFlux.Cli;

internal sealed record CliRunRequest(
    string SourceFormat,
    string TargetFormat,
    IReadOnlyList<string> ContentParts,
    string? InputFilePath,
    string? OutputFilePath,
    string PreserveUnknown,
    string EmitUnknownAsPlainText,
    string LineEnding,
    bool Compact,
    bool Pretty);
