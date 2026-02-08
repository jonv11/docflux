using System.Text;
using System.Text.Json;
using DocFlux.Core.Conversion;

namespace DocFlux.Core.Tests;

public sealed class FixtureBasedConversionTests
{
    [Theory]
    [MemberData(nameof(TextCases))]
    public void Convert_TextFixture_MatchesExpected(string caseName)
    {
        var fixtureCase = GetCase(caseName, "text");
        var converter = new DocFluxConverter();
        var input = ReadFixture(fixtureCase.Input);
        var expected = NormalizeLineEndings(ReadFixture(fixtureCase.Expected));

        var actual = NormalizeLineEndings(converter.Convert(input, fixtureCase.SourceFormat, fixtureCase.TargetFormat));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(JsonCases))]
    public void Convert_JsonFixture_MatchesExpectedJson(string caseName)
    {
        var fixtureCase = GetCase(caseName, "json");
        var converter = new DocFluxConverter();
        var input = ReadFixture(fixtureCase.Input);
        var expected = ReadFixture(fixtureCase.Expected);

        var actual = converter.Convert(input, fixtureCase.SourceFormat, fixtureCase.TargetFormat);

        Assert.Equal(CanonicalJson(expected), CanonicalJson(actual));
    }

    public static IEnumerable<object[]> TextCases()
    {
        return LoadCases("text").Select(item => new object[] { item.Name });
    }

    public static IEnumerable<object[]> JsonCases()
    {
        return LoadCases("json").Select(item => new object[] { item.Name });
    }

    private static string ReadFixture(string relativePath)
    {
        var path = TestPathHelper.FixturePath(relativePath.Split('/'));
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string CanonicalJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static FixtureCase GetCase(string name, string expectedComparison)
    {
        var match = LoadCases(expectedComparison).SingleOrDefault(item =>
            item.Name.Equals(name, StringComparison.Ordinal));

        if (match is null)
        {
            throw new InvalidOperationException(
                $"Fixture case '{name}' with comparison '{expectedComparison}' was not found.");
        }

        return match;
    }

    private static IReadOnlyList<FixtureCase> LoadCases(string comparison)
    {
        var manifestPath = TestPathHelper.FixturePath("cases.json");
        var json = File.ReadAllText(manifestPath, Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<CasesManifest>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Fixture manifest is empty.");

        return manifest.Cases
            .Where(item => item.Comparison.Equals(comparison, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private sealed record CasesManifest(IReadOnlyList<FixtureCase> Cases);

    private sealed record FixtureCase(
        string Name,
        string SourceFormat,
        string TargetFormat,
        string Input,
        string Expected,
        string Comparison);
}
