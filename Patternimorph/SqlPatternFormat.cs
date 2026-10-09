namespace Patternimorph
{
    /// <summary>The SQL pattern operator that <see cref="RegexToSqlPattern"/> produces patterns for.</summary>
    public enum SqlPatternFormat
    {
        /// <summary>
        /// <c>GLOB</c> (SQLite): case-sensitive, with '?', '*' and character classes. Supports the widest
        /// regex subset.
        /// </summary>
        Glob,

        /// <summary>
        /// <c>LIKE ... ESCAPE '\'</c>: only '_' and '%', no character classes. Case handling
        /// follows <see cref="RegexConversionOptions.CaseSensitiveLike"/>.
        /// </summary>
        Like
    }
}
