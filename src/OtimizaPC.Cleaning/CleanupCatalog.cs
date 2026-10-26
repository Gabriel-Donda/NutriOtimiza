using System;
using System.Collections.Generic;
using System.Linq;
using OtimizaPC.Cleaning.Cleaners;
using OtimizaPC.Cleaning.Files;

namespace OtimizaPC.Cleaning
{
    /// <summary>
    /// Lista curada de itens de limpeza. Fica de fora, de propósito, o que costuma ser placebo ou nocivo:
    /// Prefetch (deixa o boot mais lento), "limpeza de registro" e cache de ícones.
    /// </summary>
    public static class CleanupCatalog
    {
        private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
        private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);
        private static readonly TimeSpan OneMonth = TimeSpan.FromDays(30);

        /// <summary>Itens disponíveis neste computador.</summary>
        public static IReadOnlyList<Cleaner> Create() =>
            All().Where(c =>
            {
                try
                {
                    return c.IsAvailable;
                }
                catch
                {
                    return false;
                }
            }).ToList();

        private static IEnumerable<Cleaner> All()
        {
            // ---------- Windows ----------
            yield return new FileCleaner("user-temp", "Arquivos temporários do usuário",
                "Pasta %TEMP%. Arquivos alterados nas últimas 24 horas são mantidos.",
                CleanupGroups.Windows,
                new[] { new FileTarget("%TEMP%", minAge: OneDay) });

            yield return new FileCleaner("windows-temp", "Arquivos temporários do Windows",
                "Pasta C:\\Windows\\Temp. Arquivos alterados nas últimas 24 horas são mantidos.",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%WINDIR%\Temp", minAge: OneDay) },
                requiresAdmin: true);

            yield return new WindowsUpdateCleaner();

            yield return new FileCleaner("delivery-optimization", "Cache de Otimização de Entrega",
                "Pedaços de atualizações compartilhados com outros PCs da rede.",
                CleanupGroups.Windows,
                new[]
                {
                    new FileTarget(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                    new FileTarget(@"%WINDIR%\SoftwareDistribution\DeliveryOptimization"),
                },
                requiresAdmin: true);

            yield return new FileCleaner("user-error-reports", "Relatórios de erro (usuário)",
                "Relatórios de falha de programas já enviados ou pendentes.",
                CleanupGroups.Windows,
                new[]
                {
                    new FileTarget(@"%LocalAppData%\Microsoft\Windows\WER\ReportArchive"),
                    new FileTarget(@"%LocalAppData%\Microsoft\Windows\WER\ReportQueue"),
                });

            yield return new FileCleaner("system-error-reports", "Relatórios de erro do sistema",
                "Relatórios de falha do Windows e de serviços.",
                CleanupGroups.Windows,
                new[]
                {
                    new FileTarget(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"),
                    new FileTarget(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue"),
                    new FileTarget(@"%ProgramData%\Microsoft\Windows\WER\Temp"),
                },
                requiresAdmin: true);

            yield return new FileCleaner("windows-logs", "Logs antigos do Windows",
                "Logs de manutenção (CBS e DISM) com mais de 7 dias.",
                CleanupGroups.Windows,
                new[]
                {
                    new FileTarget(@"%WINDIR%\Logs\CBS", "*.log", recursive: false, minAge: OneWeek),
                    new FileTarget(@"%WINDIR%\Logs\CBS", "*.cab", recursive: false, minAge: OneWeek),
                    new FileTarget(@"%WINDIR%\Logs\DISM", "*.log", recursive: false, minAge: OneWeek),
                },
                requiresAdmin: true);

            yield return new FileCleaner("windows-update-logs", "Logs antigos do Windows Update",
                "Rastreamentos ETL do Windows Update com mais de 7 dias.",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%WINDIR%\Logs\WindowsUpdate", "*.etl", recursive: false, minAge: OneWeek) },
                requiresAdmin: true);

            yield return new FileCleaner("diagnostic-logs", "Logs de diagnóstico antigos",
                "Rastreamentos ETL de diagnóstico do Windows.",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%ProgramData%\Microsoft\Diagnosis\ETLLogs", "*.etl", minAge: OneWeek) },
                requiresAdmin: true);

            yield return new FileCleaner("defender-history", "Histórico do Microsoft Defender",
                "Resultados antigos de verificações do Defender.",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%ProgramData%\Microsoft\Windows Defender\Scans\History\Results", minAge: OneWeek) },
                CleanupRisk.Moderate, requiresAdmin: true, defaultSelected: false);

            yield return new FileCleaner("iis-logs", "Logs antigos do IIS",
                "Logs de sites do IIS com mais de 30 dias.",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%SystemDrive%\inetpub\logs\LogFiles", "*.log", minAge: OneMonth) },
                requiresAdmin: true);

            yield return new FileCleaner("store-cache", "Cache da Microsoft Store",
                "Cache local da Microsoft Store. Ele será recriado quando necessário.",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%LocalAppData%\Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache") });

            yield return new FileCleaner("memory-dumps", "Dumps de memória",
                "Arquivos de diagnóstico de telas azuis e travamentos. Mantenha se for investigar um problema.",
                CleanupGroups.Windows,
                new[]
                {
                    new FileTarget(@"%WINDIR%\Minidump", "*.dmp", recursive: false),
                    new FileTarget(@"%WINDIR%\LiveKernelReports", "*.dmp"),
                    new FileTarget("%WINDIR%", "MEMORY.DMP", recursive: false),
                    new FileTarget(@"%LocalAppData%\CrashDumps"),
                },
                CleanupRisk.Moderate, requiresAdmin: true, defaultSelected: false);

            yield return new FileCleaner("thumbnails", "Cache de miniaturas",
                "Miniaturas de fotos e vídeos do Explorador. São recriadas ao abrir as pastas (fica mais lento na primeira vez).",
                CleanupGroups.Windows,
                new[] { new FileTarget(@"%LocalAppData%\Microsoft\Windows\Explorer", "thumbcache_*.db", recursive: false) },
                CleanupRisk.Moderate, defaultSelected: false);

            yield return new FileCleaner("windows-upgrade-remnants", "Restos de atualizações do Windows",
                "Pastas temporárias deixadas por atualizações anteriores. Confirme que não há atualização em andamento.",
                CleanupGroups.Windows,
                new[]
                {
                    new FileTarget(@"%SystemDrive%\$WINDOWS.~BT"),
                    new FileTarget(@"%SystemDrive%\$WINDOWS.~WS"),
                    new FileTarget(@"%SystemDrive%\$GetCurrent"),
                },
                CleanupRisk.Moderate, requiresAdmin: true, defaultSelected: false);

            yield return new RecycleBinCleaner();
            yield return new ComponentStoreCleaner();

            // ---------- Navegadores (somente cache: senhas, histórico e cookies são preservados) ----------
            const string browserNote = "Somente cache. Senhas, histórico, cookies e abas são preservados.";

            yield return new AppCacheCleaner("chrome", "Google Chrome", browserNote, CleanupGroups.Browsers,
                AppCacheCleaner.ChromiumTargets(@"%LocalAppData%\Google\Chrome\User Data"), new[] { "chrome" });

            yield return new AppCacheCleaner("edge", "Microsoft Edge", browserNote, CleanupGroups.Browsers,
                AppCacheCleaner.ChromiumTargets(@"%LocalAppData%\Microsoft\Edge\User Data"), new[] { "msedge" });

            yield return new AppCacheCleaner("brave", "Brave", browserNote, CleanupGroups.Browsers,
                AppCacheCleaner.ChromiumTargets(@"%LocalAppData%\BraveSoftware\Brave-Browser\User Data"), new[] { "brave" });

            yield return new AppCacheCleaner("vivaldi", "Vivaldi", browserNote, CleanupGroups.Browsers,
                AppCacheCleaner.ChromiumTargets(@"%LocalAppData%\Vivaldi\User Data"), new[] { "vivaldi" });

            yield return new AppCacheCleaner("opera", "Opera", browserNote, CleanupGroups.Browsers,
                new[]
                {
                    new FileTarget(@"%LocalAppData%\Opera Software\Opera Stable\Cache"),
                    new FileTarget(@"%LocalAppData%\Opera Software\Opera GX Stable\Cache"),
                    new FileTarget(@"%AppData%\Opera Software\Opera Stable\Code Cache"),
                    new FileTarget(@"%AppData%\Opera Software\Opera Stable\GPUCache"),
                },
                new[] { "opera" });

            yield return new AppCacheCleaner("firefox", "Mozilla Firefox", browserNote, CleanupGroups.Browsers,
                AppCacheCleaner.FirefoxTargets(), new[] { "firefox" });

            // ---------- Aplicativos e jogos ----------
            yield return new AppCacheCleaner("discord", "Discord", "Cache de imagens e mídia.", CleanupGroups.Apps,
                new[]
                {
                    new FileTarget(@"%AppData%\discord\Cache"),
                    new FileTarget(@"%AppData%\discord\Code Cache"),
                    new FileTarget(@"%AppData%\discord\GPUCache"),
                },
                new[] { "Discord" });

            yield return new AppCacheCleaner("teams-classic", "Microsoft Teams (clássico)", "Cache de mídia e páginas.", CleanupGroups.Apps,
                new[]
                {
                    new FileTarget(@"%AppData%\Microsoft\Teams\Cache"),
                    new FileTarget(@"%AppData%\Microsoft\Teams\Code Cache"),
                    new FileTarget(@"%AppData%\Microsoft\Teams\GPUCache"),
                },
                new[] { "Teams" });

            yield return new AppCacheCleaner("spotify", "Spotify", "Músicas em cache para reprodução rápida (downloads offline são mantidos).", CleanupGroups.Apps,
                new[] { new FileTarget(@"%LocalAppData%\Spotify\Data") },
                new[] { "Spotify" });

            yield return new AppCacheCleaner("vscode", "Visual Studio Code", "Cache de páginas e extensões do Visual Studio Code.", CleanupGroups.Apps,
                new[]
                {
                    new FileTarget(@"%AppData%\Code\Cache"),
                    new FileTarget(@"%AppData%\Code\CachedData"),
                    new FileTarget(@"%AppData%\Code\Code Cache"),
                    new FileTarget(@"%AppData%\Code\GPUCache"),
                }, new[] { "Code" });

            yield return new AppCacheCleaner("slack", "Slack", "Cache de páginas e mídia do Slack.", CleanupGroups.Apps,
                new[]
                {
                    new FileTarget(@"%AppData%\Slack\Cache"),
                    new FileTarget(@"%AppData%\Slack\Code Cache"),
                    new FileTarget(@"%AppData%\Slack\GPUCache"),
                }, new[] { "slack" });

            yield return new AppCacheCleaner("steam", "Steam", "Cache HTML do Steam.", CleanupGroups.Apps,
                new[] { new FileTarget(@"%ProgramFiles(x86)%\Steam\htmlcache") }, new[] { "steam" });

            yield return new AppCacheCleaner("epic-games", "Epic Games Launcher", "Cache web do Epic Games Launcher.", CleanupGroups.Apps,
                new[] { new FileTarget(@"%LocalAppData%\EpicGamesLauncher\Saved\webcache") }, new[] { "EpicGamesLauncher" });

            const string shaderNote = "Shaders de jogos já compilados. Na primeira partida depois da limpeza, o jogo pode engasgar enquanto recompila.";

            yield return new FileCleaner("directx-shaders", "Cache de shaders do DirectX", shaderNote, CleanupGroups.Apps,
                new[] { new FileTarget(@"%LocalAppData%\D3DSCache") },
                CleanupRisk.Moderate, defaultSelected: false);

            // ---------- Privacidade ----------
            yield return new FileCleaner("recent-documents", "Documentos recentes",
                "Atalhos e listas de arquivos recentes do Explorador de Arquivos.",
                CleanupGroups.Privacy,
                new[]
                {
                    new FileTarget(@"%AppData%\Microsoft\Windows\Recent", "*.lnk", recursive: false),
                    new FileTarget(@"%AppData%\Microsoft\Windows\Recent\AutomaticDestinations"),
                    new FileTarget(@"%AppData%\Microsoft\Windows\Recent\CustomDestinations"),
                }, CleanupRisk.Moderate, defaultSelected: false);

            yield return new FileCleaner("nvidia-shaders", "Cache de shaders NVIDIA", shaderNote, CleanupGroups.Apps,
                new[]
                {
                    new FileTarget(@"%LocalAppData%\NVIDIA\DXCache"),
                    new FileTarget(@"%LocalAppData%\NVIDIA\GLCache"),
                    new FileTarget(@"%LocalAppData%\NVIDIA Corporation\NV_Cache"),
                },
                CleanupRisk.Moderate, defaultSelected: false);

            yield return new FileCleaner("amd-shaders", "Cache de shaders AMD", shaderNote, CleanupGroups.Apps,
                new[]
                {
                    new FileTarget(@"%LocalAppData%\AMD\DxCache"),
                    new FileTarget(@"%LocalAppData%\AMD\DxcCache"),
                    new FileTarget(@"%LocalAppData%\AMD\GLCache"),
                    new FileTarget(@"%LocalAppData%\AMD\VkCache"),
                },
                CleanupRisk.Moderate, defaultSelected: false);
        }
    }
}
