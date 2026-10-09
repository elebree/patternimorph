using System.Text.RegularExpressions;

namespace Patternimorph.Tests;

/// <summary>Exact LIKE output, rejections, options and SQL predicates for <see cref="RegexToSqlPattern"/> with <see cref="SqlPatternFormat.Like"/>.</summary>
public class LikeConversionTests
{
    private static readonly RegexConversionOptions CaseSensitive = new() { Format = SqlPatternFormat.Like, CaseSensitiveLike = true };

    private static void AssertConverts(string regex, params string[] expected) =>
        AssertConverts(regex, TestOptions.Like, expected);

    private static void AssertConverts(string regex, RegexConversionOptions options, params string[] expected)
    {
        Assert.That(RegexToSqlPattern.Convert(regex, options), Is.EqualTo(expected));

        Assert.That(RegexToSqlPattern.TryConvert(regex, out var patterns, options), Is.True);
        Assert.That(patterns, Is.EqualTo(expected));
    }

    // ------------------------------------------------------------------ conversions

    [TestCase("abc", "%abc%")]
    [TestCase("^abc", "abc%")]
    [TestCase("abc$", "%abc")]
    [TestCase("^abc$", "abc")]
    [TestCase(@"\Aabc\z", "abc")]
    [TestCase("", "%")]
    [TestCase("^$", "")]
    [TestCase("a|", "%")]
    public void Anchors(string regex, string expected) => AssertConverts(regex, expected);

    [TestCase("^a.c$", "a_c")]
    [TestCase("a.*b", "%a%b%")]
    [TestCase("^a.+$", "a_%")]
    [TestCase("^a.*?b$", "a%b")]
    [TestCase("^.{3}$", "___")]
    [TestCase("^.{3,}$", "___%")]
    [TestCase("^.*.$", "_%")]
    [TestCase("^(.*)+$", "%")]
    [TestCase("^(?:.+){2,}$", "__%")]
    [TestCase(@"^[\s\S]$", "_")]
    [TestCase(@"^[\d\D]{2}$", "__")]
    [TestCase(@"^[\w\W]*$", "%")]
    [TestCase(@"^a[\s\S]+$", "a_%")]
    public void Wildcards(string regex, string expected) => AssertConverts(regex, expected);

    [TestCase("^100%$", @"100\%")]
    [TestCase("^a_b$", @"a\_b")]
    [TestCase(@"^a\\b$", @"a\\b")]
    [TestCase(@"^\%$", @"\%")]
    [TestCase("^[%]$", @"\%")]
    [TestCase("^[_]$", @"\_")]
    [TestCase(@"^[\\]$", @"\\")]
    [TestCase(@"^\*\?\[\]$", "*?[]")]
    [TestCase(@"^a\.b$", "a.b")]
    [TestCase(@"^\x41B\t$", "AB\t")]
    [TestCase("^a{,2}$", "a{,2}")]
    [TestCase("^[a]$", "a")]
    [TestCase(@"^[\.]$", ".")]
    [TestCase("^1€$", "1€")]
    [TestCase("^ı$", "ı")] // dotless i: no case variants under invariant IgnoreCase
    public void LiteralsAndEscapes(string regex, string expected) => AssertConverts(regex, expected);

    [Test]
    public void EscapedWildcardsStayDistinctFromWildcards() =>
        AssertConverts("^(?:_|.)(?:%|.*)$", @"\_\%", @"\_%", @"_\%", "_%");

    [Test]
    public void Optional() => AssertConverts("^colou?r$", "color", "colour");

    [Test]
    public void Alternation() => AssertConverts(@"\.(jpg|png)$", "%.jpg", "%.png");

    [Test]
    public void BoundedRepeat() => AssertConverts("^(?:ab){1,2}$", "ab", "abab");

    [Test]
    public void BoundedWildcardRange() => AssertConverts("^x.{0,2}$", "x", "x_", "x__");

    [Test]
    public void AnchorsBelongToEachTopLevelBranch() => AssertConverts("^a|b$", "a%", "%b");

    [TestCase("^[aA]$", "a")]
    [TestCase("^[Aa]$", "a")]
    [TestCase("^[zZ]{2}$", "zz")]
    [TestCase("^[a]$", "a")]
    [TestCase("^[A]$", "A")]
    public void CaseInsensitiveCollapsesAsciiCasePairs(string regex, string expected) =>
        AssertConverts(regex, expected);

    [TestCase("^é$", "é")]
    [TestCase("^Ж$", "Ж")]
    [TestCase("^[A]$", "A")]
    public void CaseSensitiveKeepsAnyLiteral(string regex, string expected) =>
        AssertConverts(regex, CaseSensitive, expected);

    [TestCase(@"\.(jpg|png)", ".jpg", ".png")]
    [TestCase("colou?r", "color", "colour")]
    [TestCase("100%.*", @"100\%%")]
    public void FullMatch(string regex, params string[] expected) =>
        AssertConverts(regex, new RegexConversionOptions { Format = SqlPatternFormat.Like, FullMatch = true }, expected);

    [Test]
    public void DollarBeforeFinalNewline() =>
        AssertConverts("^abc$", new RegexConversionOptions { Format = SqlPatternFormat.Like, DollarMatchesBeforeFinalNewline = true }, "abc", "abc\n");

    // ------------------------------------------------------------------ rejections

