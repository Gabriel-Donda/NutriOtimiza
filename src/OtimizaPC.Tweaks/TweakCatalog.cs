using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Tweaks.Registry;
using OtimizaPC.Tweaks.SystemTools;
using static OtimizaPC.Tweaks.Registry.RegistryChange;

namespace OtimizaPC.Tweaks
{
    /// <summary>
    /// Otimizações curadas. Ficam de fora, de propósito, os "tweaks" que são placebo ou nocivos:
    /// desativar SysMain/Superfetch, desativar o arquivo de paginação, desativar o Defender ou o Windows Update,
    /// "liberar RAM", NetworkThrottlingIndex, limpeza de registro.
    /// </summary>
    public static class TweakCatalog
    {
        private const RegistryHive HKCU = RegistryHive.CurrentUser;
        private const RegistryHive HKLM = RegistryHive.LocalMachine;

        private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private const string GamesTask = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
        private const string ClassicMenuKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

        /// <summary>Otimizações suportadas por esta versão/edição do Windows.</summary>
        public static IReadOnlyList<Tweak> Create() => All().Where(t => t.IsSupported).ToList();

        private static RegistryTweak Reg(string id, string name, string description, string category, params RegistryChange[] changes)
        {
            var tweak = new RegistryTweak(id, name, description, category);
            tweak.Changes.AddRange(changes);
            return tweak;
        }

        private static IEnumerable<Tweak> All()
        {
            // ---------- Energia e desempenho ----------
            yield return new PowerPlanTweak();
            yield return new HibernationTweak();

            yield return Reg("startup-delay", "Remover atraso dos programas de inicialização",
                "O Windows espera cerca de 10 segundos após o login antes de abrir os programas da inicialização. Isso remove a espera.",
                TweakCategories.Performance,
                Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0));

            var fastStartup = Reg("fast-startup-off", "Desativar Inicialização Rápida",
                "Faz o \"Desligar\" desligar de verdade: resolve problemas de drivers, dual boot e o tempo ligado que nunca zera. O boot fica alguns segundos mais lento.",
                TweakCategories.Performance,
                Dword(HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, 1));
            fastStartup.ClientOnly = true;
            yield return fastStartup;

            yield return new RegistryTweak("faster-app-close", "Encerrar programas travados mais rápido",
                "Ao desligar ou sair da conta, o Windows espera menos (2 s em vez de 5 s) por programas que não respondem e os fecha sozinho.",
                TweakCategories.Performance)
            {
                Restart = RestartRequirement.Logoff,
                Changes =
                {
                    String(HKCU, @"Control Panel\Desktop", "AutoEndTasks", "1", "0"),
                    String(HKCU, @"Control Panel\Desktop", "HungAppTimeout", "2000", "5000"),
                    String(HKCU, @"Control Panel\Desktop", "WaitToKillAppTimeout", "2000", "5000"),
                },
            };

            yield return new RegistryTweak("faster-service-stop", "Desligar mais rápido (tempo dos serviços)",
                "Reduz de 5 s para 2 s a espera por serviços que demoram a parar no desligamento. Um serviço lento pode ser interrompido antes de salvar seus dados.",
                TweakCategories.Performance)
            {
                Risk = TweakRisk.Moderate,
                Restart = RestartRequirement.Reboot,
                Changes = { String(HKLM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", "5000") },
            };

            yield return new RegistryTweak("menu-delay", "Menus mais rápidos",
                "Remove o atraso de 400 ms para abrir submenus. A interface parece mais ágil.",
                TweakCategories.Performance)
            {
                Restart = RestartRequirement.Logoff,
                Changes = { String(HKCU, @"Control Panel\Desktop", "MenuShowDelay", "0", "400") },
            };

            yield return new RegistryTweak("bsod-no-reboot", "Mostrar a tela azul sem reiniciar sozinho",
                "Em vez de reiniciar na hora, o PC fica parado na tela azul para você anotar o código do erro. Útil para diagnosticar travamentos.",
                TweakCategories.Performance)
            {
                Restart = RestartRequirement.Reboot,
                Changes = { Dword(HKLM, @"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", 0, 1) },
            };

            // ---------- Privacidade ----------
            var telemetry =Reg("telemetry-minimum", "Reduzir telemetria ao mínimo",
                "Envia à Microsoft apenas os dados de diagnóstico obrigatórios para sua edição (no Home e Pro, o nível \"Obrigatório\").",
                TweakCategories.Privacy,
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0));
            telemetry.Recommended = true;
            telemetry.MinBuild = OsInfo.Windows10;
            yield return telemetry;

            var diagTrack = new ServiceTweak("diagtrack-service", "Desativar serviço de telemetria (DiagTrack)",
                "Serviço \"Experiências do Usuário Conectado e Telemetria\", que coleta e envia dados de uso.",
                TweakCategories.Privacy, "DiagTrack", ServiceTweak.Disabled, ServiceTweak.Automatic)
            { Recommended = true, MinBuild = OsInfo.Windows10 };
            yield return diagTrack;

            var adId = Reg("advertising-id", "Desativar ID de publicidade",
                "Impede que apps usem um identificador seu para mostrar anúncios personalizados.",
                TweakCategories.Privacy,
                Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1));
            adId.Recommended = true;
            adId.MinBuild = OsInfo.Windows81;
            yield return adId;

