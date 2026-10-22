using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Cleaning;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;
using OtimizaPC.Core.Updates;
using OtimizaPC.Hardware;
using OtimizaPC.Hardware.Sensors;

namespace OtimizaPC.App.ViewModels
{
    public sealed class HistoryEntry
    {
        public string When { get; set; }

        public string Area { get; set; }

        public string Message { get; set; }
    }

    public partial class SettingsViewModel : ObservableObject, IActivatable
    {
        private const int HistoryLimit = 300;

        private readonly SettingsStore _store;
        private readonly IDialogService _dialogs;
        private readonly AppSettings _settings;
        private bool _suppress;

        [ObservableProperty]
        private int _themeIndex;

        [ObservableProperty]
        private bool _checkUpdatesOnStartup;

        [ObservableProperty]
        private string _updateRepository;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand), nameof(InstallUpdateCommand))]
        private bool _isUpdating;

        [ObservableProperty]
        private string _updateStatus;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasUpdate))]
        [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
        private UpdateInfo _availableUpdate;

        [ObservableProperty]
        private int _downloadProgress;

        [ObservableProperty]
        private bool _isCleanupScheduled;

        [ObservableProperty]
        private string _cleanupStatus;

        [ObservableProperty]
        private bool _isPawnIoInstalled;

        public SettingsViewModel(SettingsStore store, IDialogService dialogs)
        {
            _store = store;
            _dialogs = dialogs;
            _settings = store.Load();

            _suppress = true;
            ThemeIndex = (int)_settings.Theme;
            CheckUpdatesOnStartup = _settings.CheckUpdatesOnStartup;
            UpdateRepository = _settings.UpdateRepository;
            _suppress = false;
        }

        public IReadOnlyList<string> ThemeOptions { get; } = new[] { "Igual ao Windows", "Claro", "Escuro" };

        public ObservableCollection<HistoryEntry> History { get; } = new ObservableCollection<HistoryEntry>();

        public bool HasUpdate => AvailableUpdate != null;

        public bool IsAdministrator => AppInfo.IsAdministrator;

        public string VersionText => $"{AppInfo.Name} {AppInfo.Version} ({UpdateService.Architecture})";

        public string SystemText => Format.Join(" · ", WindowsInfo.DisplayVersion, OsInfo.IsServer ? "Windows Server" : null, WindowsInfo.DotNetFrameworkVersion);

        public string DataFolder => AppInfo.DataDirectory;

        /// <summary>Avisa a janela principal quando há atualização (para o aviso no topo).</summary>
        public event EventHandler UpdateFound;

        public void OnActivated()
        {
            IsPawnIoInstalled = PawnIo.IsInstalled;
            _ = RefreshCleanupStateAsync();
            LoadHistory();
        }

        public void OnDeactivated()
        {
        }

        // ---------- Preferências ----------

        partial void OnThemeIndexChanged(int value)
        {
            if (_suppress) return;
            _settings.Theme = (ThemePreference)value;
            _store.Save(_settings);
            ThemeService.Apply(_settings.Theme);
        }

        partial void OnCheckUpdatesOnStartupChanged(bool value)
        {
            if (_suppress) return;
            _settings.CheckUpdatesOnStartup = value;
            _store.Save(_settings);
        }

        partial void OnUpdateRepositoryChanged(string value)
        {
            if (_suppress) return;
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length > 0 && !UpdateService.IsValidRepository(trimmed))
            {
                UpdateStatus = "Use o formato dono/repositório (ex.: minhaempresa/otimizapc).";
                return;
            }
            _settings.UpdateRepository = trimmed;
            _store.Save(_settings);
            UpdateStatus = null;
        }

        // ---------- Atualizações ----------

        /// <summary>Verificação silenciosa na abertura do app: só avisa se houver versão nova.</summary>
        public async Task CheckOnStartupAsync()
        {
            if (!_settings.CheckUpdatesOnStartup || !UpdateService.IsValidRepository(_settings.UpdateRepository)) return;
            await CheckAsync(silent: true);
        }

        private bool CanCheck() => !IsUpdating;

        [RelayCommand(CanExecute = nameof(CanCheck))]
        private Task CheckUpdatesAsync() => CheckAsync(silent: false);

        private async Task CheckAsync(bool silent)
        {
            if (!UpdateService.IsValidRepository(_settings.UpdateRepository))
            {
                if (!silent) UpdateStatus = "Informe o repositório do GitHub onde as versões são publicadas.";
                return;
            }

            IsUpdating = true;
            if (!silent) UpdateStatus = "Procurando atualizações…";
            try
            {
                AvailableUpdate = await UpdateService.CheckAsync(_settings.UpdateRepository, CancellationToken.None);
                _settings.LastUpdateCheck = DateTime.Now;
                _store.Save(_settings);

                if (AvailableUpdate != null)
                {
                    UpdateStatus = $"Versão {AvailableUpdate.Version.ToString(3)} disponível.";
                    UpdateFound?.Invoke(this, EventArgs.Empty);
                }
                else if (!silent)
                {
                    UpdateStatus = "Você já está na versão mais recente.";
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao verificar atualizações: " + ex.Message);
                if (!silent) UpdateStatus = "Não foi possível verificar: " + ex.Message;
            }
            finally
            {
                IsUpdating = false;
            }
        }

        private bool CanInstall() => HasUpdate && !IsUpdating;

        [RelayCommand(CanExecute = nameof(CanInstall))]
        private async Task InstallUpdateAsync()
        {
            var update = AvailableUpdate;
            if (!await _dialogs.ConfirmAsync("Atualizar o OtimizaPC",
                    $"Baixar e instalar a versão {update.Version.ToString(3)}? O OtimizaPC será fechado e reaberto ao final.", "Atualizar"))
                return;

            IsUpdating = true;
            DownloadProgress = 0;
            UpdateStatus = "Baixando…";
            try
            {
                var progress = new Progress<int>(p =>
                {
                    DownloadProgress = p;
                    UpdateStatus = $"Baixando… {p}%";
                });
                var installer = await UpdateService.DownloadAsync(update, progress, CancellationToken.None);
                UpdateStatus = "Instalando…";
                UpdateService.LaunchInstaller(installer);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao atualizar", ex);
                UpdateStatus = "Não foi possível atualizar: " + ex.Message;
                IsUpdating = false;
            }
        }

        [RelayCommand]
        private void OpenReleasePage()
        {
            if (AvailableUpdate?.PageUrl != null) Process.Start(AvailableUpdate.PageUrl);
        }

        // ---------- Limpeza automática ----------

        private async Task RefreshCleanupStateAsync()
        {
            var enabled = await Task.Run(() => ScheduledCleanup.IsEnabled());
            _suppress = true;
            IsCleanupScheduled = enabled;
            _suppress = false;
            CleanupStatus = enabled
                ? "Toda semana (domingo ao meio-dia, ou assim que o PC ligar), só com os itens seguros e recomendados."
                : IsAdministrator ? null : "Agendar requer executar como administrador.";
        }

        partial void OnIsCleanupScheduledChanged(bool value)
        {
            if (_suppress) return;
            _ = ApplyCleanupScheduleAsync(value);
        }

        private async Task ApplyCleanupScheduleAsync(bool enable)
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                await Task.Run(() =>
                {
                    if (enable) ScheduledCleanup.Enable(exe);
                    else ScheduledCleanup.Disable();
                });
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao alterar a limpeza automática", ex);
                CleanupStatus = ex.Message;
            }
            await RefreshCleanupStateAsync();
            LoadHistory();
        }

        // ---------- Sensores ----------

        [RelayCommand]
        private void InstallPawnIo()
        {
            try
            {
                PawnIo.StartInstall();
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível iniciar a instalação do PawnIO: " + ex.Message);
            }
        }

        // ---------- Histórico ----------

        [RelayCommand]
        private void LoadHistory()
        {
            History.Clear();
            try
            {
                if (!File.Exists(Core.History.FilePath)) return;
                foreach (var line in File.ReadAllLines(Core.History.FilePath).Reverse().Take(HistoryLimit))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    History.Add(new HistoryEntry
                    {
                        When = DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
                            ? when.ToString("dd/MM/yyyy HH:mm")
                            : parts[0],
                        Area = parts[1],
                        Message = string.Join("\t", parts.Skip(2)),
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao ler o histórico: " + ex.Message);
            }
        }

        [RelayCommand]
        private void OpenDataFolder() => Process.Start("explorer.exe", $"\"{AppInfo.DataDirectory}\"");
    }
}
