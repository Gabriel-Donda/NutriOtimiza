using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OtimizaPC.Core;
using OtimizaPC.Hardware;
using OtimizaPC.Hardware.Sensors;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    /// <summary>Página que precisa saber quando entra/sai da tela (para ligar/desligar timers).</summary>
    public interface IActivatable
    {
        void OnActivated();

        void OnDeactivated();
    }

    public sealed class NavItem
    {
        public NavItem(string title, SymbolRegular icon, object page)
        {
            Title = title;
            Icon = icon;
            Page = page;
        }

        public string Title { get; }

        public SymbolRegular Icon { get; }

        public object Page { get; }
    }

    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private NavItem _selectedItem;

        public MainViewModel(SensorService sensors, HardwareInfoService hardwareInfo, HealthService health)
        {
            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem("Painel", SymbolRegular.Home24, new DashboardViewModel(sensors, hardwareInfo, health)),
                new NavItem("Hardware", SymbolRegular.DeveloperBoard24, new HardwareViewModel(hardwareInfo)),
                new NavItem("Sensores", SymbolRegular.Temperature24, new SensorsViewModel(sensors)),
                new NavItem("Limpeza", SymbolRegular.Broom24, new ComingSoonViewModel(
                    "Limpeza", SymbolRegular.Broom24, 2,
                    "Libere espaço com segurança, sempre com prévia do que será apagado.",
                    "Temporários do usuário e do Windows",
                    "Cache do Windows Update e Delivery Optimization",
                    "Cache de navegadores (Chrome, Edge, Firefox, Brave)",
                    "Miniaturas, logs, dumps de erro e lixeira",
                    "Limpeza de componentes do Windows (DISM)",
                    "Analisador de espaço: maiores pastas e arquivos")),
                new NavItem("Otimizações", SymbolRegular.Flash24, new ComingSoonViewModel(
                    "Otimizações", SymbolRegular.Flash24, 3,
                    "Ajustes curados, com nível de risco, ponto de restauração e botão de desfazer.",
                    "Programas e tarefas na inicialização",
                    "Serviços com perfis (Gamer, Escritório, Notebook)",
                    "Plano de energia e efeitos visuais",
                    "Privacidade e telemetria",
                    "Remoção de bloatware",
                    "Ajustes de jogos e de rede")),
                new NavItem("Reparo", SymbolRegular.WrenchScrewdriver24, new ComingSoonViewModel(
                    "Reparo", SymbolRegular.WrenchScrewdriver24, 4,
                    "Ferramentas de diagnóstico e reparo do técnico em um só lugar.",
                    "SFC, DISM e CHKDSK com saída ao vivo",
                    "Reset do Windows Update e da pilha de rede",
                    "Erros críticos do Visualizador de Eventos",
                    "Análise de telas azuis (minidumps)",
                    "Lista de drivers com data e status",
                    "Ponto de restauração com um clique")),
                new NavItem("Configurações", SymbolRegular.Settings24, new ComingSoonViewModel(
                    "Configurações", SymbolRegular.Settings24, 5,
                    "Preferências do aplicativo.",
                    "Agendamento de limpeza automática",
                    "Histórico de alterações com desfazer",
                    "Tema claro/escuro e alertas de temperatura",
                    "Atualização automática")),
            };
            SelectedItem = NavItems[0];
        }

        public ObservableCollection<NavItem> NavItems { get; }

        public string VersionText => $"{AppInfo.Name} v{AppInfo.Version}";

        public bool IsAdministrator => AppInfo.IsAdministrator;

        public string PrivilegeText => IsAdministrator ? "Administrador" : "Sem privilégios de administrador";

        partial void OnSelectedItemChanged(NavItem oldValue, NavItem newValue)
        {
            (oldValue?.Page as IActivatable)?.OnDeactivated();
            (newValue?.Page as IActivatable)?.OnActivated();
        }
    }
}