            var tailored = Reg("tailored-experiences", "Desativar experiências personalizadas",
                "Impede que a Microsoft use seus dados de diagnóstico para dicas e anúncios personalizados.",
                TweakCategories.Privacy,
                Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1));
            tailored.Recommended = true;
            tailored.MinBuild = OsInfo.Windows10_1703;
            yield return tailored;

            var activity = Reg("activity-history", "Desativar histórico de atividades",
                "Para de registrar e enviar quais apps, arquivos e sites você abre (linha do tempo).",
                TweakCategories.Privacy,
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0));
            activity.Recommended = true;
            activity.MinBuild = OsInfo.Windows10_1803;
            yield return activity;

            var feedback = Reg("feedback-prompts", "Desativar pedidos de feedback",
                "Para as janelas do Windows perguntando sua opinião.",
                TweakCategories.Privacy,
                Dword(HKCU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0));
            feedback.Recommended = true;
            feedback.MinBuild = OsInfo.Windows10;
            yield return feedback;

            var recall = Reg("recall-off", "Desativar Recall",
                "Impede que o Windows tire capturas periódicas da tela para a busca com IA.",
                TweakCategories.Privacy,
                Dword(HKCU, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            recall.Recommended = true;
            recall.MinBuild = OsInfo.Windows11_24H2;
            yield return recall;

            var copilot = Reg("copilot-off", "Desativar Copilot",
                "Remove o botão e o atalho do Copilot da barra de tarefas.",
                TweakCategories.Privacy,
                Dword(HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
                Dword(HKCU, ExplorerAdvanced, "ShowCopilotButton", 0, 1));
            copilot.MinBuild = OsInfo.Windows11_22H2;
            copilot.Restart = RestartRequirement.Explorer;
            yield return copilot;

            var trackProgs = Reg("track-launched-apps", "Não rastrear os programas que você abre",
                "O Windows deixa de contar quais programas você mais usa (para sugestões no Iniciar e estatísticas).",
                TweakCategories.Privacy,
                Dword(HKCU, ExplorerAdvanced, "Start_TrackProgs", 0, 1));
            trackProgs.Recommended = true;
            trackProgs.MinBuild = OsInfo.Windows10;
            yield return trackProgs;

            var recentFiles = Reg("recent-files-off", "Não mostrar arquivos recentes no Explorador",
                "O Acesso Rápido deixa de listar os últimos arquivos e as pastas mais usadas.",
                TweakCategories.Privacy,
                Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, 1),
                Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0, 1));
            recentFiles.MinBuild = OsInfo.Windows10;
            recentFiles.Restart = RestartRequirement.Explorer;
            yield return recentFiles;

            var langOptOut = Reg("language-list-optout", "Não compartilhar a lista de idiomas com sites",
                "Impede que sites leiam os idiomas instalados no Windows para personalizar conteúdo.",
                TweakCategories.Privacy,
                Dword(HKCU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, 0));
            langOptOut.MinBuild = OsInfo.Windows10;
            yield return langOptOut;

            yield return new RegistryTweak("wer-off", "Desativar envio de relatórios de erro",
                "O Windows para de oferecer e enviar relatórios de falha à Microsoft. Os eventos continuam nos logs da aba Reparo.",
                TweakCategories.Privacy)
            {
                Risk = TweakRisk.Moderate,
                Changes = { Dword(HKLM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, 0) },
            };

            // ---------- Anúncios e sugestões ----------
            var suggestions = Reg("windows-suggestions", "Desativar dicas, sugestões e anúncios",
                "Remove sugestões de apps no Iniciar, dicas do Windows e propagandas nas Configurações.",
                TweakCategories.Ads,
                Dword(HKCU, ContentDelivery, "SystemPaneSuggestionsEnabled", 0, 1),
                Dword(HKCU, ContentDelivery, "SubscribedContent-338388Enabled", 0, 1),
                Dword(HKCU, ContentDelivery, "SubscribedContent-338389Enabled", 0, 1),
                Dword(HKCU, ContentDelivery, "SubscribedContent-353698Enabled", 0, 1),
                Dword(HKCU, ContentDelivery, "SoftLandingEnabled", 0, 1));
            suggestions.Recommended = true;
            suggestions.MinBuild = OsInfo.Windows10;
            yield return suggestions;

            var silentApps = Reg("silent-app-installs", "Impedir instalação automática de apps sugeridos",
                "Evita que o Windows instale sozinho jogos e apps promovidos (como Candy Crush).",
                TweakCategories.Ads,
                Dword(HKCU, ContentDelivery, "SilentInstalledAppsEnabled", 0, 1),
                Dword(HKCU, ContentDelivery, "OemPreInstalledAppsEnabled", 0, 1),
                Dword(HKCU, ContentDelivery, "PreInstalledAppsEnabled", 0, 1),
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1));
            silentApps.Recommended = true;
            silentApps.MinBuild = OsInfo.Windows10;
            yield return silentApps;

            var lockScreen = Reg("lockscreen-ads", "Desativar curiosidades na tela de bloqueio",
                "Remove textos promocionais e \"curiosidades\" da tela de bloqueio.",
                TweakCategories.Ads,
                Dword(HKCU, ContentDelivery, "RotatingLockScreenOverlayEnabled", 0, 1),
                Dword(HKCU, ContentDelivery, "SubscribedContent-338387Enabled", 0, 1));
            lockScreen.Recommended = true;
            lockScreen.MinBuild = OsInfo.Windows10;
            yield return lockScreen;

            var bing = Reg("bing-search", "Remover resultados da web na pesquisa do Iniciar",
                "A pesquisa do Iniciar passa a mostrar só apps, arquivos e configurações do PC. Também deixa a pesquisa mais rápida.",
                TweakCategories.Ads,
                Dword(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
                Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, 1));
            bing.Recommended = true;
            bing.MinBuild = OsInfo.Windows10;
            bing.Restart = RestartRequirement.Explorer;
            yield return bing;

            var widgets = Reg("widgets-off", "Desativar Widgets",
                "Remove o painel de notícias e clima da barra de tarefas.",
                TweakCategories.Ads,
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0));
            widgets.MinBuild = OsInfo.Windows11;
            widgets.Restart = RestartRequirement.Explorer;
            yield return widgets;

            var news = Reg("news-interests-off", "Desativar Notícias e Interesses",
                "Remove o widget de notícias e clima da barra de tarefas do Windows 10.",
                TweakCategories.Ads,
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0));
            news.MinBuild = OsInfo.Windows10_2004;
            news.MaxBuild = OsInfo.Windows11 - 1;
            news.Restart = RestartRequirement.Explorer;
            yield return news;

            var syncNotifications = Reg("sync-provider-ads", "Remover propaganda do OneDrive no Explorador",
                "Esconde as notificações e ofertas de provedores de nuvem (OneDrive, Microsoft 365) que aparecem no Explorador.",
                TweakCategories.Ads,
                Dword(HKCU, ExplorerAdvanced, "ShowSyncProviderNotifications", 0, 1));
            syncNotifications.Recommended = true;
            syncNotifications.MinBuild = OsInfo.Windows10;
            syncNotifications.Restart = RestartRequirement.Explorer;
            yield return syncNotifications;

            // ---------- Explorador e interface ----------
            var extensions = Reg("show-extensions", "Mostrar extensões de arquivos",
                "Exibe .exe, .pdf, .docx etc. Ajuda a identificar arquivos maliciosos disfarçados (ex.: foto.jpg.exe).",
                TweakCategories.Interface,
                Dword(HKCU, ExplorerAdvanced, "HideFileExt", 0, 1));
            extensions.Recommended = true;
            extensions.Restart = RestartRequirement.Explorer;
            yield return extensions;

            yield return new RegistryTweak("show-hidden", "Mostrar arquivos ocultos",
                "Exibe arquivos e pastas ocultos no Explorador (ex.: AppData).",
                TweakCategories.Interface)
            {
                Restart = RestartRequirement.Explorer,
                Changes = { Dword(HKCU, ExplorerAdvanced, "Hidden", 1, 2) },
            };

            yield return new RegistryTweak("explorer-this-pc", "Abrir o Explorador em \"Este Computador\"",
                "O Explorador abre mostrando as unidades em vez do Acesso Rápido.",
                TweakCategories.Interface)
            {
                MinBuild = OsInfo.Windows10,
                Changes = { Dword(HKCU, ExplorerAdvanced, "LaunchTo", 1) },
            };

            yield return new RegistryTweak("classic-context-menu", "Menu de contexto clássico",
                "Volta o menu do botão direito completo do Windows 10, sem precisar clicar em \"Mostrar mais opções\".",
                TweakCategories.Interface)
            {
                MinBuild = OsInfo.Windows11,
                Restart = RestartRequirement.Explorer,
                Changes = { new RegistryChange(HKCU, ClassicMenuKey + @"\InprocServer32", string.Empty, string.Empty, RegistryValueKind.String) { DefaultDeleteKey = ClassicMenuKey } },
            };

            yield return new RegistryTweak("transparency-off", "Desativar transparência",
                "Remove o efeito translúcido de janelas e barra de tarefas. Ajuda em PCs com vídeo integrado fraco.",
                TweakCategories.Interface)
            {
                MinBuild = OsInfo.Windows10,
                Changes = { Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1) },
            };

            yield return new RegistryTweak("animations-off", "Desativar animações de janelas",
                "Janelas abrem e minimizam instantaneamente. Deixa PCs fracos mais ágeis.",
                TweakCategories.Interface)
            {
                Restart = RestartRequirement.Logoff,
                Changes =
                {
                    String(HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1"),
                    Dword(HKCU, ExplorerAdvanced, "TaskbarAnimations", 0, 1),
                },
            };

            yield return new RegistryTweak("task-view-off", "Esconder o botão Visão de Tarefas",
                "Remove da barra de tarefas o botão de áreas de trabalho virtuais. O atalho Win+Tab continua funcionando.",
                TweakCategories.Interface)
            {
                MinBuild = OsInfo.Windows10,
                Restart = RestartRequirement.Explorer,
                Changes = { Dword(HKCU, ExplorerAdvanced, "ShowTaskViewButton", 0, 1) },
            };

            yield return new RegistryTweak("taskbar-chat-off", "Esconder o botão de Chat (Teams)",
                "Remove o ícone de bate-papo da barra de tarefas do Windows 11.",
                TweakCategories.Interface)
            {
                MinBuild = OsInfo.Windows11,
                Restart = RestartRequirement.Explorer,
                Changes = { Dword(HKCU, ExplorerAdvanced, "TaskbarMn", 0, 1) },
            };

            yield return new RegistryTweak("taskbar-left", "Barra de tarefas alinhada à esquerda",
                "Volta o botão Iniciar e os ícones para o canto esquerdo, como no Windows 10.",
                TweakCategories.Interface)
            {
                MinBuild = OsInfo.Windows11,
                Restart = RestartRequirement.Explorer,
                Changes = { Dword(HKCU, ExplorerAdvanced, "TaskbarAl", 0, 1) },
            };

            yield return new RegistryTweak("explorer-compact", "Explorador compacto",
                "Reduz o espaço entre os itens das listas do Explorador, mostrando mais arquivos de uma vez.",
                TweakCategories.Interface)
            {
                MinBuild = OsInfo.Windows11,
                Restart = RestartRequirement.Explorer,
                Changes = { Dword(HKCU, ExplorerAdvanced, "UseCompactMode", 1, 0) },
            };

            // ---------- Jogos ----------
            yield return new RegistryTweak("game-mode", "Ativar Modo de Jogo",
                "O Windows prioriza o jogo aberto e pausa atualizações em segundo plano durante a partida.",
                TweakCategories.Gaming)
            {
                Recommended = true,
                ClientOnly = true,
                MinBuild = OsInfo.Windows10_1703,
                Changes =
                {
                    Dword(HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
                    Dword(HKCU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1),
                },
            };

            yield return new RegistryTweak("game-dvr-off", "Desativar gravação em segundo plano (Game DVR)",
                "Para a gravação contínua dos últimos minutos de jogo, que consome GPU e disco. A Xbox Game Bar continua funcionando.",
                TweakCategories.Gaming)
            {
                Recommended = true,
                ClientOnly = true,
                MinBuild = OsInfo.Windows10,
                Changes =
                {
                    Dword(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0, 1),
                    Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
                },
            };

            yield return new RegistryTweak("hags", "Agendamento de GPU acelerado por hardware",
                "A placa de vídeo gerencia a própria memória, reduzindo latência. Exige GPU compatível (NVIDIA GTX 10xx+, AMD RX 5000+, Intel Arc). Em alguns jogos e gravadores pode causar instabilidade.",
                TweakCategories.Gaming)
            {
                Risk = TweakRisk.Moderate,
                ClientOnly = true,
                MinBuild = OsInfo.Windows10_2004,
                Restart = RestartRequirement.Reboot,
                Changes = { Dword(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1) },
            };

            yield return new RegistryTweak("game-priority", "Prioridade de CPU para jogos",
                "Aumenta a prioridade de jogos no agendador multimídia do Windows. O efeito é pequeno e depende do jogo.",
                TweakCategories.Gaming)
            {
                ClientOnly = true,
                Changes =
                {
                    Dword(HKLM, GamesTask, "Priority", 6, 2),
                    String(HKLM, GamesTask, "Scheduling Category", "High", "Medium"),
                    String(HKLM, GamesTask, "SFIO Priority", "High", "Normal"),
                },
            };

            yield return new RegistryTweak("game-bar-off", "Desativar a Xbox Game Bar",
                "Impede que o painel da Game Bar abra com o botão do controle ou Win+G e consuma recursos durante o jogo.",
                TweakCategories.Gaming)
            {
                ClientOnly = true,
                MinBuild = OsInfo.Windows10,
                Changes =
                {
                    Dword(HKCU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, 1),
                    Dword(HKCU, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0, 1),
                },
            };

            yield return new RegistryTweak("mouse-precision-off", "Desativar aceleração do mouse",
                "Desliga \"Aumentar precisão do ponteiro\": o cursor se move sempre na mesma proporção da mão. Preferido em jogos de tiro.",
                TweakCategories.Gaming)
            {
                Restart = RestartRequirement.Logoff,
                Changes =
                {
                    String(HKCU, @"Control Panel\Mouse", "MouseSpeed", "0", "1"),
                    String(HKCU, @"Control Panel\Mouse", "MouseThreshold1", "0", "6"),
                    String(HKCU, @"Control Panel\Mouse", "MouseThreshold2", "0", "10"),
                },
            };

            yield return new RegistryTweak("sticky-keys-prompt", "Desativar atalho das Teclas de Aderência",
                "Para de abrir o aviso das Teclas de Aderência ao apertar Shift 5 vezes (comum no meio de jogos).",
                TweakCategories.Gaming)
            {
                Restart = RestartRequirement.Logoff,
                Changes = { String(HKCU, @"Control Panel\Accessibility\StickyKeys", "Flags", "506", "510") },
            };

            // ---------- Atualizações e rede ----------
            var noReboot = Reg("no-auto-reboot", "Não reiniciar sozinho com usuário logado",
                "Impede que o Windows Update reinicie o PC por conta própria enquanto alguém está usando.",
                TweakCategories.Updates,
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1));
            noReboot.Recommended = true;
            yield return noReboot;

            var p2p = Reg("delivery-p2p-off", "Desativar compartilhamento de atualizações",
                "Impede que seu PC envie pedaços de atualizações para outros PCs (economiza upload da internet).",
                TweakCategories.Updates,
                Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0));
            p2p.Recommended = true;
            p2p.MinBuild = OsInfo.Windows10;
            yield return p2p;

            yield return new RegistryTweak("exclude-driver-updates", "Não instalar drivers pelo Windows Update",
                "Útil quando o Windows troca um driver de vídeo ou áudio que você instalou manualmente. Você passa a atualizar drivers por conta própria.",
                TweakCategories.Updates)
            {
                Risk = TweakRisk.Moderate,
                MinBuild = OsInfo.Windows10,
                Changes = { Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1) },
            };

            // ---------- Serviços ----------
            yield return new ServiceTweak("search-indexing-off", "Desativar indexação da pesquisa",
                "Recomendado só para PCs com HD lento. A pesquisa no Iniciar, no Explorador e no Outlook fica mais lenta.",
                TweakCategories.Services, "WSearch", ServiceTweak.Disabled, ServiceTweak.Automatic)
            { Risk = TweakRisk.Moderate };

            yield return new ServiceTweak("maps-service-off", "Desativar serviço de mapas baixados",
                "Gerenciador de mapas offline do app Mapas. Desnecessário se você não usa mapas offline.",
                TweakCategories.Services, "MapsBroker", ServiceTweak.Disabled, ServiceTweak.Automatic)
            { ClientOnly = true };

            yield return new ServiceTweak("wmp-sharing-off", "Desativar compartilhamento do Media Player",
                "Serviço que compartilha músicas e vídeos do Windows Media Player com outros aparelhos da rede.",
                TweakCategories.Services, "WMPNetworkSvc", ServiceTweak.Disabled, ServiceTweak.Manual);

            yield return new ServiceTweak("fax-off", "Desativar serviço de Fax",
                "Só é útil se você ainda envia fax pelo computador.",
                TweakCategories.Services, "Fax", ServiceTweak.Disabled, ServiceTweak.Manual);

            yield return new ServiceTweak("print-spooler-off", "Desativar spooler de impressão",
                "Só se você não usa impressora nem \"Imprimir em PDF\". Fecha brechas de segurança conhecidas do spooler.",
                TweakCategories.Services, "Spooler", ServiceTweak.Disabled, ServiceTweak.Automatic)
            { Risk = TweakRisk.Advanced };

            // ---------- Segurança ----------
            yield return new RegistryTweak("remote-assistance-off", "Desativar Assistência Remota",
                "Impede convites de Assistência Remota do Windows, usados em golpes de \"suporte técnico\". Não afeta a Área de Trabalho Remota.",
                TweakCategories.Security)
            {
                Changes = { Dword(HKLM, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, 1) },
            };

            yield return new RegistryTweak("autorun-off", "Desativar execução automática de pendrives",
                "Impede que programas em pendrives e discos externos rodem sozinhos ao conectar. Bloqueia uma via comum de vírus.",
                TweakCategories.Security)
            {
                Recommended = true,
                Changes =
                {
                    Dword(HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255),
                    Dword(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", 1, 0),
                },
            };

            yield return new RegistryTweak("llmnr-off", "Desativar LLMNR",
                "Protocolo de resolução de nomes que atacantes na rede local usam para roubar credenciais. Redes sem DNS interno podem deixar de achar computadores pelo nome.",
                TweakCategories.Security)
            {
                Risk = TweakRisk.Moderate,
                Changes = { Dword(HKLM, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0) },
            };

            yield return new RegistryTweak("smb1-off", "Desativar compartilhamento SMBv1",
                "Protocolo antigo explorado pelo WannaCry. Pode quebrar acesso a NAS, impressoras e scanners muito antigos.",
                TweakCategories.Security)
            {
                Risk = TweakRisk.Moderate,
                Restart = RestartRequirement.Reboot,
                Changes = { Dword(HKLM, @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", 0, 1) },
            };
        }
    }
}
