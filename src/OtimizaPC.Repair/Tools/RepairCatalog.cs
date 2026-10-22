using System;
using System.Collections.Generic;
using System.Linq;

namespace OtimizaPC.Repair.Tools
{
    public static class RepairGroups
    {
        public const string Windows = "Arquivos do Windows";
        public const string Disk = "Disco";
        public const string Network = "Rede";
    }

    /// <summary>Atalho para uma ferramenta nativa do Windows (Gerenciador de Dispositivos etc.).</summary>
    public sealed class WindowsShortcut
    {
        public WindowsShortcut(string name, string description, string command, string arguments = null)
        {
            Name = name;
            Description = description;
            Command = command;
            Arguments = arguments;
        }

        public string Name { get; }

        public string Description { get; }

        public string Command { get; }

        public string Arguments { get; }
    }

    public static class RepairCatalog
    {
        public static IReadOnlyList<RepairTool> Tools() => new RepairTool[]
        {
            new CommandTool("sfc", "Verificar arquivos do sistema (SFC)",
                "Procura arquivos do Windows corrompidos e os substitui por cópias íntegras.",
                RepairGroups.Windows,
                new[] { new CommandStep("sfc.exe", "/scannow") },
                SummarizeSfc)
            { Duration = "5 a 20 minutos" },

            new CommandTool("dism-check", "Verificar imagem do Windows (DISM, rápido)",
                "Consulta se o repositório de componentes do Windows está marcado como corrompido. Não altera nada.",
                RepairGroups.Windows,
                new[] { new CommandStep("dism.exe", "/Online /Cleanup-Image /CheckHealth") },
                SummarizeDism)
            { Duration = "menos de 1 minuto" },

            new CommandTool("dism-restore", "Reparar imagem do Windows (DISM)",
                "Baixa do Windows Update e substitui componentes corrompidos. Rode antes do SFC quando ele não conseguir corrigir. Precisa de internet.",
                RepairGroups.Windows,
                new[] { new CommandStep("dism.exe", "/Online /Cleanup-Image /RestoreHealth") },
                SummarizeDism)
            { Duration = "10 a 40 minutos", UnsafeToCancel = true, NeedsConfirmation = true },

            new WindowsUpdateResetTool(),

            new CommandTool("chkdsk-scan", "Verificar o disco do sistema",
                "Procura erros no sistema de arquivos sem precisar reiniciar (CHKDSK online).",
                RepairGroups.Disk,
                new[] { new CommandStep("chkdsk.exe", "%SystemDrive% /scan") { SuccessCodes = new[] { 0, 1 } } },
                SummarizeChkdsk)
            { Duration = "2 a 10 minutos" },

            new CommandTool("chkdsk-boot", "Agendar reparo completo do disco",
                "Marca o disco do sistema para o CHKDSK completo na próxima inicialização. Use se a verificação acima encontrar erros.",
                RepairGroups.Disk,
                new[] { new CommandStep("fsutil.exe", "dirty set %SystemDrive%") },
                (lines, ok) => ok ? "Reparo agendado. Reinicie o computador para executar (pode levar bastante tempo)." : null)
            { Duration = "instantâneo (o reparo roda ao reiniciar)", RequiresRestart = true, NeedsConfirmation = true },

            new CommandTool("dns-flush", "Limpar cache de DNS",
                "Resolve sites que não abrem depois de mudanças de rede ou DNS.",
                RepairGroups.Network,
                new[] { new CommandStep("ipconfig.exe", "/flushdns") })
            { RequiresAdmin = false, Duration = "instantâneo" },

            new CommandTool("ip-renew", "Renovar endereço IP",
                "Pede um novo IP ao roteador. A conexão cai por alguns segundos.",
                RepairGroups.Network,
                new[]
                {
                    new CommandStep("ipconfig.exe", "/release"),
                    new CommandStep("ipconfig.exe", "/renew"),
                })
            { Duration = "menos de 1 minuto", NeedsConfirmation = true },

            new CommandTool("network-reset", "Redefinir a pilha de rede",
                "Restaura Winsock e TCP/IP ao padrão. Resolve internet que não funciona mesmo com a rede conectada. Configurações de IP fixo e VPN podem precisar ser refeitas.",
                RepairGroups.Network,
                new[]
                {
                    new CommandStep("ipconfig.exe", "/flushdns"),
                    new CommandStep("netsh.exe", "winsock reset"),
                    new CommandStep("netsh.exe", "int ip reset") { SuccessCodes = new[] { 0, 1 } },
                },
                (lines, ok) => "Redefinido. Reinicie o computador para concluir.")
            { Duration = "menos de 1 minuto", RequiresRestart = true, NeedsConfirmation = true },
        };

