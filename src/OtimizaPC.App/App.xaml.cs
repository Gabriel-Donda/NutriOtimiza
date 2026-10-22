using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OtimizaPC.App.Services;
using OtimizaPC.App.ViewModels;
using OtimizaPC.Cleaning;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;
using OtimizaPC.Core.Updates;
using OtimizaPC.Hardware;
using OtimizaPC.Hardware.Sensors;

namespace OtimizaPC.App
{
    public partial class App : Application
    {
        private SensorService _sensors;

        private const string InstanceMutexName = @"Local\OtimizaPC.InstanciaUnica";
        private Mutex _instanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RegisterExceptionHandlers();
            UpdateService.EnableModernTls();
            Log.Info($"Iniciando {AppInfo.Name} v{AppInfo.Version} — {Environment.OSVersion} — 64 bits: {Environment.Is64BitProcess} — admin: {AppInfo.IsAdministrator}");

            // Modo sem interface, chamado pela tarefa agendada
            if (e.Args.Contains(AutoCleanup.Argument))
            {
                RunAutoCleanup();
                return;
            }

            if (!AcquireSingleInstance())
            {
                Shutdown();
                return;
            }

            var settings = new SettingsStore();
            ThemeService.Apply(settings.Load().Theme);

            _sensors = new SensorService();
            _ = _sensors.StartAsync();

            var viewModel = new MainViewModel(
                _sensors,
                new HardwareInfoService(_sensors),
                new HealthService(_sensors),
                new DialogService(),
                settings);

            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();
            _ = viewModel.CheckForUpdatesOnStartupAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _sensors?.Dispose();
            _instanceMutex?.Dispose();
            Log.Info("Aplicativo encerrado.");
            base.OnExit(e);
        }

        private void RunAutoCleanup()
        {
            try
            {
                AutoCleanup.Run(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Error("Falha na limpeza automática", ex);
            }
            Shutdown();
        }

        /// <summary>Se o app já estiver aberto, traz a janela existente para a frente em vez de abrir outra.</summary>
        private bool AcquireSingleInstance()
        {
            _instanceMutex = new Mutex(true, InstanceMutexName, out var created);
            if (created) return true;

            var current = Process.GetCurrentProcess();
            var other = Process.GetProcessesByName(current.ProcessName).FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);
            if (other != null)
            {
                NativeMethods.ShowWindow(other.MainWindowHandle, NativeMethods.SwRestore);
                NativeMethods.SetForegroundWindow(other.MainWindowHandle);
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }

        private static class NativeMethods
        {
            public const int SwRestore = 9;

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr handle, int command);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr handle);
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
