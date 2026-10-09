namespace Patternimorph.Tests;

/// <summary>Exact GLOB output for supported regex constructs (default options).</summary>
public class ConversionTests
{
    private static void AssertConverts(string regex, params string[] expected)
    {
        Assert.That(RegexToSqlPattern.Convert(regex), Is.EqualTo(expected));

        Assert.That(RegexToSqlPattern.TryConvert(regex, out var globs), Is.True);
        Assert.That(globs, Is.EqualTo(expected));
    }

    [TestCase("abc", "*abc*")]
    [TestCase("^abc", "abc*")]
    [TestCase("abc$", "*abc")]
    [TestCase("^abc$", "abc")]
    [TestCase(@"\Aabc\z", "abc")]
    [TestCase(@"\Aabc\Z", "abc")]
    [TestCase("", "*")]
    [TestCase("^", "*")]
    [TestCase("$", "*")]
    [TestCase("^$", "")]
    public void Anchors(string regex, string expected) => AssertConverts(regex, expected);

    [TestCase("^a.c$", "a?c")]
    [TestCase("a.*b", "*a*b*")]
    [TestCase("^a.+$", "a?*")]
    [TestCase("^a.*?b$", "a*b")]
    [TestCase("^.{3}$", "???")]
    [TestCase("^.{3,}$", "???*")]
    [TestCase("^.*.*$", "*")]
    [TestCase("^.*.$", "?*")]
    [TestCase("^(.*)+$", "*")]
    [TestCase("^(?:.+){2,}$", "??*")]
    [TestCase("^(?:.+)?$", "*")]
    [TestCase("^(?:.{2,})+$", "??*")]
    [TestCase(".*", "*")]
    [TestCase(@"^[\s\S]$", "?")]
    [TestCase(@"^[\d\D]{2}$", "??")]
    [TestCase(@"^[\w\W]*$", "*")]
    [TestCase(@"^a[\S\s]+$", "a?*")]
    [TestCase(@"^[\x00-￿]$", "?")]
    public void Wildcards(string regex, string expected) => AssertConverts(regex, expected);

    [Test]
    public void BoundedWildcardRangeExpands() => AssertConverts("^.{1,3}$", "?", "??", "???");

    [Test]
    public void AnyPatternMatchingEverythingCollapsesToStar() => AssertConverts("^abc$|x|.*", "*");

    [TestCase("^[abc]$", "[a-c]")]
    [TestCase("^[a-cx]$", "[a-cx]")]
    [TestCase("^[xa-c]$", "[a-cx]")]
    [TestCase("^[ab]$", "[ab]")]
    [TestCase("^[^abc]$", "[^a-c]")]
    [TestCase("^[a]$", "a")]
    [TestCase("^[aa]$", "a")]
    [TestCase("^[^a]$", "[^a]")]
    [TestCase("^[*]$", "[*]")]
    [TestCase("^[?]$", "[?]")]
    [TestCase("^[[]$", "[[]")]
    [TestCase("^[]]$", "]")]
    [TestCase("^[]a]$", "[]a]")]
    [TestCase(@"^[\]a]$", "[]a]")]
    [TestCase("^[-a]$", "[a-]")]
    [TestCase("^[a-]$", "[a-]")]
    [TestCase(@"^[a\-z]$", "[az-]")]
    [TestCase(@"^[\^a]$", "[a^]")]
    [TestCase("^[-]$", "-")]
    [TestCase("^[-^]$", "[-^]")]
    [TestCase("^[]^-]$", "[]^-]")]
    [TestCase("^[^^]$", "[^^]")]
    [TestCase("^[^-]$", "[^-]")]
    [TestCase("^[^]]$", "[^]]")]
    [TestCase("^[!-~]$", @"[]!-,.-\_-~^-]")]
    [TestCase(@"^[\d-z]$", "[0-9z-]")]
    [TestCase(@"^[\d\s]$", "[\t-\r 0-9]")]
    [TestCase(@"^[\w-]$", "[0-9A-Z_a-z-]")]
    [TestCase(@"^[\n\t]$", "[\t\n]")]
    [TestCase(@"^[\x41-\x43]$", "[A-C]")]
    [TestCase(@"^[\b]$", "\b")]
    public void CharacterClasses(string regex, string expected) => AssertConverts(regex, expected);

    [TestCase(@"^\d$", "[0-9]")]
    [TestCase(@"^\D$", "[^0-9]")]
    [TestCase(@"^\w$", "[0-9A-Z_a-z]")]
    [TestCase(@"^\W$", "[^0-9A-Z_a-z]")]
    [TestCase(@"^\s$", "[\t-\r ]")]
    [TestCase(@"^\S$", "[^\t-\r ]")]
    public void Shorthands(string regex, string expected) => AssertConverts(regex, expected);

    [TestCase(@"^\n\r\t\f\v\a\e$", "\n\r\t\f\v\a\u001B")]
    [TestCase(@"^\x41B$", "AB")]
    [TestCase(@"^\*\?\[\]\.\\\{\}\(\)\|\^\$\+$", @"[*][?][[]].\{}()|^$+")]
    [TestCase(@"^\ \#$", " #")]
    [TestCase("^a{$", "a{")]
    [TestCase("^a{,2}$", "a{,2}")]
    [TestCase("^a{x}$", "a{x}")]
    [TestCase("^}]$", "}]")]
    public void LiteralsAndEscapes(string regex, string expected) => AssertConverts(regex, expected);

    [Test]
    public void Optional() => AssertConverts("^ab?c$", "ac", "abc");

    [Test]
    public void LazyOptional() => AssertConverts("^a??b$", "b", "ab");

    [Test]
    public void ExactRepeat() => AssertConverts("^a{3}$", "aaa");

    [Test]
    public void LazyExactRepeat() => AssertConverts("^a{2}?$", "aa");

    [Test]
    public void ZeroRepeat() => AssertConverts("^ab{0}c$", "ac");

    [Test]
    public void BoundedRepeat() => AssertConverts("^a{1,3}$", "a", "aa", "aaa");

    [Test]
    public void BoundedRepeatOfAlternation() => AssertConverts("^(?:a|b){2}$", "aa", "ab", "ba", "bb");

    [Test]
    public void RepeatDeduplicates() => AssertConverts("^(?:a|aa){1,2}$", "a", "aa", "aaa", "aaaa");

    [Test]
    public void EmptyGroupRepeat() => AssertConverts("^a()*b$", "ab");

    [Test]
    public void GroupAlternation() => AssertConverts("^(ab|cd)e$", "abe", "cde");

    [Test]
    public void NestedGroups() => AssertConverts("^(a(b|c))d$", "abd", "acd");

    [Test]
    public void NamedGroups() => AssertConverts("^(?<first>x)(?'second'y)(?<3>z)$", "xyz");

    [Test]
    public void DuplicateAlternativesAreRemoved() => AssertConverts("^(a|a)$|^a$", "a");

    [Test]
    public void AnchorsBelongToEachTopLevelBranch() => AssertConverts("^a|b$", "a*", "*b");

    [Test]
    public void EmptyTopLevelBranchMatchesEverything() => AssertConverts("a|", "*");

    [Test]
    public void CrossProductOfGroups() =>
        AssertConverts(@"^(ab|cd)\d{2}$", "ab[0-9][0-9]", "cd[0-9][0-9]");

    [Test]
    public void NonBmpLiteralPassesThrough() => AssertConverts("^\U0001F600$", "\U0001F600");
}
