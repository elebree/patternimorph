namespace Patternimorph
{
    /// <summary>Options controlling how regex semantics are mapped onto a SQL pattern format (SQLite GLOB or SQL LIKE).</summary>
    public sealed class RegexConversionOptions
    {
        public static readonly RegexConversionOptions Default = new();

        /// <summary>The pattern format to produce: <see cref="SqlPatternFormat.Glob"/> (default) or <see cref="SqlPatternFormat.Like"/>.</summary>
        public SqlPatternFormat Format { get; init; } = SqlPatternFormat.Glob;

        /// <summary>Upper bound on the number of patterns produced.</summary>
        public int MaxPatterns { get; init; } = 256;

        /// <summary>Upper bound on the length of any single pattern (guards against a{1000000}).</summary>
        public int MaxPatternLength { get; init; } = 4096;

        /// <summary>
        /// false (default): the patterns match wherever the regex matches, like Regex.IsMatch;
        ///                  an unanchored branch gets leading/trailing any-string wildcards.
        /// true:            the regex must match the whole string, as if written \A(?:regex)\z.
        ///                  '^', '\A', '$', '\Z' and '\z' at branch edges are then redundant, and
        ///                  <see cref="DollarMatchesBeforeFinalNewline"/> has no effect.
        /// </summary>
        public bool FullMatch { get; init; } = false;

        /// <summary>
        /// true  (default): '.' matches any character, as with RegexOptions.Singleline.
        ///                  '.' => one-character wildcard, '.*' => any-string wildcard.
        /// false:           '.' excludes '\n', exactly like default .NET semantics
        ///                  (GLOB: [^\n]; LIKE cannot express it and rejects '.').
        ///                  '.*' and '.+' are then not representable and are rejected.
        /// </summary>
        public bool DotMatchesNewline { get; init; } = true;

        /// <summary>
        /// false (default): '$' and '\Z' are treated like '\z' (end of string only).
        /// true:            exact .NET semantics: '$' also matches before a final '\n',
        ///                  so each such branch yields an extra pattern ending in '\n'.
        /// </summary>
        public bool DollarMatchesBeforeFinalNewline { get; init; } = false;

        /// <summary>
        /// Only used with <see cref="SqlPatternFormat.Like"/>; GLOB is always case-sensitive.
        /// false (default): LIKE ignores case for ASCII letters only (SQLite's default LIKE).
        ///                  The patterns match what the regex matches with RegexOptions.IgnoreCase,
        ///                  so "abc" also matches "ABC". Letters outside ASCII that have case
        ///                  (é, Ж, ...) are rejected, because LIKE would compare them case-sensitively.
        ///                  Classes that differ only in ASCII case collapse: [aA] => a.
        /// true:            LIKE compares case-sensitively (e.g. PostgreSQL, or SQLite after
        ///                  PRAGMA case_sensitive_like = ON); the patterns match exactly what
        ///                  the case-sensitive regex matches.
        /// </summary>
        public bool CaseSensitiveLike { get; init; } = false;
    }
}
