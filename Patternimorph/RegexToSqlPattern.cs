namespace Patternimorph
{
    using System;
    using System.Collections.Generic;
    using Patternimorph.Internal;

    /// <summary>
    /// Converts a subset of .NET regex into SQL LIKE or SQLite GLOB patterns, as selected by
    /// <see cref="RegexConversionOptions.Format"/>.
    /// </summary>
    public static class RegexToSqlPattern
    {
        /// <summary>
        /// The escape character used in LIKE patterns; LIKE SQL must say <c>ESCAPE '\'</c>.
        /// GLOB has no escape character.
        /// </summary>
        public const char LikeEscapeCharacter = LikeDialect.Escape;

        private const string LikeEscapeClause = " ESCAPE '\\'";

        /// <summary>
        /// Converts a subset of .NET regex into one or more SQL patterns whose OR-combination
        /// matches exactly the same strings (see <see cref="RegexConversionOptions"/> for the
        /// format and how '.', '$' and case are interpreted).
        ///
        /// Supported for GLOB:
        ///   literals, escaped metacharacters, \n \r \t \f \v \a \e \xHH \uHHHH
        ///   .   [abc]  [a-z]  [^abc]  (with \d \w \s and escapes inside classes)
        ///   \d \D \w \W \s \S           (ASCII interpretation)
        ///   [\s\S] and similar classes matching any character (treated like '.')
        ///   ?  {m}  {m,n}  and lazy forms (??, {m,n}?, .*?)
        ///   .*  .+  .{m,}  (and e.g. (.*)+, (?:.+){2,})
        ///   (...)  (?:...)  (?&lt;name&gt;...)  (?'name'...)  alternation |
        ///   ^ \A at the start and $ \Z \z at the end of each top-level branch
        ///
        /// Unsupported (no finite exact form, or not worth emulating):
        ///   x*, x+, x{m,} for anything that is not an arbitrary-character wildcard,
        ///   lookarounds, backreferences, \b, inline options, class subtraction,
        ///   anchors inside groups or mid-branch.
        ///
        /// LIKE additionally rejects:
        ///   any character class other than a single character, a class matching any character
        ///   such as [\s\S], or (case-insensitively) an ASCII case pair [aA];
        ///   '.' when <see cref="RegexConversionOptions.DotMatchesNewline"/> is false;
        ///   non-ASCII letters that have case, unless <see cref="RegexConversionOptions.CaseSensitiveLike"/>.
        ///
        /// Known caveat: regex works on UTF-16 code units, SQLite (and most databases) on code points, so '.'
        /// and negated classes differ for characters outside the BMP (e.g. emoji).
        /// </summary>
        public static bool TryConvert(
            string regex,
            out IReadOnlyList<string> patterns,
            RegexConversionOptions? options = null)
        {
            if (regex == null)
                throw new ArgumentNullException(nameof(regex));

            try
            {
                patterns = Convert(regex, options);
                return true;
            }
            catch (Exception ex) when (ex is NotSupportedException or FormatException)
            {
                patterns = Array.Empty<string>();
                return false;
            }
        }

        /// <summary>
        /// Like <see cref="TryConvert(string, out IReadOnlyList{string}, RegexConversionOptions?)"/>
        /// but throws NotSupportedException / FormatException with a reason on failure.
        /// </summary>
        public static IReadOnlyList<string> Convert(string regex, RegexConversionOptions? options = null)
        {
            if (regex == null)
                throw new ArgumentNullException(nameof(regex));

            options ??= RegexConversionOptions.Default;

            return new RegexPatternParser(regex, options, Dialect(options)).Parse();
        }

        /// <summary>
        /// Builds <c>("col" GLOB ? OR ...)</c> or <c>("col" LIKE ? ESCAPE '' OR ...)</c>. Bind
        /// <paramref name="patterns"/>, in order, as the positional parameters.
        /// </summary>
        /// <param name="column">Column name; quoted as a single identifier.</param>
        /// <param name="patterns">Patterns from <see cref="Convert"/>.</param>
        /// <param name="format">The <see cref="RegexConversionOptions.Format"/> the patterns were converted with.</param>
        public static string ToSqlPredicate(string column, IReadOnlyList<string> patterns, SqlPatternFormat format)
        {
            var (op, suffix) = Operator(format);
            return SqlPredicate.Positional(column, patterns, op, suffix);
        }

        /// <summary>
        /// Builds <c>("col" GLOB @p0 OR ...)</c> or <c>("col" LIKE @p0 ESCAPE '' OR ...)</c> with
        /// named parameters, for providers such as Microsoft.Data.Sqlite that do not support
        /// positional '?'. Bind <c>patterns[i]</c> to <paramref name="parameterPrefix"/> + i, e.g. "@p0".
        /// </summary>
        /// <param name="column">Column name; quoted as a single identifier.</param>
        /// <param name="patterns">Patterns from <see cref="Convert"/>.</param>
        /// <param name="format">The <see cref="RegexConversionOptions.Format"/> the patterns were converted with.</param>
        /// <param name="parameterPrefix">'@', '$' or ':' followed by letters, digits or '_'.</param>
        public static string ToSqlPredicate(
            string column, IReadOnlyList<string> patterns, SqlPatternFormat format, string parameterPrefix)
        {
            var (op, suffix) = Operator(format);
            return SqlPredicate.Named(column, patterns, parameterPrefix, op, suffix);
        }

        private static PatternDialect Dialect(RegexConversionOptions options) => options.Format switch
        {
            SqlPatternFormat.Glob => GlobDialect.Instance,
            SqlPatternFormat.Like => options.CaseSensitiveLike ? LikeDialect.CaseSensitive : LikeDialect.CaseInsensitive,
            _ => throw InvalidFormat(options.Format, nameof(options))
        };

        private static (string Op, string Suffix) Operator(SqlPatternFormat format) => format switch
        {
            SqlPatternFormat.Glob => ("GLOB", ""),
            SqlPatternFormat.Like => ("LIKE", LikeEscapeClause),
            _ => throw InvalidFormat(format, nameof(format))
        };

        private static ArgumentOutOfRangeException InvalidFormat(SqlPatternFormat format, string paramName)
            => new(paramName, $"Unknown pattern format {format}.");
    }
}