    /// <summary>Valid .NET regexes GLOB can express but LIKE cannot (or GLOB cannot either).</summary>
    public static IEnumerable<string> Unsupported =>
    [
        // character classes other than single characters
        "[abc]", "[ab]", "[a-z]", "[^a]", "[^%]", "[aB]", @"\d", @"\w", @"\s", @"\D", @"\W", @"\S", @"[\d]",
        @"[\D]", "[-a]",
        // non-ASCII letters with case under case-insensitive LIKE
        "é", "Ж", "K", "ß", "ẞ", "caf[eé]",
        // unsupported by GLOB too
        "a+", "a*", "(ab)*", @"\bx", "(?=a)", @"(a)\1", "(?i)a", "a(^b)", "a$b", "(a|b){20}",
    ];

    [TestCaseSource(nameof(Unsupported))]
    public void UnsupportedIsValidDotNetRegex(string regex) => _ = new Regex(regex);

    [TestCaseSource(nameof(Unsupported))]
    public void UnsupportedThrowsNotSupported(string regex) =>
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert(regex, TestOptions.Like));

    [TestCaseSource(nameof(Unsupported))]
    public void TryConvertReturnsFalseAndEmpty(string regex)
    {
        Assert.That(RegexToSqlPattern.TryConvert(regex, out var patterns, TestOptions.Like), Is.False);
        Assert.That(patterns, Is.Empty);
    }

    [TestCase("^[aA]$")]
    [TestCase("^[Aa]{2}$")]
    public void CaseSensitiveRejectsCasePairs(string regex) =>
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert(regex, CaseSensitive));

    [TestCase("^a.b$")]
    [TestCase(".")]
    public void DotExcludingNewlineIsRejected(string regex) =>
        Assert.Throws<NotSupportedException>(() =>
            RegexToSqlPattern.Convert(regex, new RegexConversionOptions { Format = SqlPatternFormat.Like, DotMatchesNewline = false }));

    [TestCase("(")]
    [TestCase("[a")]
    [TestCase(".**")]
    [TestCase("a{3,2}")]
    public void MalformedThrowsFormat(string regex) =>
        Assert.Throws<FormatException>(() => RegexToSqlPattern.Convert(regex, TestOptions.Like));

    [Test]
    public void LimitsApply()
    {
        Assert.Throws<NotSupportedException>(() =>
            RegexToSqlPattern.Convert("^(?:a|b){2}$", new RegexConversionOptions { Format = SqlPatternFormat.Like, MaxPatterns = 3 }));
        Assert.Throws<NotSupportedException>(() =>
            RegexToSqlPattern.Convert("^abcdef$", new RegexConversionOptions { Format = SqlPatternFormat.Like, MaxPatternLength = 5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RegexToSqlPattern.TryConvert("a", out _, new RegexConversionOptions { Format = SqlPatternFormat.Like, MaxPatterns = 0 }));
    }

    [Test]
    public void NullRegexThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.Convert(null!, TestOptions.Like));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.TryConvert(null!, out _, TestOptions.Like));
    }

    [Test]
    public void DefaultOptions()
    {
        var o = TestOptions.Like;

        Assert.That(o.Format, Is.EqualTo(SqlPatternFormat.Like));
        Assert.That(o.CaseSensitiveLike, Is.False);
        Assert.That(o.MaxPatterns, Is.EqualTo(256));
        Assert.That(o.MaxPatternLength, Is.EqualTo(4096));
        Assert.That(o.DotMatchesNewline, Is.True);
        Assert.That(o.DollarMatchesBeforeFinalNewline, Is.False);
        Assert.That(o.FullMatch, Is.False);
    }

    // ------------------------------------------------------------------ SQL predicates

    [Test]
    public void EscapeCharacterIsBackslash() => Assert.That(RegexToSqlPattern.LikeEscapeCharacter, Is.EqualTo('\\'));

    [Test]
    public void PositionalPredicate() =>
        Assert.That(
            RegexToSqlPattern.ToSqlPredicate("name", new[] { "a", "b" }, SqlPatternFormat.Like),
            Is.EqualTo(@"(""name"" LIKE ? ESCAPE '\' OR ""name"" LIKE ? ESCAPE '\')"));

    [Test]
    public void NamedPredicate() =>
        Assert.That(
            RegexToSqlPattern.ToSqlPredicate("we\"ird", new[] { "a", "b" }, SqlPatternFormat.Like, "@p"),
            Is.EqualTo(@"(""we""""ird"" LIKE @p0 ESCAPE '\' OR ""we""""ird"" LIKE @p1 ESCAPE '\')"));

    [Test]
    public void EmptyPatternListIsFalse()
    {
        Assert.That(RegexToSqlPattern.ToSqlPredicate("c", Array.Empty<string>(), SqlPatternFormat.Like), Is.EqualTo("(1 = 0)"));
        Assert.That(RegexToSqlPattern.ToSqlPredicate("c", Array.Empty<string>(), SqlPatternFormat.Like, "@p"), Is.EqualTo("(1 = 0)"));
    }

    [TestCase("@")]
    [TestCase("p")]
    [TestCase("@p;x")]
    public void NamedPredicateRejectsInvalidPrefix(string prefix) =>
        Assert.Throws<ArgumentException>(() => RegexToSqlPattern.ToSqlPredicate("c", new[] { "x" }, SqlPatternFormat.Like, prefix));

    [Test]
    public void PredicateNullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate(null!, new[] { "x" }, SqlPatternFormat.Like));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate("c", null!, SqlPatternFormat.Like));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate("c", new[] { "x" }, SqlPatternFormat.Like, null!));
    }
}
