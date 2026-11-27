using System;
using System.Text.RegularExpressions;

namespace OtimizaPC.Tweaks.Uninstall
{
    public static class UninstallCommandParser
    {
        private static readonly Regex Executable = new Regex(
            @"\A(?:""(?<file>[^""]+\.exe)""|(?<file>msiexec)|(?<file>[^""]+?\.exe))(?=\s|\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex MsiProduct = new Regex(
            @"\A\s*[/\-][ix]\s*(?:""(?<code>\{[0-9a-f-]{36}\})""|(?<code>\{[0-9a-f-]{36}\}))(?=\s|\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParse(string command, out string executable, out string arguments)
        {
            executable = null;
            arguments = null;
            if (string.IsNullOrWhiteSpace(command) || command.IndexOf('\0') >= 0) return false;

            command = command.Trim();
            var match = Executable.Match(command);
            if (!match.Success) return false;

            var file = Environment.ExpandEnvironmentVariables(match.Groups["file"].Value);
            var args = command.Substring(match.Length).TrimStart();
            var fileName = file.Substring(file.LastIndexOfAny(new[] { '\\', '/' }) + 1);
            if (string.Equals(fileName, "msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "msiexec", StringComparison.OrdinalIgnoreCase))
            {
                var product = MsiProduct.Match(args);
                if (!product.Success || !Guid.TryParseExact(product.Groups["code"].Value, "B", out var code))
                    return false;

                file = "msiexec.exe";
                args = "/x " + code.ToString("B").ToUpperInvariant();
            }

            executable = file;
            arguments = args;
            return true;
        }
    }
}
