using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OtimizaPC.App.ViewModels;
using OtimizaPC.Core;
using OtimizaPC.Hardware;
using OtimizaPC.Hardware.Sensors;
using Wpf.Ui.Appearance;

namespace OtimizaPC.App
{
    public partial class App : Application
    {
        private SensorService _sensors;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RegisterExceptionHandlers();
            Log.Info($"Iniciando {AppInfo.Name} v{AppInfo.Version} — {Environment.OSVersion} — 64 bits: {Environment.Is64BitProcess} — admin: {AppInfo.IsAdministrator}");

            try
            {
                ApplicationThemeManager.ApplySystemTheme();
            }
            catch (Exception ex)
            {
                // Windows 8.1 / Server 2012 R2 não têm tema do sistema: fica o escuro padrão
                Log.Warn("Não foi possível aplicar o tema do sistema: " + ex.Message);
            }

            _sensors = new SensorService();
            _ = _sensors.StartAsync();

            var viewModel = new MainViewModel(
                _sensors,
                new HardwareInfoService(_sensors),
                new HealthService(_sensors));

            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _sensors?.Dispose();
            Log.Info("Aplicativo encerrado.");
            base.OnExit(e);
        }

        private void RegisterExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log.Error("Exceção não tratada (AppDomain)", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log.Error("Exceção não observada em Task", args.Exception);
                args.SetObserved();
            };
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Exceção não tratada na interface", e.Exception);
            MessageBox.Show(
                "Ocorreu um erro inesperado:\n\n" + e.Exception.Message + "\n\nDetalhes foram salvos no log.",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
