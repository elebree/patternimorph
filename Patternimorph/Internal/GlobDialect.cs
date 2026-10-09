namespace Patternimorph.Internal
{
    using System.Collections.Generic;
    using System.Text;

    internal sealed class GlobDialect : PatternDialect
    {
        public static readonly GlobDialect Instance = new();

        public override char AnyChar => '?';
        public override char AnyString => '*';
        public override string Name => "GLOB";

        public override string Literal(char c)
        {
            // SQLite GLOB has no backslash escaping; special characters go in brackets.
            return c switch
            {
                '*' => "[*]",
                '?' => "[?]",
                '[' => "[[]",
                _ => c.ToString()
            };
        }

        /// <summary>
        /// Emits a GLOB bracket expression. GLOB has no escaping, so ']' must come first,
        /// '-' last, and '^' anywhere but first.
        /// </summary>
        public override string CharacterSet(IReadOnlyList<(char Lo, char Hi)> ranges, bool negated)
        {
            if (!negated && ranges.Count == 1 && ranges[0].Lo == ranges[0].Hi)
                return Literal(ranges[0].Lo);

            bool hasBracket = false, hasCaret = false, hasDash = false;
            var body = new StringBuilder();

            foreach (var (lo, hi) in ranges)
            {
                int start = lo;

                foreach (char special in "-]^") // ascending code-point order
                {
                    if (special < start || special > hi)
                        continue;

                    if (special > start)
                        AppendRange(body, (char)start, (char)(special - 1));

                    start = special + 1;

                    if (special == '-') hasDash = true;
                    else if (special == ']') hasBracket = true;
                    else hasCaret = true;
                }

                if (start <= hi)
                    AppendRange(body, (char)start, hi);
            }

            // Only '-' and '^' (a lone '^' was handled above). "[^-]" would be a negation.
            if (!negated && !hasBracket && body.Length == 0 && hasCaret)
                return "[-^]";

            var sb = new StringBuilder("[");
            if (negated) sb.Append('^');
            if (hasBracket) sb.Append(']');
            sb.Append(body);
            if (hasCaret) sb.Append('^');
            if (hasDash) sb.Append('-');
            sb.Append(']');

            return sb.ToString();
        }

        public override int TokenLength(string p, int i)
        {
            if (p[i] != '[')
                return 1;

            // Mirrors SQLite: optional '^', then a leading ']' is literal.
            int j = i + 1;
            if (j < p.Length && p[j] == '^') j++;
            if (j < p.Length && p[j] == ']') j++;
            while (p[j] != ']') j++; // classes we emit are always terminated
            return j - i + 1;
        }

        private static void AppendRange(StringBuilder sb, char lo, char hi)
        {
            sb.Append(lo);

            if (hi == lo)
                return;

            if (hi > lo + 1)
                sb.Append('-');

            sb.Append(hi);
        }
    }
}
