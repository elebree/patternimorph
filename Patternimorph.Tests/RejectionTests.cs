using System.Text.RegularExpressions;

namespace Patternimorph.Tests;

/// <summary>Regexes that must not be converted, and how the failure is reported.</summary>
public class RejectionTests
{
    /// <summary>Valid .NET regexes with no exact GLOB equivalent.</summary>
    public static IEnumerable<string> Unsupported =>
    [
        // unbounded repetition of anything but an arbitrary-character wildcard
        "a+", "a*", "^a*$", "a+?", "[0-9]+", @"\d*", "(ab)*", "a{2,}", "(?:..)*", "(?:.*|a)+",
        // zero-width assertions
        @"\bx", @"x\B", @"\Gx", "(?=a)", "(?!a)", "(?<=a)", "(?<!a)",
        // other group kinds and options
        "(?>a)", "(?i)a", "(?i:a)", "(?#comment)a", "(?<a>x)(?<b-a>y)", "(?(a)b|c)",
        // backreferences, unicode categories, control and octal escapes
        @"(a)\1", @"(?<n>a)\k<n>", @"\p{L}", @"\P{L}", @"\cA", @"\0",
        // class forms
        "[a-z-[aeiou]]", @"[\D]", @"[\W]", @"[\S]", @"[\p{L}]", @"[\uD800-\uDFFF]",
        // misplaced anchors
        "a(^b)", "(a$)", "a$b", "a^", @"a\Ab", @"(\z)", "a$$", "^^a", "a$*",
        // size limits
        "a{5000}", "(a|b){20}",
    ];

    /// <summary>Malformed regexes (rejected by .NET too).</summary>
    public static IEnumerable<string> Malformed =>
    [
        "(", "(a", "((a)", ")", "a)", "[", "[a", "[]", @"[\", @"\",
        // nested quantifiers use '.' so the first quantifier is itself convertible
        "*", "+a", "?", "{2}", "a{2}{3}", ".**", ".++", ".*?*",
        "[z-a]", @"[a-\d]", "a{3,2}", "a{99999999999}",
        @"\x4", @"\xZZ", @"\u12", @"\u12G4",
        "(?<>a)", "(?<a", "(?'a",
    ];

    [TestCaseSource(nameof(Unsupported))]
    public void UnsupportedIsValidDotNetRegex(string regex) => _ = new Regex(regex);

    [TestCaseSource(nameof(Unsupported))]
    public void UnsupportedThrowsNotSupported(string regex) =>
        Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert(regex));

    [TestCaseSource(nameof(Malformed))]
    public void MalformedIsInvalidDotNetRegex(string regex) =>
        Assert.That(() => new Regex(regex), Throws.InstanceOf<ArgumentException>());

    [TestCaseSource(nameof(Malformed))]
    public void MalformedThrowsFormat(string regex) =>
        Assert.Throws<FormatException>(() => RegexToSqlPattern.Convert(regex));

    [TestCaseSource(nameof(Unsupported))]
    [TestCaseSource(nameof(Malformed))]
    public void TryConvertReturnsFalseAndEmpty(string regex)
    {
        Assert.That(RegexToSqlPattern.TryConvert(regex, out var globs), Is.False);
        Assert.That(globs, Is.Empty);
    }

    [Test]
    public void NullRegexThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.Convert(null!));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.TryConvert(null!, out _));
    }

    [Test]
    public void FailureMessageExplainsReason()
    {
        var ex = Assert.Throws<NotSupportedException>(() => RegexToSqlPattern.Convert("a+"));
        Assert.That(ex!.Message, Does.Contain("Unbounded repetition"));
    }
}
