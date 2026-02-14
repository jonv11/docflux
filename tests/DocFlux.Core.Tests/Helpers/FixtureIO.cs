using System.Text;

namespace DocFlux.Core.Tests.Helpers;

internal static class FixtureIO
{
    public static string ReadFixture(params string[] segments)
    {
        var path = TestPathHelper.FixturePath(segments);
        return File.ReadAllText(path, Encoding.UTF8);
    }
}
