using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.Core;
using OtimizaPC.Hardware.Sensors;

namespace OtimizaPC.App.ViewModels
{
    /// <summary>Compartilha instalação e diagnóstico entre o painel e as configurações.</summary>
    public partial class SensorSetupViewModel : ObservableObject
    {
        private readonly SensorService _sensors;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(InstallPawnIoCommand), nameof(CheckSensorsCommand))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(InstallPawnIoCommand))]
        private bool _isInstalled;

        [ObservableProperty]
        private string _status;

        public SensorSetupViewModel(SensorService sensors)
        {
            _sensors = sensors;
            RefreshInstalledState();
        }

        public void RefreshInstalledState() => IsInstalled = PawnIo.IsInstalled;

        private bool CanInstall() => !IsBusy && !IsInstalled;
        private bool CanCheck() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanInstall))]
        private async Task InstallPawnIoAsync()
        {
            IsBusy = true;
            Status = "Instalando PawnIO… aceite a solicitação de administrador do Windows, se aparecer.";
            try
            {
                await PawnIo.InstallAsync();
                await RefreshSensorsAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao instalar PawnIO: " + ex.Message);
                Status = "Não foi possível concluir a instalação: " + ex.Message;
            }
            finally
            {
                RefreshInstalledState();
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanCheck))]
        private async Task CheckSensorsAsync()
        {
            IsBusy = true;
            Status = "Verificando sensores…";
            try
            {
                await RefreshSensorsAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao verificar sensores: " + ex.Message);
                Status = "Não foi possível verificar os sensores: " + ex.Message;
            }
            finally
            {
                RefreshInstalledState();
                IsBusy = false;
            }
        }

        private async Task RefreshSensorsAsync()
        {
            RefreshInstalledState();
            await _sensors.RestartAsync();
            var readings = await Task.Run(() => _sensors.Read());
            Status = SensorAvailability.Message(_sensors.State, _sensors.ErrorMessage,
                AppInfo.IsAdministrator, IsInstalled, SensorPicker.CpuTemperature(readings).HasValue)
                ?? "Temperatura da CPU disponível. Sensores atualizados.";
        }

        [RelayCommand]
        private void OpenPawnIoWebsite() => Process.Start(new ProcessStartInfo(PawnIo.Website) { UseShellExecute = true });
    }
}
