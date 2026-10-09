namespace Patternimorph.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>Builds <c>("col" OP p0 [suffix] OR ...)</c> for GLOB and LIKE.</summary>
    internal static class SqlPredicate
    {
        public static string Positional(string column, IReadOnlyList<string> patterns, string op, string suffix)
        {
            if (column == null) throw new ArgumentNullException(nameof(column));
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));

            return Build(column, patterns.Count, _ => "?", op, suffix);
        }

        public static string Named(
            string column, IReadOnlyList<string> patterns, string parameterPrefix, string op, string suffix)
        {
            if (column == null) throw new ArgumentNullException(nameof(column));
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));
            if (parameterPrefix == null) throw new ArgumentNullException(nameof(parameterPrefix));

            if (parameterPrefix.Length < 2 ||
                "@$:".IndexOf(parameterPrefix[0]) < 0 ||
                !parameterPrefix.Skip(1).All(c => c == '_' || c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c is >= '0' and <= '9'))
                throw new ArgumentException(
                    "Parameter prefix must be '@', '$' or ':' followed by ASCII letters, digits or '_'.",
                    nameof(parameterPrefix));

            return Build(column, patterns.Count,
                i => parameterPrefix + i.ToString(CultureInfo.InvariantCulture), op, suffix);
        }

        private static string Build(string column, int count, Func<int, string> parameter, string op, string suffix)
        {
            if (count == 0) return "(1 = 0)"; // portable FALSE: "0" is not a boolean in e.g. PostgreSQL or SQL Server

            string col = "\"" + column.Replace("\"", "\"\"") + "\"";
            return "(" + string.Join(" OR ", Enumerable.Range(0, count).Select(i =>
                col + " " + op + " " + parameter(i) + suffix)) + ")";
        }
    }
}
