using System.Text.RegularExpressions;
using SQLitePCL;

namespace Patternimorph.Tests;

/// <summary>
/// Runs the generated patterns through SQLite and checks that the OR of the GLOBs selects
/// exactly the strings the .NET regex matches.
/// </summary>
public class SqliteGlobTests
{
    private static readonly RegexConversionOptions ExactOptions = new()
    {
        DotMatchesNewline = false,
        DollarMatchesBeforeFinalNewline = true,
    };

    private SqliteFixture _db = null!;

    [OneTimeSetUp]
    public void SetUp() => _db = new SqliteFixture();

    [OneTimeTearDown]
    public void TearDown() => _db.Dispose();

    /// <summary>Compared with RegexOptions.Singleline under default options.</summary>
    public static IEnumerable<string> DefaultModeRegexes =>
    [
        "", "abc", "^abc", "abc$", "^abc$", "^$", @"\Aa", @"a\z", @"a\Z",
        "^a.c$", "a.*c", "^a.+c$", "^.{2,}$", "^.{1,3}$", "b.{2}$", "(?:.*)+", "^(?:.+){2,}$", "^a??b.*?$",
        "^(ab|cd)e?$", "^a|b$", "a|", "^(a|b|)(c|)$", "^x{3}$", "^(?:a|b){1,3}$", "^(?<n>ab)?c",
        "^[a-c]x$", "^[^a-c]$", "[a-c][x-z]", "[]]", "^[]a]$", "^[^]]$", "^[-^]$", "^[^^]$", "^[^-]$",
        "^[]^-]$", @"^[\^a]$", @"^[a\-z]$", "^[!-~]$", @"^[\d-z]$", @"^[\s\]]?$", @"^[\w-]{2}$", "[*?[]",
        @"\d\w\s", @"^\D\W?\S$", @"a\*b", @"a\?b", @"\[x", @"\\", @"x\x41B", "^.*[?*].*$",
        "a{", "^a{,2}$", @"^\{\}$", @"^[\n\r]", @"\t.\n", @"^a[\s\S]c$", @"[\w\W]{2}", "%", "^a_b$",
    ];

    /// <summary>Compared with RegexOptions.None under options that reproduce .NET's '.' and '$'.</summary>
    public static IEnumerable<string> ExactModeRegexes =>
    [
        "abc", "^abc$", "abc$", "^$", "$", "^a.c$", "a.c", "^.{2}$", "^.{1,3}$", "^a.?$", "a|b$",
        @"a\Z", @"a\z", @"^\Z", "^[^a]$", "^(ab|cd)$", @"^\n$", "c$|^a", @"^[\s]$",
    ];

    [TestCaseSource(nameof(DefaultModeRegexes))]
    public void DefaultModeMatchesSinglelineRegex(string pattern)
    {
        var globs = RegexToSqlPattern.Convert(pattern);
        var regex = new Regex(pattern, RegexOptions.Singleline);

        // By default '$' and '\Z' are end-of-string only, so a trailing '\n' is out of scope here;
        // ExactModeMatchesDotNetRegex covers it.
        bool hasEndAnchor = pattern.Contains('$') || pattern.Contains(@"\Z");
        var inScope = _db.Corpus.Where(s => !(hasEndAnchor && s.EndsWith("\n", StringComparison.Ordinal)));

        AssertSameMatches(pattern, globs, regex, inScope);
    }

    [TestCaseSource(nameof(ExactModeRegexes))]
    public void ExactModeMatchesDotNetRegex(string pattern)
    {
        var globs = RegexToSqlPattern.Convert(pattern, ExactOptions);
        var regex = new Regex(pattern, RegexOptions.None);

        AssertSameMatches(pattern, globs, regex, _db.Corpus);
    }

    /// <summary>
    /// FullMatch is compared with \A(?:regex)\z. The whole corpus is in scope in both modes:
    /// '$' can never match before a final '\n' when \z must follow it.
    /// </summary>
    [TestCaseSource(nameof(DefaultModeRegexes))]
    [TestCaseSource(nameof(ExactModeRegexes))]
    public void FullMatchMatchesWholeStringRegex(string pattern)
    {
        string whole = @"\A(?:" + pattern + @")\z";

        var globs = RegexToSqlPattern.Convert(pattern, new RegexConversionOptions { FullMatch = true });
        AssertSameMatches(pattern, globs, new Regex(whole, RegexOptions.Singleline), _db.Corpus);

        if (RegexToSqlPattern.TryConvert(pattern, out var exact, new RegexConversionOptions
            {
                FullMatch = true,
                DotMatchesNewline = false,
                DollarMatchesBeforeFinalNewline = true,
            }))
        {
            AssertSameMatches(pattern, exact, new Regex(whole, RegexOptions.None), _db.Corpus);
        }
    }

    [Test]
    public void CorpusIsLargeAndDiscriminating()
    {
        Assert.That(_db.Corpus.Count, Is.GreaterThan(2000));

        // Each shape of pattern selects some rows but not all, so the comparisons are meaningful.
        foreach (string pattern in new[] { "^a.c$", "[]]", @"\d\w\s", "^(?:.+){2,}$" })
        {
            int count = _db.SelectMatching(RegexToSqlPattern.Convert(pattern)).Count;
            Assert.That(count, Is.InRange(1, _db.Corpus.Count - 1));
        }
    }

