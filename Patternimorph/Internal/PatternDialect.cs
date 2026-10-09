namespace Patternimorph.Internal
{
    using System.Collections.Generic;

    /// <summary>
    /// How a SQL pattern language spells wildcards, literals and character sets.
    /// <see cref="RegexPatternParser"/> does the regex work and asks the dialect for the text.
    /// </summary>
    /// <remarks>
    /// Contract relied on by the parser: the one-character and any-string wildcards appear in
    /// emitted text only as wildcards; a literal '?' / '*' (GLOB) or '_' / '%' (LIKE) is always
    /// part of a longer token, such as "[?]" or "\_".
    /// </remarks>
    internal abstract class PatternDialect
    {
        /// <summary>Matches exactly one character: '?' or '_'.</summary>
        public abstract char AnyChar { get; }

        /// <summary>Matches any sequence of characters, including none: '*' or '%'.</summary>
        public abstract char AnyString { get; }

        /// <summary>Name used in error messages, e.g. "GLOB".</summary>
        public abstract string Name { get; }

        public abstract string Literal(char c);

        /// <summary>
        /// A set of characters given as sorted, merged, non-overlapping ranges that contain no
        /// surrogate code units. Throws NotSupportedException if the dialect cannot express it.
        /// </summary>
        public abstract string CharacterSet(IReadOnlyList<(char Lo, char Hi)> ranges, bool negated);

        /// <summary>Length of the token starting at p[i]: 1 for a plain character or wildcard.</summary>
        public abstract int TokenLength(string p, int i);
    }
}
