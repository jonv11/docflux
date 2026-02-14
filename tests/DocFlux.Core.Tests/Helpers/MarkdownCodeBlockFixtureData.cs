namespace DocFlux.Core.Tests.Helpers;

internal static class MarkdownCodeBlockFixtureData
{
    public static IReadOnlyList<FixtureCase> Cases { get; } = CreateCases();

    public static IEnumerable<object[]> FixtureCaseNames()
    {
        return Cases.Select(item => new object[] { item.Name });
    }

    private static IReadOnlyList<FixtureCase> CreateCases()
    {
        return
        [
            new FixtureCase(
                "case-01-multiple-code-blocks-mixed-language",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "public class App {",
                            "    public static void main(String[] args) {",
                            "        System.out.println(\"hello\");",
                            "    }",
                            "}"),
                        "java"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "SELECT *",
                            "FROM users",
                            "WHERE active = 1;"),
                        null),
                    new ExpectedCodeBlock("Console.WriteLine(\"done\");", "csharp"),
                ]),
            new FixtureCase(
                "case-02-triple-backticks-inside-code-content",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "String start = \"begin\";",
                            "```",
                            "String finish = \"end\";"),
                        "java"),
                    new ExpectedCodeBlock("after-first-block", "text"),
                ]),
            new FixtureCase(
                "case-03-embedded-looking-fences-with-prose",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "def render():",
                            "    text = \"~~~ not a fence here\"",
                            "    return \"``` also not closing because this is code\""),
                        "python"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "```",
                            "fake nested block text",
                            "```"),
                        null),
                ]),
            new FixtureCase(
                "case-04-comments-with-embedded-fence-text",
                false,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "/*",
                            " Multi-line comment mentions ``` and must remain text.",
                            "*/",
                            "public class Commented {",
                            "    // Inline comment with ``` marker",
                            "    String value = \"ok\";",
                            "}"),
                        "java"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "/* plain block comment */",
                            "var x = 1; // ``` fence token in a line comment"),
                        null),
                ]),
            new FixtureCase(
                "case-05-mixed-structure-heading-paragraph-list-code",
                false,
                [
                    new ExpectedCodeBlock("echo \"prepare\"", "bash"),
                    new ExpectedCodeBlock(
                        JoinLines(
                            "key: value",
                            "nested:",
                            "  flag: true"),
                        "yaml"),
                ]),
            new FixtureCase(
                "case-06-tilde-fences-and-backtick-fences",
                false,
                [
                    new ExpectedCodeBlock("select 1;", "sql"),
                    new ExpectedCodeBlock("{ \"a\": 1 }", "json"),
                    new ExpectedCodeBlock(JoinLines("no language tilde block", "line two"), null),
                ]),
            new FixtureCase(
                "case-07-trailing-spaces-blank-lines-crlf-input",
                true,
                [
                    new ExpectedCodeBlock(
                        JoinLines(
                            "first line with trailing spaces.   ",
                            "  indented line",
                            "third line"),
                        "txt"),
                    new ExpectedCodeBlock(JoinLines("alpha", "beta"), null),
                ]),
            new FixtureCase(
                "case-08-large-code-block-stress",
                false,
                [
                    new ExpectedCodeBlock(BuildLargeCodePayload(), "txt"),
                ]),
            new FixtureCase(
                "case-09-sh-language-and-no-language",
                false,
                [
                    new ExpectedCodeBlock(JoinLines("npm ci", "npm test"), "bash"),
                    new ExpectedCodeBlock("echo \"with info string\"", "bash"),
                    new ExpectedCodeBlock("plain output", null),
                ]),
            new FixtureCase(
                "case-10-complex-fence-info-attributes",
                false,
                [
                    new ExpectedCodeBlock(JoinLines("echo \"one\"", "echo \"two\""), "bash"),
                ]),
        ];
    }

    private static string JoinLines(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    private static string BuildLargeCodePayload()
    {
        var lines = Enumerable.Range(1, 1500).Select(index => $"line {index} -> value_{index % 17}");
        return string.Join("\n", lines);
    }

    internal sealed record FixtureCase(
        string Name,
        bool UseCrlfInput,
        IReadOnlyList<ExpectedCodeBlock> ExpectedCodeBlocks);

    internal sealed record ExpectedCodeBlock(string CodeText, string? Language);
}
