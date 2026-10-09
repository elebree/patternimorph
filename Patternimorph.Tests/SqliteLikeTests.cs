using System.Text.RegularExpressions;

namespace Patternimorph.Tests;

/// <summary>Regexes whose LIKE conversion is compared against .NET in both LIKE case modes.</summary>
public static class LikeRegexes
{
    public static IEnumerable<string> All =>
    [
        "", "abc", "^abc", "abc$", "^abc$", "^$", @"\Aa", @"a\z", "^a|b$", "a|",
        "^a.c$", "a.*c", "^a.+c$", "^.{2,}$", "^.{1,3}$", "b.{2}$", "(?:.*)+", "^(?:.+){2,}$", "^a??b.*?$",
        "^(ab|cd)e?$", "^(a|b|)(c|)$", "^x{3}$", "^(?:a|b){1,3}$", "^(?<n>ab)?c",
        "^colou?r$", @"\.(jpg|png)$", "^100%$", "%", "_", "^a_b$", "^a%b$", @"\\", @"^\\%$", @"a\*b", @"a\?b",
        @"\[x", "[]]", "^[%]$", "^[_]{1,2}$", @"^a[\s\S]c$", @"[\w\W]{2}", "^[aA]b", "^[xX][yY]?$", "C",
        @"x\x41B", "a{", "^a{,2}$", @"^[\n]", @"\t.\n",
    ];

    /// <summary>Uppercase literals: case-folded by default LIKE, exact under case_sensitive_like.</summary>
    public static IEnumerable<string> UppercaseLiterals => ["^A", "^[A]$", "X", "^Abc$"];
}

/// <summary>SQLite's default LIKE (ASCII case-insensitive), compared with RegexOptions.IgnoreCase.</summary>
public class SqliteLikeCaseInsensitiveTests
{
    private SqliteFixture _db = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _db = new SqliteFixture();
        _db.SetCaseSensitiveLike(false);
    }

    [OneTimeTearDown]
    public void TearDown() => _db.Dispose();

    [TestCaseSource(typeof(LikeRegexes), nameof(LikeRegexes.All))]
    [TestCaseSource(typeof(LikeRegexes), nameof(LikeRegexes.UppercaseLiterals))]
    public void MatchesIgnoreCaseRegex(string pattern)
    {
        var patterns = RegexToSqlPattern.Convert(pattern, TestOptions.Like);
        var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        LikeAssert.SameMatches(_db, pattern, patterns, regex, LikeAssert.WithoutFinalNewlineIfAnchored(pattern, _db.Corpus));
    }

    [TestCaseSource(typeof(LikeRegexes), nameof(LikeRegexes.All))]
    public void FullMatchMatchesWholeStringIgnoreCaseRegex(string pattern)
    {
        var patterns = RegexToSqlPattern.Convert(pattern, new RegexConversionOptions { Format = SqlPatternFormat.Like, FullMatch = true });
        var regex = new Regex(@"\A(?:" + pattern + @")\z",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        LikeAssert.SameMatches(_db, pattern, patterns, regex, _db.Corpus);
    }

    [Test]
    public void ExactDollarSemantics()
    {
        var options = new RegexConversionOptions { Format = SqlPatternFormat.Like, DollarMatchesBeforeFinalNewline = true };

        foreach (string pattern in new[] { "^abc$", "c$", @"a\Z", "^$", "b$|^a" })
        {
            var patterns = RegexToSqlPattern.Convert(pattern, options);
            var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            LikeAssert.SameMatches(_db, pattern, patterns, regex, _db.Corpus);
        }
    }

    [Test]
    public void AsciiCaseIsIgnored()
    {
        var patterns = RegexToSqlPattern.Convert("^abc$", TestOptions.Like);

        Assert.That(_db.LikeMatches("abc", patterns), Is.True);
        Assert.That(_db.LikeMatches("ABC", patterns), Is.True);
        Assert.That(_db.LikeMatches("aBc", patterns), Is.True);
        Assert.That(_db.LikeMatches("abcd", patterns), Is.False);
    }

    [Test]
    public void EndToEndFilterExample()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "CREATE TEMP TABLE files (name TEXT); " +
                          "INSERT INTO files VALUES ('a.jpg'), ('b.PNG'), ('c.gif'), ('jpg'), ('d_jpg'), ('100%.png'), (NULL)";
        cmd.ExecuteNonQuery();

        var patterns = RegexToSqlPattern.Convert(@"\.(jpg|png)$", TestOptions.Like);
        cmd.CommandText = "SELECT name FROM files WHERE " +
                          RegexToSqlPattern.ToSqlPredicate("name", patterns, SqlPatternFormat.Like, "@p") + " ORDER BY name";
        for (int i = 0; i < patterns.Count; i++)
            cmd.Parameters.AddWithValue("@p" + i, patterns[i]);

        var names = new List<string>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                names.Add(reader.GetString(0));

        Assert.That(names, Is.EqualTo(new[] { "100%.png", "a.jpg", "b.PNG" }));
    }

    [Test]
    public void EscapedWildcardsAreLiteral()
    {
        var patterns = RegexToSqlPattern.Convert("^a_b%$", TestOptions.Like);

        Assert.That(_db.LikeMatches("a_b%", patterns), Is.True);
        Assert.That(_db.LikeMatches("axb%", patterns), Is.False);
        Assert.That(_db.LikeMatches("a_bxyz", patterns), Is.False);
    }

    /// <summary>
    /// Documented differences. If one of these starts failing, update the README's LIKE caveats.
    /// </summary>
    [TestCase("^k$", "K")]       // KELVIN SIGN: .NET case-folds it to 'k', LIKE does not
    [TestCase("^..$", "\U0001F600")]   // regex sees 2 UTF-16 units, LIKE sees 1 code point
    [TestCase("^abc$", "abc\n")]       // '$' before a final newline is opt-in
    public void KnownDeviations_RegexMatchesButLikeDoesNot(string pattern, string input)
    {
        Assert.That(new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).IsMatch(input), Is.True);
        Assert.That(_db.LikeMatches(input, RegexToSqlPattern.Convert(pattern, TestOptions.Like)), Is.False);
    }

    [Test]
    public void KnownDeviations_LikeMatchesButRegexDoesNot()
    {
        Assert.That(new Regex("^.$", RegexOptions.Singleline).IsMatch("\U0001F600"), Is.False);
        Assert.That(_db.LikeMatches("\U0001F600", RegexToSqlPattern.Convert("^.$", TestOptions.Like)), Is.True);
    }
}

