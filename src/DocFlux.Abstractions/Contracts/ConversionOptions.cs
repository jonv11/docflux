namespace DocFlux.Abstractions.Contracts;

public sealed record ConversionOptions
{
    public static ConversionOptions Default { get; } = new();

    public FormatReadOptions ReadOptions { get; init; } = FormatReadOptions.Default;

    public FormatWriteOptions WriteOptions { get; init; } = FormatWriteOptions.Default;
}
