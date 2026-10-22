using System.Net;
using System.Text;
using OtimizaPC.Core.Models;

namespace OtimizaPC.Core.Reports
{
    /// <summary>Gera o relatório de hardware em HTML (para entregar ao cliente) ou texto puro.</summary>
    public static class ReportExporter
    {
        public static string ToText(HardwareReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Relatório de hardware — {report.MachineName}");
            sb.AppendLine($"Gerado em {report.GeneratedAt:dd/MM/yyyy HH:mm} pelo {AppInfo.Name} v{AppInfo.Version}");
            foreach (var section in report.Sections)
            {
                sb.AppendLine();
                sb.AppendLine("== " + section.Title.ToUpperInvariant() + " ==");
                foreach (var group in section.Groups)
                {
                    if (!string.IsNullOrEmpty(group.Header))
                        sb.AppendLine("-- " + group.Header);
                    foreach (var entry in group.Entries)
                        sb.AppendLine($"{entry.Label}: {entry.Value}");
                }
            }
            return sb.ToString();
        }

        public static string ToHtml(HardwareReport report)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>Relatório de hardware — ").Append(E(report.MachineName)).Append("</title>");
            sb.Append("<style>");
            // Cores da Nutriport: laranja de destaque; no escuro, fundo grafite e títulos dourados
            sb.Append(":root{--bg:#f5f5f3;--card:#fff;--text:#202020;--muted:#5f5f5a;--line:#e5e3dc;--accent:#c4621a;--brand:#202020}");
            sb.Append("@media (prefers-color-scheme:dark){:root{--bg:#202020;--card:#2b2b2b;--text:#f0f0f0;--muted:#b0aea4;--line:#3a3a3a;--accent:#f58634;--brand:#d2c068}}");
            sb.Append("*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,sans-serif}");
            sb.Append("main{max-width:960px;margin:0 auto;padding:32px 16px}");
            sb.Append("header{margin-bottom:24px;border-bottom:4px solid var(--accent);padding-bottom:12px}h1{margin:0;font-size:26px;color:var(--brand)}header p{margin:4px 0 0;color:var(--muted)}");
            sb.Append("section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:18px 20px;margin-bottom:16px;break-inside:avoid}");
            sb.Append("h2{margin:0 0 10px;font-size:18px;color:var(--accent)}h3{margin:14px 0 4px;font-size:14px}");
            sb.Append("table{width:100%;border-collapse:collapse}td{padding:5px 0;border-bottom:1px solid var(--line);vertical-align:top}");
            sb.Append("td:first-child{width:38%;color:var(--muted);padding-right:12px}tr:last-child td{border-bottom:0}");
            sb.Append("footer{color:var(--muted);font-size:12px;text-align:center;margin-top:24px}");
            sb.Append("@media print{body{background:#fff}section{border-color:#ccc}}");
            sb.Append("</style></head><body><main>");
            sb.Append("<header><h1>Relatório de hardware — ").Append(E(report.MachineName)).Append("</h1>");
            sb.Append("<p>Gerado em ").Append(report.GeneratedAt.ToString("dd/MM/yyyy 'às' HH:mm")).Append("</p></header>");

            foreach (var section in report.Sections)
            {
                sb.Append("<section><h2>").Append(E(section.Title)).Append("</h2>");
                foreach (var group in section.Groups)
                {
                    if (group.Entries.Count == 0) continue;
                    if (!string.IsNullOrEmpty(group.Header))
                        sb.Append("<h3>").Append(E(group.Header)).Append("</h3>");
                    sb.Append("<table>");
                    foreach (var entry in group.Entries)
                        sb.Append("<tr><td>").Append(E(entry.Label)).Append("</td><td>").Append(E(entry.Value)).Append("</td></tr>");
                    sb.Append("</table>");
                }
                sb.Append("</section>");
            }

            sb.Append("<footer>").Append(E(AppInfo.Name)).Append(" v").Append(E(AppInfo.Version)).Append("</footer>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
