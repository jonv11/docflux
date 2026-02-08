namespace DocFlux.Abstractions.Contracts;

public sealed record FormatWriteOptions
{
    public static FormatWriteOptions Default { get; } = new();

    public string LineEnding { get; init; } = "\n";

    public bool PreferSingleLine { get; init; }

    public bool EmitUnknownNodesAsPlainText { get; init; } = true;

    public bool PreserveUnknownNodes { get; init; } = true;
}