/// <summary>LIKE after <c>PRAGMA case_sensitive_like = ON</c>, compared with a case-sensitive regex.</summary>
public class SqliteLikeCaseSensitiveTests
{
    private static readonly RegexConversionOptions Options = new() { Format = SqlPatternFormat.Like, CaseSensitiveLike = true };

    private SqliteFixture _db = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _db = new SqliteFixture();
        _db.SetCaseSensitiveLike(true);
    }

    [OneTimeTearDown]
    public void TearDown() => _db.Dispose();

    public static IEnumerable<string> Regexes =>
        LikeRegexes.All.Concat(LikeRegexes.UppercaseLiterals)
            .Where(p => !p.Contains("[aA]") && !p.Contains("[xX]"));

    [TestCaseSource(nameof(Regexes))]
    public void MatchesCaseSensitiveRegex(string pattern)
    {
        var patterns = RegexToSqlPattern.Convert(pattern, Options);
        var regex = new Regex(pattern, RegexOptions.Singleline);

        LikeAssert.SameMatches(_db, pattern, patterns, regex, LikeAssert.WithoutFinalNewlineIfAnchored(pattern, _db.Corpus));
    }

    [Test]
    public void CaseIsRespected()
    {
        var patterns = RegexToSqlPattern.Convert("^abc$", Options);

        Assert.That(_db.LikeMatches("abc", patterns), Is.True);
        Assert.That(_db.LikeMatches("ABC", patterns), Is.False);
    }

    [Test]
    public void NonAsciiLettersAreSupported()
    {
        var patterns = RegexToSqlPattern.Convert("^café$", Options);

        Assert.That(_db.LikeMatches("café", patterns), Is.True);
        Assert.That(_db.LikeMatches("cafÉ", patterns), Is.False);
    }
}

internal static class LikeAssert
{
    /// <summary>By default '$' and '\Z' are end-of-string only, so a trailing '\n' is out of scope.</summary>
    public static IEnumerable<string> WithoutFinalNewlineIfAnchored(string pattern, IEnumerable<string> corpus)
    {
        bool hasEndAnchor = pattern.Contains('$') || pattern.Contains(@"\Z");
        return corpus.Where(s => !(hasEndAnchor && s.EndsWith("\n", StringComparison.Ordinal)));
    }

    public static void SameMatches(
        SqliteFixture db, string pattern, IReadOnlyList<string> patterns, Regex regex, IEnumerable<string> inputs)
    {
        var selected = db.SelectLike(patterns);

        var mismatches = inputs
            .Where(s => regex.IsMatch(s) != selected.Contains(s))
            .Take(10)
            .Select(s => $"  {Escape(s)}: regex={regex.IsMatch(s)}, like={selected.Contains(s)}")
            .ToList();

        Assert.That(mismatches.Count == 0, Is.True,
            $"Pattern {Escape(pattern)} => [{string.Join(" | ", patterns.Select(Escape))}] disagrees with Regex:\n" +
            string.Join("\n", mismatches));
    }

    private static string Escape(string s) =>
        string.Concat(s.Select(c => c switch
        {
            '\n' => @"\n",
            '\r' => @"\r",
            '\t' => @"\t",
            < ' ' or > '~' => $@"\u{(int)c:X4}",
            _ => c.ToString(),
        }));
}
