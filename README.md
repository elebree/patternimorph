# Patternimorph

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/elebree/patternimorph/blob/HEAD/LICENSE)

Converts a subset of .NET regular expressions into SQL `LIKE` patterns or SQLite `GLOB` patterns (GLOB supports a wider subset). One class, `RegexToSqlPattern`, does both; the output format is chosen with `RegexConversionOptions.Format`. A regex filter can then run inside the database (and use its engine) instead of pulling every row into .NET.

A regex usually can't be expressed as one pattern, so the converter returns a **list** of patterns. Their OR-combination matches exactly the same strings as the regex. When there's no exact equivalent, conversion fails; you never get an approximation.

- Target: **.NET Standard 2.0** (works on .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+, Mono, Unity)
- Dependencies: none

## Install

```sh
dotnet add package Patternimorph
```

## Quick start

```csharp
using Patternimorph;

if (RegexToSqlPattern.TryConvert(@"^(ab|cd)\d{2}$", out var globs))
{
    // globs: ["ab[0-9][0-9]", "cd[0-9][0-9]"]
    string where = RegexToSqlPattern.ToSqlPredicate("name", globs, SqlPatternFormat.Glob, "@g");
    // where: ("name" GLOB @g0 OR "name" GLOB @g1)

    using var cmd = connection.CreateCommand();   // Microsoft.Data.Sqlite
    cmd.CommandText = $"SELECT * FROM items WHERE {where}";
    for (int i = 0; i < globs.Count; i++)
        cmd.Parameters.AddWithValue("@g" + i, globs[i]);
}
else
{
    // Not representable: fall back to filtering in .NET with Regex.
}
```

To get the reason for a failure, use `Convert`. It throws `NotSupportedException` (valid regex, no GLOB equivalent) or `FormatException` (malformed regex) with a message:

```csharp
IReadOnlyList<string> globs = RegexToSqlPattern.Convert("a.*b");   // ["*a*b*"]
RegexToSqlPattern.Convert("a+b");   // NotSupportedException: Unbounded repetition is only representable ...
```

## API

| Member | Description |
|---|---|
| `bool TryConvert(string regex, out IReadOnlyList<string> patterns, RegexConversionOptions? options = null)` | Returns `false` (and an empty list) if the regex is unsupported or malformed. |
| `IReadOnlyList<string> Convert(string regex, RegexConversionOptions? options = null)` | Same as `TryConvert`, but throws with a reason. |
| `string ToSqlPredicate(string column, IReadOnlyList<string> patterns, SqlPatternFormat format, string parameterPrefix)` | Builds `("col" GLOB @p0 OR "col" GLOB @p1 ...)` with named parameters (LIKE: `"col" LIKE @p0 ESCAPE '\'`). Bind `patterns[i]` to `parameterPrefix + i`. The prefix is `@`, `$` or `:` followed by ASCII letters, digits or `_`. Use this overload with **Microsoft.Data.Sqlite**, which doesn't support bare `?`. |
| `string ToSqlPredicate(string column, IReadOnlyList<string> patterns, SqlPatternFormat format)` | Builds `("col" GLOB ? OR ...)` with positional parameters, for providers that bind `?` by position (System.Data.SQLite, the native API). |

`format` is required, and must be the `Format` the patterns were converted with (`options.Format`); it picks `GLOB` or `LIKE ... ESCAPE ''`. Both overloads quote `column` as a single identifier: `t.col` becomes `"t.col"`, not a table-qualified name. Both return `(1 = 0)` (false) for an empty list. A `NULL` column value never matches.

A `null` regex throws `ArgumentNullException`. Invalid options (`MaxPatterns < 1`, `MaxPatternLength < 1`, or an undefined `Format`) throw `ArgumentOutOfRangeException`, even from `TryConvert`.

### `RegexConversionOptions`

Immutable (`init`-only). `RegexConversionOptions.Default` (GLOB) is shared.

