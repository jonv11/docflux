namespace DocFlux.Abstractions.Contracts;

public sealed record FormatReadOptions
{
    public static FormatReadOptions Default { get; } = new();

    public bool NormalizeLineEndings { get; init; } = true;

    public bool PreserveUnknownNodes { get; init; } = true;
}
