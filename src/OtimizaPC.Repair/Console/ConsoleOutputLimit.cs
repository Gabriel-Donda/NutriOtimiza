using System.Collections.Generic;

namespace OtimizaPC.Repair.Console
{
    public static class ConsoleOutputLimit
    {
        private const int MaxLines = 5000;

        public static void TrimOldest<T>(IList<T> lines)
        {
            while (lines.Count > MaxLines)
                lines.RemoveAt(0);
        }
    }
}