| Option | Default | Meaning |
|---|---|---|
| `Format` | `Glob` | `SqlPatternFormat.Glob` or `SqlPatternFormat.Like`. GLOB is SQLite-only; LIKE works with other databases too. See [LIKE conversion](#like-conversion). |
| `MaxPatterns` | `256` | Upper limit on the number of patterns produced. Alternations and bounded repeats multiply (for example, `(a\|b){20}` would need 2²⁰ patterns). |
| `MaxPatternLength` | `4096` | Upper limit on the length of any single pattern (guards against `a{1000000}`). |
| `FullMatch` | `false` | `false`: patterns match wherever the regex matches, like `Regex.IsMatch`. `true`: the regex must match the whole string, as if written `\A(?:regex)\z`, so `\d{4}` becomes `[0-9][0-9][0-9][0-9]` instead of `*[0-9][0-9][0-9][0-9]*`. Edge anchors (`^`, `$`, ...) are then redundant, and `DollarMatchesBeforeFinalNewline` has no effect. |
| `DotMatchesNewline` | `true` | `true`: `.` matches any character, as with `RegexOptions.Singleline`. `false`: `.` means `[^\n]`, as in default .NET, but then `.*` and `.+` can't be represented. |
| `DollarMatchesBeforeFinalNewline` | `false` | `false`: `$` and `\Z` mean end of string only. `true`: exact .NET semantics, where `$` also matches before a trailing `\n`, at the cost of one extra pattern per branch. |
| `CaseSensitiveLike` | `false` | LIKE only; see [Case sensitivity](#case-sensitivity). GLOB is always case-sensitive. |

For the closest match to plain `new Regex(pattern)` semantics, use:

```csharp
var exact = new RegexConversionOptions { DotMatchesNewline = false, DollarMatchesBeforeFinalNewline = true };
```

## Supported syntax

| Construct | Example | GLOB output |
|---|---|---|
| Literals, escaped metacharacters | `a\*b` | `*a[*]b*` |
| Character escapes | `\n \r \t \f \v \a \e \xHH \uHHHH` | literal character |
| Any character | `^a.c$`, `^a[\s\S]c$` | `a?c` |
| Character classes | `[a-z]`, `[^abc]`, `[\d_]` | `[a-z]`, `[^a-c]`, `[0-9_]` |
| Shorthands (ASCII) | `\d \D \w \W \s \S` | `[0-9]`, `[^0-9]`, ... |
| Optional and bounded repeats | `x?`, `x{3}`, `x{2,4}`, lazy forms | expanded into several patterns |
| Unbounded wildcards | `.*`, `.+`, `.{3,}`, `(?:.+)+` | `*`, `?*`, `???*` |
| Groups | `(...)`, `(?:...)`, `(?<name>...)`, `(?'name'...)` | expanded |
| Alternation | `a\|b` | one pattern per branch |
| Anchors | `^` / `\A` at the start and `$` / `\Z` / `\z` at the end of a top-level branch | unanchored ends get `*` |

Unanchored regexes match anywhere, the same as `Regex.IsMatch`: `abc` becomes `*abc*`. With `FullMatch = true` it becomes `abc`.

## Unsupported syntax

These have no exact finite GLOB form, or aren't worth emulating:

- `x*`, `x+`, `x{m,}` where `x` is anything other than an any-character wildcard (for example `a+`, `[0-9]*`, `(ab)*`)
- Lookarounds, backreferences, `\b`, `\B`, `\G`, `\p{...}`, atomic groups `(?>...)`, conditionals, balancing groups
- Inline options such as `(?i)` and comments `(?#...)`
- Character class subtraction `[a-z-[aeiou]]`, and negated shorthands inside classes (`[\D]`) unless the class matches any character (`[\s\S]`)
- Anchors inside groups or mid-branch (`a(^b)`, `a$b`)

`RegexOptions` aren't accepted. The regex is interpreted as case-sensitive (which `GLOB` also is), with `IgnorePatternWhitespace` off.

## Semantic caveats

- **`\d`, `\w`, `\s` are ASCII-only.** In .NET they match Unicode digits, letters and whitespace (for example `٣` or ` `) unless you use `RegexOptions.ECMAScript`. The converted patterns match only `[0-9]`, `[0-9A-Za-z_]` and `[\t-\r ]`.
- **Characters outside the BMP.** Regex works on UTF-16 code units and GLOB on code points, so `.` and negated classes treat surrogate pairs (for example emoji) differently. Surrogate code units inside character classes are rejected.
- **The output isn't minimal.** Every pattern is exact, but patterns can overlap. For example, unanchored `.{2,4}` produces `??*`, `???*` and `????*`, where `??*` alone would do.

## LIKE conversion

Set `Format = SqlPatternFormat.Like` and use the same `RegexToSqlPattern` methods. LIKE only has `_` (one character) and `%` (any string), so it supports the GLOB subset **without character classes**.

```csharp
var like = new RegexConversionOptions { Format = SqlPatternFormat.Like };

var patterns = RegexToSqlPattern.Convert(@"\.(jpg|png)$", like);   // ["%.jpg", "%.png"]
string where = RegexToSqlPattern.ToSqlPredicate("name", patterns, like.Format, "@p");
// ("name" LIKE @p0 ESCAPE '\' OR "name" LIKE @p1 ESCAPE '\')

RegexToSqlPattern.Convert("^100%_done$", like);    // ["100\%\_done"]
RegexToSqlPattern.Convert("^[0-9]$", like);        // NotSupportedException: LIKE has no character classes ...
```

The patterns always use `\` as the escape character (`RegexToSqlPattern.LikeEscapeCharacter`), so a literal `%`, `_` or `\` is written as `\%`, `\_` or `\\`. If you write the SQL yourself, it **must** include `ESCAPE '\'`. The `ToSqlPredicate` overloads add it.

| Supported | Example | LIKE output |
|---|---|---|
| Literals and escapes | `^a\.b$`, `^100%$` | `a.b`, `100\%` |
| Any character | `.`, `[\s\S]`, `[\d\D]` | `_` |
| Unbounded wildcards | `.*`, `.+`, `.{3,}` | `%`, `_%`, `___%` |
| Single-character classes | `[a]`, `[%]` | `a`, `\%` |
| ASCII case pairs (case-insensitive only) | `[aA]` | `a` |
| Optional, bounded repeats, groups, alternation, anchors | `^colou?r$` | `color`, `colour` |

Everything else in a class is rejected: `[abc]`, `[a-z]`, `[^a]`, `\d`, `\w`, `\s` and their negations. So are `.` when `DotMatchesNewline = false` (LIKE can't express `[^\n]`) and everything GLOB rejects.

### Case sensitivity

How `LIKE` compares case depends on the database. `RegexConversionOptions.CaseSensitiveLike` says which behavior yours has:

| `CaseSensitiveLike` | Connection | Patterns match what this regex matches |
|---|---|---|
| `false` (default) | ignores case for ASCII letters only (SQLite default) | `new Regex(pattern, RegexOptions.IgnoreCase \| RegexOptions.CultureInvariant)`. So `abc` also matches `ABC`. Non-ASCII letters that have case (`é`, `Ж`, `ß`, ...) are rejected, because LIKE would compare them case-sensitively. |
| `true` | case-sensitive (PostgreSQL; SQLite after `PRAGMA case_sensitive_like = ON`) | `new Regex(pattern)`, case-sensitive. Any literal is allowed; `[aA]` is rejected. |

On a default SQLite connection, use the GLOB format for a case-sensitive match.

LIKE caveats, on top of the GLOB ones that apply (BMP, non-minimal output):

- **The Kelvin sign.** .NET's `IgnoreCase` treats `k`/`K` as equal to `K` (U+212A); LIKE doesn't. A check of all 65,536 UTF-16 code units found no other ASCII letter with such a non-ASCII case equivalent.
- **Unicode case folding.** If LIKE also folds non-ASCII case (SQLite with ICU, case-insensitive collations elsewhere), `CaseSensitiveLike = false` still gives correct results; the non-ASCII rejections are just stricter than needed.
- **Accent- or width-insensitive collations** (for example MySQL `*_ai_ci`, SQL Server `*_AI`) make LIKE match more than the regex. They aren't supported.
- **The pragma is deprecated.** `case_sensitive_like` is deprecated in recent SQLite versions and may be missing from builds compiled with `SQLITE_OMIT_DEPRECATED`.

### Other databases

The LIKE patterns and the `ESCAPE '\'` predicate are standard SQL, but check your database for these differences:

- **SQL Server** (and Sybase) treat `[...]` inside LIKE as a character class, so a pattern containing a literal `[` won't match correctly.
- **MySQL and MariaDB** treat `\` as an escape inside string literals, so the generated `ESCAPE '\'` is a syntax error unless `NO_BACKSLASH_ESCAPES` is set. `\` is already the default LIKE escape there, so you can drop the clause and bind the same patterns. MySQL also reads `"col"` as a string unless `ANSI_QUOTES` is set, so the generated predicate needs both adjustments there.
- **Parameters.** Use the named `ToSqlPredicate` overload with a prefix your provider accepts (`@` for SQL Server, Npgsql and MySQL; `:` for Oracle). The positional `?` overload suits ODBC-style providers.

The test suite only runs the patterns against SQLite.

## Building and testing

```sh
dotnet build Patternimorph.sln -c Release
dotnet test
```

The `Patternimorph.Tests` project (xUnit, net9.0) covers:

- **Conversion:** exact GLOB and LIKE output for each supported construct, the options, and the SQL predicate builders.
- **Rejection:** unsupported regexes throw `NotSupportedException`, and each one is checked to be a valid .NET regex. Malformed ones throw `FormatException`, and each one is checked to be rejected by .NET too.
- **SQLite:** each regex's patterns run as a real `WHERE` clause over about 3,000 strings in an in-memory Microsoft.Data.Sqlite database. The selected rows must be exactly the strings `Regex.IsMatch` accepts. This runs in both default mode (against `RegexOptions.Singleline`) and exact mode (against plain `RegexOptions.None`). `FullMatch` is checked against the same regex wrapped in `\A(?:...)\z`. LIKE runs on a default connection (against `IgnoreCase`) and with `PRAGMA case_sensitive_like = ON` (against a case-sensitive regex). The known caveats above are pinned as tests too, so any behavior change shows up.

Both formats share one regex parser (`Internal/RegexPatternParser.cs`). A small `PatternDialect` (`GlobDialect`, `LikeDialect`) decides how wildcards, literals and character sets are written.

The project uses C# `latest` language features on `netstandard2.0`. `Polyfills/IsExternalInit.cs` provides the one missing runtime type, needed for `init` accessors.

## License

[MIT](https://github.com/elebree/patternimorph/blob/HEAD/LICENSE)
