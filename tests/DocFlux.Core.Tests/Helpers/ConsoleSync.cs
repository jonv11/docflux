namespace DocFlux.Core.Tests.Helpers;

internal static class ConsoleSync
{
    public static object Lock { get; } = new();
}
