namespace Patternimorph.Tests;

public class OptionsTests
{
    private static readonly RegexConversionOptions DotExcludesNewline = new() { DotMatchesNewline = false };
    private static readonly RegexConversionOptions DollarBeforeNewline = new() { DollarMatchesBeforeFinalNewline = true };

    [Test]
    public void DefaultsAreDocumentedValues()
    {
        var o = RegexConversionOptions.Default;

        Assert.That(o.Format, Is.EqualTo(SqlPatternFormat.Glob));
        Assert.That(o.CaseSensitiveLike, Is.False);
        Assert.That(o.MaxPatterns, Is.EqualTo(256));
        Assert.That(o.MaxPatternLength, Is.EqualTo(4096));
        Assert.That(o.DotMatchesNewline, Is.True);
        Assert.That(o.DollarMatchesBeforeFinalNewline, Is.False);
        Assert.That(o.FullMatch, Is.False);
    }

    private static readonly RegexConversionOptions FullMatch = new() { FullMatch = true };

    [TestCase("abc", "abc")]
    [TestCase("^abc", "abc")]
    [TestCase("abc$", "abc")]
    [TestCase(@"\Aabc\z", "abc")]
    [TestCase("", "")]
    [TestCase("a.*", "a*")]
    [TestCase(".*", "*")]
    [TestCase(@"\d{2}", "[0-9][0-9]")]
    public void FullMatchDropsEdgeWildcards(string regex, string expected) =>
        Assert.That(RegexToSqlPattern.Convert(regex, FullMatch), Is.EqualTo(new[] { expected }));

    [Test]
    public void FullMatchAppliesToEveryBranch() =>
        Assert.That(RegexToSqlPattern.Convert("a|^b|c$", FullMatch), Is.EqualTo(new[] { "a", "b", "c" }));

    [TestCase("abc$")]
    [TestCase(@"abc\Z")]
    public void FullMatchIgnoresDollarBeforeFinalNewline(string regex) =>
        Assert.That(
            RegexToSqlPattern.Convert(regex, new RegexConversionOptions { FullMatch = true, DollarMatchesBeforeFinalNewline = true }),
            Is.EqualTo(new[] { "abc" }));

    [TestCase("a$b")]
    [TestCase("a(^b)")]
    [TestCase("a+")]
    public void FullMatchStillRejectsUnsupported(string regex) =>
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert(regex, FullMatch));

    [Test]
    public void DotExcludingNewlineEmitsNegatedClass() =>
        Assert.That(RegexToSqlPattern.Convert("^a.b$", DotExcludesNewline), Is.EqualTo(new[] { "a[^\n]b" }));

    [Test]
    public void DotExcludingNewlineAllowsBoundedRepeat() =>
        Assert.That(RegexToSqlPattern.Convert("^.{2}$", DotExcludesNewline), Is.EqualTo(new[] { "[^\n][^\n]" }));

    [TestCase(".*")]
    [TestCase("a.+")]
    [TestCase(".{2,}")]
    public void DotExcludingNewlineRejectsUnboundedDot(string regex) =>
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert(regex, DotExcludesNewline));

    [TestCase("^abc$")]
    [TestCase(@"^abc\Z")]
    public void DollarBeforeFinalNewlineAddsNewlineVariant(string regex) =>
        Assert.That(RegexToSqlPattern.Convert(regex, DollarBeforeNewline), Is.EqualTo(new[] { "abc", "abc\n" }));

    [Test]
    public void LowercaseZIsAlwaysEndOfStringOnly() =>
        Assert.That(RegexToSqlPattern.Convert(@"^abc\z", DollarBeforeNewline), Is.EqualTo(new[] { "abc" }));

    [Test]
    public void DollarBeforeFinalNewlineAppliesPerBranch() =>
        Assert.That(RegexToSqlPattern.Convert("a$|^b", DollarBeforeNewline), Is.EqualTo(new[] { "*a", "*a\n", "b*" }));

    [Test]
    public void MaxPatternsIsInclusive()
    {
        var four = new RegexConversionOptions { MaxPatterns = 4 };
        var three = new RegexConversionOptions { MaxPatterns = 3 };

        Assert.That(RegexToSqlPattern.Convert("^(?:a|b){2}$", four).Count, Is.EqualTo(4));
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert("^(?:a|b){2}$", three));
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert("^a|^b|^c|^d$", three));
    }

    [Test]
    public void MaxPatternLengthIsInclusive()
    {
        var five = new RegexConversionOptions { MaxPatternLength = 5 };

        Assert.That(RegexToSqlPattern.Convert("^a{5}$", five), Is.EqualTo(new[] { "aaaaa" }));
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert("^a{6}$", five));
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert("^abcdef$", five));
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert("^.{5,}$", five));
        Assert.That(RegexToSqlPattern.Convert("^.{4,}$", five), Is.EqualTo(new[] { "????*" }));
    }

    [TestCase(0, 10)]
    [TestCase(-1, 10)]
    [TestCase(10, 0)]
    public void InvalidLimitsThrowEvenFromTryConvert(int maxPatterns, int maxPatternLength)
    {
        var options = new RegexConversionOptions { MaxPatterns = maxPatterns, MaxPatternLength = maxPatternLength };

        Assert.Throws<ArgumentOutOfRangeException>(() => RegexToSqlPattern.Convert("a", options));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegexToSqlPattern.TryConvert("a", out _, options));
    }

    [Test]
    public void CaseSensitiveLikeDoesNotAffectGlob() =>
        Assert.That(
            RegexToSqlPattern.Convert("[aA]", new RegexConversionOptions { CaseSensitiveLike = false }),
            Is.EqualTo(new[] { "*[Aa]*" }));

    [Test]
    public void UnknownFormatThrows()
    {
        var options = new RegexConversionOptions { Format = (SqlPatternFormat)42 };

        Assert.Throws<ArgumentOutOfRangeException>(() => RegexToSqlPattern.Convert("a", options));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegexToSqlPattern.TryConvert("a", out _, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegexToSqlPattern.ToSqlPredicate("c", new[] { "x" }, (SqlPatternFormat)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegexToSqlPattern.ToSqlPredicate("c", new[] { "x" }, (SqlPatternFormat)42, "@p"));
    }
}
