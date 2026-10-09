namespace Patternimorph.Internal
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// SQL LIKE with <c>ESCAPE '\'</c>. LIKE has no character classes, so a set is only
    /// expressible when it is a single character (or, case-insensitively, one ASCII letter pair).
    /// </summary>
    internal sealed class LikeDialect : PatternDialect
    {
        public const char Escape = '\\';

        public static readonly LikeDialect CaseInsensitive = new(caseSensitive: false);
        public static readonly LikeDialect CaseSensitive = new(caseSensitive: true);

        private readonly bool _caseSensitive;

        private LikeDialect(bool caseSensitive) => _caseSensitive = caseSensitive;

        public override char AnyChar => '_';
        public override char AnyString => '%';
        public override string Name => "LIKE";

        public override string Literal(char c)
        {
            // SQLite's built-in LIKE folds only ASCII; a regex compared case-insensitively
            // would also fold these, so they have no exact LIKE form.
            if (!_caseSensitive && c > '\u007F' && HasCase(c))
                throw new NotSupportedException(
                    $"'{c}' has case variants that case-insensitive LIKE does not fold (it folds ASCII only).");

            return c is '%' or '_' or Escape
                ? new string(new[] { Escape, c })
                : c.ToString();
        }

        public override string CharacterSet(IReadOnlyList<(char Lo, char Hi)> ranges, bool negated)
        {
            if (!negated)
            {
                if (ranges.Count == 1 && ranges[0].Lo == ranges[0].Hi)
                    return Literal(ranges[0].Lo);

                // [aA] is a single character under ASCII case folding.
                if (!_caseSensitive && ranges.Count <= 2 && CountChars(ranges) == 2)
                {
                    char a = ranges[0].Lo;
                    char b = ranges[ranges.Count - 1].Hi;

                    if (IsAsciiLetter(a) && FoldAscii(a) == FoldAscii(b))
                        return Literal(FoldAscii(a));
                }
            }

            throw new NotSupportedException(
                "LIKE has no character classes; only single characters" +
                (_caseSensitive ? "" : " and ASCII case pairs such as [aA]") + " are supported.");
        }

        public override int TokenLength(string p, int i) => p[i] == Escape ? 2 : 1;

        private static int CountChars(IReadOnlyList<(char Lo, char Hi)> ranges)
        {
            int count = 0;
            foreach (var (lo, hi) in ranges)
                count += hi - lo + 1;
            return count;
        }

        private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

        private static char FoldAscii(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;

        /// <summary>
        /// True if c has a case mapping, or is the case mapping of another character. The second
        /// half catches characters like 'ß', which has no single-char uppercase but is the
        /// lowercase of 'ẞ', so RegexOptions.IgnoreCase folds the two.
        /// </summary>
        private static bool HasCase(char c) =>
            char.ToUpperInvariant(c) != c || char.ToLowerInvariant(c) != c || CaseMappingTargets.Value.Contains(c);

        private static readonly Lazy<HashSet<char>> CaseMappingTargets = new(() =>
        {
            var targets = new HashSet<char>();

            for (int i = 0; i <= char.MaxValue; i++)
            {
                char c = (char)i;
                char upper = char.ToUpperInvariant(c), lower = char.ToLowerInvariant(c);

                if (upper != c) targets.Add(upper);
                if (lower != c) targets.Add(lower);
            }

            return targets;
        });
    }
}