        public static IReadOnlyList<WindowsShortcut> Shortcuts() => new[]
        {
            new WindowsShortcut("Gerenciador de Dispositivos", "Drivers e hardware", "devmgmt.msc"),
            new WindowsShortcut("Visualizador de Eventos", "Logs completos do Windows", "eventvwr.msc"),
            new WindowsShortcut("Monitor de Confiabilidade", "Linha do tempo de falhas e instalações", "perfmon.exe", "/rel"),
            new WindowsShortcut("Serviços", "Iniciar, parar e configurar serviços", "services.msc"),
            new WindowsShortcut("Gerenciamento de Disco", "Partições e volumes", "diskmgmt.msc"),
            new WindowsShortcut("Informações do Sistema", "Relatório detalhado (msinfo32)", "msinfo32.exe"),
            new WindowsShortcut("Diagnóstico de Memória", "Testa a RAM ao reiniciar", "mdsched.exe"),
            new WindowsShortcut("Proteção do Sistema", "Pontos de restauração", "SystemPropertiesProtection.exe"),
            new WindowsShortcut("Conexões de Rede", "Adaptadores de rede", "ncpa.cpl"),
            new WindowsShortcut("Programas e Recursos", "Desinstalar programas", "appwiz.cpl"),
            new WindowsShortcut("Configuração do Sistema", "Inicialização seletiva e boot (msconfig)", "msconfig.exe"),
            new WindowsShortcut("Editor do Registro", "Para usuários avançados", "regedit.exe"),
        };

        // ---------- Interpretação das saídas (independente do idioma sempre que possível) ----------

        internal static string SummarizeSfc(IReadOnlyList<string> lines, bool ok)
        {
            var text = string.Join(" ", lines);
            // Os resumos do SFC citam o CBS.log apenas quando encontram problemas
            if (Contains(text, "did not find any integrity violations") || Contains(text, "não encontrou nenhuma violação"))
                return "Nenhum arquivo corrompido encontrado.";
            if (Contains(text, "successfully repaired") || Contains(text, "reparou com êxito") || Contains(text, "reparou-os com êxito"))
                return "Arquivos corrompidos encontrados e reparados. Reinicie o computador.";
            if (Contains(text, "unable to fix") || Contains(text, "não conseguiu corrigir") || Contains(text, "não pôde reparar"))
                return "Há arquivos corrompidos que o SFC não conseguiu reparar. Rode \"Reparar imagem do Windows (DISM)\" e depois o SFC de novo.";
            return ok ? "Verificação concluída. Veja a saída acima." : "O SFC terminou com erro. Veja a saída acima.";
        }

        internal static string SummarizeDism(IReadOnlyList<string> lines, bool ok)
        {
            var text = string.Join(" ", lines);
            if (Contains(text, "No component store corruption detected") || Contains(text, "Nenhuma corrupção do repositório de componentes"))
                return "Imagem do Windows íntegra.";
            if (Contains(text, "restore operation completed successfully") || Contains(text, "operação de restauração foi concluída com êxito"))
                return "Imagem do Windows reparada. Rode o SFC em seguida.";
            if (Contains(text, "repairable") || Contains(text, "reparável"))
                return "A imagem do Windows tem corrupção reparável. Rode \"Reparar imagem do Windows (DISM)\".";
            return ok ? "Concluído. Veja a saída acima." : "O DISM terminou com erro. Veja a saída acima.";
        }

        internal static string SummarizeChkdsk(IReadOnlyList<string> lines, bool ok)
        {
            var text = string.Join(" ", lines);
            if (Contains(text, "found no problems") || Contains(text, "não encontrou problemas") || Contains(text, "nenhum problema"))
                return "Nenhum problema encontrado no disco.";
            return ok
                ? "Verificação concluída. Se houver erros na saída, use \"Agendar reparo completo do disco\"."
                : "Foram encontrados erros. Use \"Agendar reparo completo do disco\" e reinicie.";
        }

        private static bool Contains(string text, string value) =>
            text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