    [Test]
    public void EndToEndFilterExample()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "CREATE TEMP TABLE items (name TEXT); " +
                          "INSERT INTO items VALUES ('ab12'), ('cd34'), ('ab1'), ('xab12'), ('ef56'), (NULL)";
        cmd.ExecuteNonQuery();

        var globs = RegexToSqlPattern.Convert(@"^(ab|cd)\d{2}$");
        cmd.CommandText = "SELECT name FROM items WHERE " +
                          RegexToSqlPattern.ToSqlPredicate("name", globs, SqlPatternFormat.Glob, "@g") + " ORDER BY name";
        for (int i = 0; i < globs.Count; i++)
            cmd.Parameters.AddWithValue("@g" + i, globs[i]);

        var names = new List<string>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                names.Add(reader.GetString(0));

        Assert.That(names, Is.EqualTo(new[] { "ab12", "cd34" }));
    }

    [Test]
    public void PositionalPredicateWorksWithRawSqlite()
    {
        // Microsoft.Data.Sqlite cannot bind bare '?', so bind by index through the native API.
        var globs = RegexToSqlPattern.Convert("^(a|b)c$");
        string sql = "SELECT " + RegexToSqlPattern.ToSqlPredicate("s", globs, SqlPatternFormat.Glob) + " FROM (SELECT ?3 AS s)";

        foreach (var (input, expected) in new[] { ("ac", 1), ("bc", 1), ("cc", 0), ("acx", 0) })
        {
            Assert.That(raw.sqlite3_prepare_v2(_db.Connection.Handle, sql, out sqlite3_stmt stmt), Is.EqualTo(raw.SQLITE_OK));
            using (stmt)
            {
                raw.sqlite3_bind_text(stmt, 1, globs[0]);
                raw.sqlite3_bind_text(stmt, 2, globs[1]);
                raw.sqlite3_bind_text(stmt, 3, input);

                Assert.That(raw.sqlite3_step(stmt), Is.EqualTo(raw.SQLITE_ROW));
                Assert.That(raw.sqlite3_column_int(stmt, 0), Is.EqualTo(expected));
            }
        }
    }

    [Test]
    public void NullColumnNeverMatches() =>
        Assert.That(MatchesNull(RegexToSqlPattern.Convert("")), Is.False);

    /// <summary>
    /// Documented differences between .NET regex and the converted GLOBs. If one of these starts
    /// failing, the behavior changed and the README's "Semantic caveats" section needs updating.
    /// </summary>
    [TestCase(@"^\d$", "٣")]            // ARABIC-INDIC DIGIT THREE: Unicode digit in .NET
    [TestCase(@"^\w$", "é")]                 // Unicode letter in .NET
    [TestCase(@"^\s$", " ")]           // NO-BREAK SPACE: Unicode whitespace in .NET
    [TestCase("^..$", "\U0001F600")]         // regex sees 2 UTF-16 units, GLOB sees 1 code point
    [TestCase("^abc$", "abc\n")]             // '$' before a final newline is opt-in
    public void KnownDeviations_RegexMatchesButGlobDoesNot(string pattern, string input)
    {
        Assert.That(new Regex(pattern, RegexOptions.Singleline).IsMatch(input), Is.True);
        Assert.That(_db.Matches(input, RegexToSqlPattern.Convert(pattern)), Is.False);
    }

    [TestCase("^.$", "\U0001F600")]          // GLOB '?' consumes a whole surrogate pair
    [TestCase(@"^[^a]$", "\U0001F600")]
    public void KnownDeviations_GlobMatchesButRegexDoesNot(string pattern, string input)
    {
        Assert.That(new Regex(pattern, RegexOptions.Singleline).IsMatch(input), Is.False);
        Assert.That(_db.Matches(input, RegexToSqlPattern.Convert(pattern)), Is.True);
    }

    [Test]
    public void NonBmpLiteralMatches()
    {
        var globs = RegexToSqlPattern.Convert("^a\U0001F600b$");

        Assert.That(_db.Matches("a\U0001F600b", globs), Is.True);
        Assert.That(_db.Matches("a\U0001F601b", globs), Is.False);
    }

    private bool MatchesNull(IReadOnlyList<string> globs)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM (SELECT NULL AS s) WHERE " +
                          RegexToSqlPattern.ToSqlPredicate("s", globs, SqlPatternFormat.Glob, "@g");
        for (int i = 0; i < globs.Count; i++)
            cmd.Parameters.AddWithValue("@g" + i, globs[i]);

        return (long)cmd.ExecuteScalar()! > 0;
    }

    private void AssertSameMatches(string pattern, IReadOnlyList<string> globs, Regex regex, IEnumerable<string> inputs)
    {
        var selected = _db.SelectMatching(globs);

        var mismatches = inputs
            .Where(s => regex.IsMatch(s) != selected.Contains(s))
            .Take(10)
            .Select(s => $"  {Escape(s)}: regex={regex.IsMatch(s)}, glob={selected.Contains(s)}")
            .ToList();

        Assert.That(mismatches.Count == 0, Is.True,
            $"Pattern {Escape(pattern)} => [{string.Join(" | ", globs.Select(Escape))}] disagrees with Regex:\n" +
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
