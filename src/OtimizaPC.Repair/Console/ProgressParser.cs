using System;
using System.Text.RegularExpressions;

namespace OtimizaPC.Repair.Console
{
    /// <summary>Reconhece os formatos de percentual emitidos pelas ferramentas nativas.</summary>
    public static class ProgressParser
    {
        private static readonly Regex Percent = new Regex(
            @"(?<!\d)(?<value>\d{1,3})(?:[\.,]\d+)?\s*(?:%|percent(?:age)?)(?:\s+(?:complete|conclu[ií]do))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParse(string line, out int percent)
        {
            percent = 0;
            if (string.IsNullOrEmpty(line)) return false;
            var match = Percent.Match(line);
            if (!match.Success || !int.TryParse(match.Groups["value"].Value, out percent)) return false;
            percent = Math.Max(0, Math.Min(100, percent));
            return true;
        }
    }
}
