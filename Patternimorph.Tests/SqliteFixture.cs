using Microsoft.Data.Sqlite;

namespace Patternimorph.Tests;

/// <summary>
/// An in-memory SQLite database with a <c>corpus(id, s)</c> table of test strings, used to check
/// generated GLOB patterns against SQLite's real GLOB implementation.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    // ASCII only: \d \w \s are converted with ASCII semantics (see KnownDeviations).
    private const string Alphabet = "abcxyzABCX09_%-^]*?[\\ \t\n\r!~{}";

    private static readonly string[] HandPicked =
    {
        "", "a", "b", "c", "x", "ab", "abc", "abcx", "xabc", "xabcx", "abc\n", "abc\r\n", "abc\nabc",
        "a\nc", "a\rc", "ac", "aac", "abbc", "ae", "abe", "cde", "cdee", "aa", "aaa", "aaaa",
        "]", "^", "-", "[", "*", "?", "\\", "{", "}", "]a", "a*b", "a?b", "a[b", "[x", "x[",
        "AB", "xAB", "0", "9", "_", " ", "\t", "\n", "\r", "\f", "\v", "\b", "\a", "\u001B",
        "1a ", "z", "zz", "zzz", "a{", "a{,2}", "{}", "ac\n", "c\n", "\n\n", "a\n", "b\n",
        "A", "ABC", "Abc", "aBc", "ABC\n", "%", "_", "a%b", "a_b", "axb", "100%", "1000", "a\\b", "\\%",
        "color", "colour", "COLOR", "x.jpg", "x.png", "x.JPG", "x.gif", "xjpg",
    };

    public SqliteConnection Connection { get; }

    /// <summary>Distinct test strings; row id is index + 1.</summary>
    public IReadOnlyList<string> Corpus { get; }

    public SqliteFixture()
    {
        var random = new Random(20260924);
        var corpus = new List<string>(HandPicked);

        for (int i = 0; i < 3000; i++)
        {
            var chars = new char[random.Next(0, 7)];
            for (int j = 0; j < chars.Length; j++)
                chars[j] = Alphabet[random.Next(Alphabet.Length)];
            corpus.Add(new string(chars));
        }

        Corpus = corpus.Distinct(StringComparer.Ordinal).ToList();

        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        using (var create = Connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE corpus (id INTEGER PRIMARY KEY, s TEXT NOT NULL)";
            create.ExecuteNonQuery();
        }

        using var tx = Connection.BeginTransaction();
        using var insert = Connection.CreateCommand();
        insert.CommandText = "INSERT INTO corpus (id, s) VALUES (@id, @s)";
        var id = insert.Parameters.Add("@id", SqliteType.Integer);
        var s = insert.Parameters.Add("@s", SqliteType.Text);

        for (int i = 0; i < Corpus.Count; i++)
        {
            id.Value = i + 1;
            s.Value = Corpus[i];
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Corpus strings selected by <c>WHERE s GLOB g0 OR s GLOB g1 ...</c>.</summary>
    public HashSet<string> SelectMatching(IReadOnlyList<string> globs) =>
        SelectWhere(RegexToSqlPattern.ToSqlPredicate("s", globs, SqlPatternFormat.Glob, "@g"), globs);

    /// <summary>Corpus strings selected by <c>WHERE s LIKE g0 ESCAPE '\' OR ...</c>.</summary>
    public HashSet<string> SelectLike(IReadOnlyList<string> patterns) =>
        SelectWhere(RegexToSqlPattern.ToSqlPredicate("s", patterns, SqlPatternFormat.Like, "@g"), patterns);

    /// <summary>Evaluates the GLOB predicate for a single value that is not in the corpus.</summary>
    public bool Matches(string input, IReadOnlyList<string> globs) =>
        Evaluate(input, RegexToSqlPattern.ToSqlPredicate("s", globs, SqlPatternFormat.Glob, "@g"), globs);

    /// <summary>Evaluates the LIKE predicate for a single value that is not in the corpus.</summary>
    public bool LikeMatches(string input, IReadOnlyList<string> patterns) =>
        Evaluate(input, RegexToSqlPattern.ToSqlPredicate("s", patterns, SqlPatternFormat.Like, "@g"), patterns);

    /// <summary>Switches this connection's LIKE between SQLite's default (ASCII case-insensitive) and case-sensitive.</summary>
    public void SetCaseSensitiveLike(bool on)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "PRAGMA case_sensitive_like = " + (on ? "ON" : "OFF");
        cmd.ExecuteNonQuery();
    }

    private HashSet<string> SelectWhere(string predicate, IReadOnlyList<string> patterns)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM corpus WHERE " + predicate;
        AddPatternParameters(cmd, patterns);

        var result = new HashSet<string>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(Corpus[(int)reader.GetInt64(0) - 1]);

        return result;
    }

    private bool Evaluate(string input, string predicate, IReadOnlyList<string> patterns)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT " + predicate + " FROM (SELECT @s AS s)";
        cmd.Parameters.AddWithValue("@s", input);
        AddPatternParameters(cmd, patterns);

        return (long)cmd.ExecuteScalar()! == 1;
    }

    private static void AddPatternParameters(SqliteCommand cmd, IReadOnlyList<string> patterns)
    {
        for (int i = 0; i < patterns.Count; i++)
            cmd.Parameters.AddWithValue("@g" + i, patterns[i]);
    }

    public void Dispose() => Connection.Dispose();
}
