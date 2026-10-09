namespace Patternimorph.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Expands a subset of .NET regex into a finite list of patterns in a SQL pattern
    /// <see cref="PatternDialect"/> whose OR-combination matches exactly the same strings.
    /// Throws NotSupportedException (valid regex, no exact form) or FormatException (malformed).
    /// </summary>
    internal sealed class RegexPatternParser
    {
        private enum EndAnchor { None, EndOnly, EndOrFinalNewline }

        private static readonly (char Lo, char Hi)[] DigitRanges = { ('0', '9') };
        private static readonly (char Lo, char Hi)[] WordRanges = { ('0', '9'), ('A', 'Z'), ('_', '_'), ('a', 'z') };
        private static readonly (char Lo, char Hi)[] SpaceRanges = { ('\t', '\r'), (' ', ' ') };

        private readonly string _re;
        private readonly RegexConversionOptions _opt;
        private readonly PatternDialect _d;
        private readonly string _anyChar;
        private readonly string _anyString;
        private int _pos;
        private bool _negatedShorthandInClass;

        public RegexPatternParser(string regex, RegexConversionOptions options, PatternDialect dialect)
        {
            if (options.MaxPatterns < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxPatterns must be positive.");
            if (options.MaxPatternLength < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxPatternLength must be positive.");

            _re = regex;
            _opt = options;
            _d = dialect;
            _anyChar = dialect.AnyChar.ToString();
            _anyString = dialect.AnyString.ToString();
        }

        public IReadOnlyList<string> Parse()
        {
            var result = new List<string>();

            while (true)
            {
                // Anchors belong to each branch: ^a|b$ means (^a)|(b$).
                result.AddRange(ParseTopLevelBranch());
                CheckCount(result.Count);

                if (!TryConsume('|'))
                    break;
            }

            if (_pos != _re.Length)
                throw new FormatException($"Unexpected '{_re[_pos]}' at position {_pos}.");

            return Normalize(result);
        }

        // ---------------------------------------------------------------- structure

        private List<string> ParseTopLevelBranch()
        {
            bool anchoredStart = TryConsume('^') || TryConsume("\\A");

            var patterns = ParseSequence(nested: false, out EndAnchor end);

            // FullMatch is \A(?:regex)\z: explicit edge anchors are redundant, and '$' cannot match
            // before a final '\n' because \z must follow it.
            if (_opt.FullMatch)
                return patterns;

            if (!anchoredStart)
                patterns = patterns.Select(p => _anyString + p).ToList();

            if (end == EndAnchor.None)
                patterns = patterns.Select(p => p + _anyString).ToList();
            else if (end == EndAnchor.EndOrFinalNewline && _opt.DollarMatchesBeforeFinalNewline)
                patterns = patterns.Concat(patterns.Select(p => p + _d.Literal('\n'))).ToList();

            CheckCount(patterns.Count);
            return patterns;
        }

        private List<string> ParseSequence(bool nested, out EndAnchor end)
        {
            end = EndAnchor.None;
            var result = new List<string> { "" };

            while (_pos < _re.Length)
            {
                char c = _re[_pos];

                if (c == '|' || (c == ')' && nested))
                    break;

                if (c == ')')
                    throw new FormatException($"Unbalanced ')' at position {_pos}.");

                if (!nested && TryParseEndAnchor(out end))
                    break;

                var atom = ParseAtom();
                atom = ParseQuantifier(atom);
                result = CrossProduct(result, atom);
            }

            return result;
        }

        private bool TryParseEndAnchor(out EndAnchor end)
        {
            int len;

            if (Peek() == '$') { len = 1; end = EndAnchor.EndOrFinalNewline; }
            else if (Peek() == '\\' && Peek(1) == 'Z') { len = 2; end = EndAnchor.EndOrFinalNewline; }
            else if (Peek() == '\\' && Peek(1) == 'z') { len = 2; end = EndAnchor.EndOnly; }
            else { end = EndAnchor.None; return false; }

            if (_pos + len != _re.Length && Peek(len) != '|')
                throw new NotSupportedException("End anchors are only supported at the end of a top-level branch.");

            _pos += len;
            return true;
        }

        private List<string> ParseAtom()
        {
            char c = _re[_pos++];

            switch (c)
            {
                case '.':
                    return new List<string>
                    {
                        _opt.DotMatchesNewline
                            ? _anyChar
                            : _d.CharacterSet(new[] { ('\n', '\n') }, negated: true)
                    };

                case '[':
                    return new List<string> { ParseCharacterClass() };

                case '\\':
                    return new List<string> { ParseEscape() };

                case '(':
                    return ParseGroup();

                case '*':
                case '+':
                case '?':
                    throw new FormatException($"Quantifier '{c}' at position {_pos - 1} has nothing to repeat.");

                case '{' when IsBoundedQuantifierAt(_pos - 1):
                    throw new FormatException($"Quantifier at position {_pos - 1} has nothing to repeat.");

                case '^':
                case '$':
                    throw new NotSupportedException("Anchors are only supported at the start/end of a top-level branch.");

                default:
                    // Includes '{' that does not start a quantifier, and lone ']' / '}',
                    // all of which .NET treats as literals.
                    return new List<string> { _d.Literal(c) };
            }
        }

        private List<string> ParseGroup()
        {
            if (Peek() == '?')
            {
                if (Peek(1) == ':')
                    _pos += 2;
                else if ((Peek(1) == '<' && Peek(2) != '=' && Peek(2) != '!') || Peek(1) == '\'')
                    SkipGroupName();
                else
                    throw new NotSupportedException("Only (...), (?:...) and named groups are supported.");
            }

            var result = new List<string>();

            while (true)
            {
                result.AddRange(ParseSequence(nested: true, out _));
                CheckCount(result.Count);

                if (!TryConsume('|'))
                    break;
            }

            if (!TryConsume(')'))
                throw new FormatException("Missing ')'.");

            return Distinct(result);
        }

        private void SkipGroupName()
        {
            char close = Peek(1) == '<' ? '>' : '\'';
            int start = _pos + 2;
            int end = _re.IndexOf(close, start);

            if (end < 0)
                throw new FormatException("Unterminated group name.");
            if (end == start)
                throw new FormatException("Empty group name.");

            for (int i = start; i < end; i++)
            {
                if (!char.IsLetterOrDigit(_re[i]) && _re[i] != '_')
                    throw new NotSupportedException("Balancing groups are unsupported.");
            }

            _pos = end + 1;
        }

        // ---------------------------------------------------------------- quantifiers

        private List<string> ParseQuantifier(List<string> atom)
        {
            int min, max; // max < 0 means unbounded

            switch (Peek())
            {
                case '?': _pos++; min = 0; max = 1; break;
                case '*': _pos++; min = 0; max = -1; break;
                case '+': _pos++; min = 1; max = -1; break;
                case '{' when IsBoundedQuantifierAt(_pos): (min, max) = ParseBoundedQuantifier(); break;
                default: return atom;
            }

            // Lazy quantifiers match exactly the same set of strings.
            TryConsume('?');

            return ApplyQuantifier(atom, min, max);
        }

        private List<string> ApplyQuantifier(List<string> atom, int min, int max)
        {
            if (max == 0 || atom.All(p => p.Length == 0))
                return new List<string> { "" };

            // Pure wildcard atoms: '.', '.*', '.+', '(?:..*)' ...
            // (w){m,} with a single any-char or with any any-string is just "at least N characters".
            if (atom.Count == 1 &&
                IsWildcardOnly(atom[0], out int q, out bool star) &&
                (star || (q == 1 && max < 0)))
            {
                long len = (long)q * min;
                if (len + 1 > _opt.MaxPatternLength)
                    throw new NotSupportedException($"{_d.Name} pattern would be too long.");

                return new List<string> { new string(_d.AnyChar, (int)len) + _anyString };
            }

            if (max < 0)
                throw new NotSupportedException(
                    "Unbounded repetition is only representable for arbitrary-character wildcards like '.'.");

            if (max > _opt.MaxPatternLength)
                throw new NotSupportedException("Repetition count is too large.");

            // Build x{min}, then extend by one atom per step: O(max) cross products, not O(max^2).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            var current = Repeat(atom, min);

            for (int n = min; ; n++)
            {
                foreach (string s in current)
                {
                    if (seen.Add(s))
                        result.Add(s);
                }

                CheckCount(result.Count);

                if (n == max)
                    break;

                current = CrossProduct(current, atom);
            }

            return result;
        }

        private List<string> Repeat(List<string> atom, int count)
        {
            var result = new List<string> { "" };

            for (int i = 0; i < count; i++)
                result = CrossProduct(result, atom);

            return result;
        }

        private bool IsBoundedQuantifierAt(int i)
        {
            // .NET only treats {n}, {n,} and {n,m} as quantifiers; any other '{' is a literal.
            if (i >= _re.Length || _re[i] != '{')
                return false;

            i++;
            int start = i;

            while (i < _re.Length && IsAsciiDigit(_re[i]))
                i++;

            if (i == start)
                return false;

            if (i < _re.Length && _re[i] == ',')
            {
                i++;
                while (i < _re.Length && IsAsciiDigit(_re[i]))
                    i++;
            }

            return i < _re.Length && _re[i] == '}';
        }

        private (int Min, int Max) ParseBoundedQuantifier()
        {
            _pos++; // {

            int min = ParseInteger();
            int max = min;

            if (TryConsume(','))
                max = Peek() == '}' ? -1 : ParseInteger();

            _pos++; // } (validated by IsBoundedQuantifierAt)

            if (max >= 0 && max < min)
                throw new FormatException("Quantifier {n,m} has m < n.");

            return (min, max);
        }

        private int ParseInteger()
        {
            int start = _pos;

            while (IsAsciiDigit(Peek()))
                _pos++;

            if (!int.TryParse(_re.Substring(start, _pos - start), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int value))
                throw new FormatException("Quantifier bound is too large.");

            return value;
        }

        // ---------------------------------------------------------------- classes & escapes

        private string ParseCharacterClass()
        {
            bool negated = TryConsume('^');
            var ranges = new List<(char Lo, char Hi)>();
            bool first = true;
            _negatedShorthandInClass = false;

            while (true)
            {
                if (_pos >= _re.Length)
                    throw new FormatException("Unterminated character class.");

                // A ']' in first position is a literal, as in .NET.
                if (Peek() == ']' && !first)
                {
                    _pos++;
                    break;
                }

                if (Peek() == '-' && Peek(1) == '[' && !first)
                    throw new NotSupportedException("Character class subtraction is unsupported.");

                first = false;

                if (!TryReadClassChar(ranges, out char lo))
                    continue; // a shorthand such as \d was added directly

                char hi = lo;

                if (Peek() == '-' && _pos + 1 < _re.Length && Peek(1) != ']')
                {
                    if (Peek(1) == '[')
                        throw new NotSupportedException("Character class subtraction is unsupported.");

                    _pos++; // -

                    if (!TryReadClassChar(ranges, out hi))
                        throw new FormatException("A shorthand class cannot be a range endpoint.");

                    if (hi < lo)
                        throw new FormatException("Character range is in reverse order.");
                }

                ranges.Add((lo, hi));
            }

            var merged = Merge(ranges);

            // [\s\S], [\d\D], [\0-￿]: any single code unit, i.e. '.' with Singleline.
            if (!negated && merged.Count == 1 && merged[0].Lo == '\0' && merged[0].Hi == '￿')
                return _anyChar;

            if (_negatedShorthandInClass)
                throw new NotSupportedException(@"'\D', '\W' and '\S' inside [] are only supported when the class matches any character.");

            if (merged.Any(r => r.Lo <= '\uDFFF' && r.Hi >= '\uD800'))
                throw new NotSupportedException("Surrogate code units inside classes are unsupported.");

            return _d.CharacterSet(merged, negated);
        }

        /// <summary>
        /// Reads one class member. Returns false if it was a shorthand (\d, \w, \s, or their
        /// negations) whose ranges were appended to <paramref name="ranges"/> instead.
        /// </summary>
        private bool TryReadClassChar(List<(char Lo, char Hi)> ranges, out char value)
        {
            value = _re[_pos++];

            if (value != '\\')
                return true;

            if (_pos >= _re.Length)
                throw new FormatException("Trailing '\\'.");

            char e = _re[_pos];
            var shorthand = ShorthandRanges(e, out bool negatedShorthand);

            if (shorthand != null)
            {
                _pos++;
                ranges.AddRange(negatedShorthand ? Complement(shorthand) : shorthand);
                _negatedShorthandInClass |= negatedShorthand;
                return false;
            }

            if (e == 'b')
            {
                _pos++;
                value = '\b'; // backspace inside a class
                return true;
            }

            value = ReadEscapedChar();
            return true;
        }

        private string ParseEscape()
        {
            if (_pos >= _re.Length)
                throw new FormatException("Trailing '\\'.");

            char e = _re[_pos];
            var shorthand = ShorthandRanges(e, out bool negated);

            if (shorthand != null)
            {
                _pos++;
                return _d.CharacterSet(shorthand, negated);
            }

            if (e is 'A' or 'Z' or 'z')
                throw new NotSupportedException($@"'\{e}' is only supported at the start/end of a top-level branch.");

            return _d.Literal(ReadEscapedChar());
        }

        private char ReadEscapedChar()
        {
            char e = _re[_pos++];

            switch (e)
            {
                case 'n': return '\n';
                case 'r': return '\r';
                case 't': return '\t';
                case 'f': return '\f';
                case 'v': return '\v';
                case 'a': return '\a';
                case 'e': return '\u001B';
                case 'x': return (char)ReadHex(2);
                case 'u': return (char)ReadHex(4);
            }

            // In .NET an escaped non-word character is always that literal character.
            if (!char.IsLetterOrDigit(e) && e != '_')
                return e;

            // \b \B \G \p \k \1 \cX \0 ...
            throw new NotSupportedException($@"Escape '\{e}' is unsupported.");
        }

        private int ReadHex(int digits)
        {
            if (_pos + digits > _re.Length ||
                !int.TryParse(_re.Substring(_pos, digits), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out int value))
                throw new FormatException("Invalid hex escape.");

            _pos += digits;
            return value;
        }

        private static (char Lo, char Hi)[]? ShorthandRanges(char c, out bool negated)
        {
            negated = char.IsUpper(c);

            return c switch
            {
                'd' or 'D' => DigitRanges,
                'w' or 'W' => WordRanges,
                's' or 'S' => SpaceRanges,
                _ => null
            };
        }

        /// <summary>All code units not in the (sorted, non-overlapping) <paramref name="ranges"/>.</summary>
        private static IEnumerable<(char Lo, char Hi)> Complement((char Lo, char Hi)[] ranges)
        {
            int next = 0;

            foreach (var (lo, hi) in ranges)
            {
                if (lo > next)
                    yield return ((char)next, (char)(lo - 1));
                next = hi + 1;
            }

            if (next <= char.MaxValue)
                yield return ((char)next, char.MaxValue);
        }

        private static List<(char Lo, char Hi)> Merge(IEnumerable<(char Lo, char Hi)> ranges)
        {
            var result = new List<(char Lo, char Hi)>();

            foreach (var r in ranges.OrderBy(r => r.Lo))
            {
                int lastIndex = result.Count - 1;

                if (lastIndex >= 0 && r.Lo <= result[lastIndex].Hi + 1)
                {
                    var last = result[lastIndex];
                    result[lastIndex] = (last.Lo, (char)Math.Max(last.Hi, r.Hi));
                }
                else
                {
                    result.Add(r);
                }
            }

            return result;
        }

        // ---------------------------------------------------------------- output

        private IReadOnlyList<string> Normalize(List<string> patterns)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            foreach (string p in patterns)
            {
                string n = CanonicalizeWildcards(p);

                // The any-string wildcard alone matches everything, so every other pattern is redundant.
                if (n == _anyString)
                    return new[] { _anyString };

                if (n.Length > _opt.MaxPatternLength)
                    throw new NotSupportedException($"{_d.Name} pattern would be too long.");

                if (seen.Add(n))
                    result.Add(n);
            }

            return result;
        }

        /// <summary>
        /// Rewrites every run of wildcards as "any-char x N, then any-string" (GLOB: "**" => "*",
        /// "*?" => "?*"), so equivalent patterns deduplicate. Other tokens are copied verbatim.
        /// </summary>
        private string CanonicalizeWildcards(string p)
        {
            var sb = new StringBuilder(p.Length);
            int i = 0;

            while (i < p.Length)
            {
                if (IsWildcard(p[i]))
                {
                    int q = 0;
                    bool star = false;

                    while (i < p.Length && IsWildcard(p[i]))
                    {
                        if (p[i] == _d.AnyChar) q++;
                        else star = true;
                        i++;
                    }

                    sb.Append(_d.AnyChar, q);
                    if (star) sb.Append(_d.AnyString);
                }
                else
                {
                    int len = _d.TokenLength(p, i);
                    sb.Append(p, i, len);
                    i += len;
                }
            }

            return sb.ToString();
        }

        // ---------------------------------------------------------------- helpers

        private List<string> CrossProduct(List<string> left, List<string> right)
        {
            CheckCount((long)left.Count * right.Count);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(left.Count * right.Count);

            foreach (string a in left)
                foreach (string b in right)
                {
                    if (a.Length + b.Length > _opt.MaxPatternLength)
                        throw new NotSupportedException($"{_d.Name} pattern would be too long.");

                    string s = a + b;
                    if (seen.Add(s))
                        result.Add(s);
                }

            return result;
        }

        private List<string> Distinct(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            foreach (string s in values)
            {
                if (seen.Add(s))
                    result.Add(s);
            }

            CheckCount(result.Count);
            return result;
        }

        private bool IsWildcard(char c) => c == _d.AnyChar || c == _d.AnyString;

        /// <summary>
        /// True if <paramref name="p"/> consists only of wildcards. Checking characters is enough:
        /// dialects never emit a bare wildcard character as a literal (see <see cref="PatternDialect"/>).
        /// </summary>
        private bool IsWildcardOnly(string p, out int anyChars, out bool hasAnyString)
        {
            anyChars = 0;
            hasAnyString = false;

            foreach (char c in p)
            {
                if (c == _d.AnyChar) anyChars++;
                else if (c == _d.AnyString) hasAnyString = true;
                else return false;
            }

            return p.Length > 0;
        }

        private void CheckCount(long count)
        {
            if (count > _opt.MaxPatterns)
                throw new NotSupportedException($"Conversion expands beyond {_opt.MaxPatterns} {_d.Name} patterns.");
        }

        private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

        private char Peek(int offset = 0)
            => _pos + offset < _re.Length ? _re[_pos + offset] : '\0';

        private bool TryConsume(char c)
        {
            if (_pos < _re.Length && _re[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        private bool TryConsume(string s)
        {
            if (_pos + s.Length <= _re.Length &&
                string.CompareOrdinal(_re, _pos, s, 0, s.Length) == 0)
            {
                _pos += s.Length;
                return true;
            }

            return false;
        }
    }
}
