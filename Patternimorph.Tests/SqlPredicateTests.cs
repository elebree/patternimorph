namespace Patternimorph.Tests;

public class SqlPredicateTests
{
    [Test]
    public void PositionalSingle() =>
        Assert.That(RegexToSqlPattern.ToSqlPredicate("name", new[] { "a*" }, SqlPatternFormat.Glob), Is.EqualTo("(\"name\" GLOB ?)"));

    [Test]
    public void PositionalMultiple() =>
        Assert.That(
            RegexToSqlPattern.ToSqlPredicate("name", new[] { "a", "b", "c" }, SqlPatternFormat.Glob),
            Is.EqualTo("(\"name\" GLOB ? OR \"name\" GLOB ? OR \"name\" GLOB ?)"));

    [Test]
    public void NamedMultiple() =>
        Assert.That(
            RegexToSqlPattern.ToSqlPredicate("name", new[] { "a", "b" }, SqlPatternFormat.Glob, "@g"),
            Is.EqualTo("(\"name\" GLOB @g0 OR \"name\" GLOB @g1)"));

    [TestCase("@p")]
    [TestCase("$p")]
    [TestCase(":p")]
    [TestCase("@glob_1")]
    public void NamedAcceptsSqlitePrefixes(string prefix) =>
        Assert.That(
            RegexToSqlPattern.ToSqlPredicate("c", new[] { "x" }, SqlPatternFormat.Glob, prefix),
            Is.EqualTo($"(\"c\" GLOB {prefix}0)"));

    [TestCase("")]
    [TestCase("@")]
    [TestCase("p")]
    [TestCase("?p")]
    [TestCase("@p-")]
    [TestCase("@p q")]
    [TestCase("@p;DROP")]
    public void NamedRejectsInvalidPrefix(string prefix) =>
        Assert.Throws<ArgumentException>(() => RegexToSqlPattern.ToSqlPredicate("c", new[] { "x" }, SqlPatternFormat.Glob, prefix));

    [Test]
    public void EmptyGlobListIsFalse()
    {
        Assert.That(RegexToSqlPattern.ToSqlPredicate("name", Array.Empty<string>(), SqlPatternFormat.Glob), Is.EqualTo("(1 = 0)"));
        Assert.That(RegexToSqlPattern.ToSqlPredicate("name", Array.Empty<string>(), SqlPatternFormat.Glob, "@g"), Is.EqualTo("(1 = 0)"));
    }

    [TestCase("we\"ird", "\"we\"\"ird\"")]
    [TestCase("with space", "\"with space\"")]
    [TestCase("t.col", "\"t.col\"")]
    [TestCase("", "\"\"")]
    public void ColumnIsQuotedAsSingleIdentifier(string column, string quoted)
    {
        Assert.That(RegexToSqlPattern.ToSqlPredicate(column, new[] { "x" }, SqlPatternFormat.Glob), Is.EqualTo($"({quoted} GLOB ?)"));
        Assert.That(RegexToSqlPattern.ToSqlPredicate(column, new[] { "x" }, SqlPatternFormat.Glob, "@g"), Is.EqualTo($"({quoted} GLOB @g0)"));
    }

    [Test]
    public void NullArgumentsThrow()
    {
        var globs = new[] { "x" };

        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate(null!, globs, SqlPatternFormat.Glob));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate("c", null!, SqlPatternFormat.Glob));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate(null!, globs, SqlPatternFormat.Glob, "@g"));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate("c", null!, SqlPatternFormat.Glob, "@g"));
        Assert.Throws<ArgumentNullException>(() => RegexToSqlPattern.ToSqlPredicate("c", globs, SqlPatternFormat.Glob, null!));
    }
}
