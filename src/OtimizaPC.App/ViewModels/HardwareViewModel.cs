using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Core.Reports;
using OtimizaPC.Core.Settings;
using OtimizaPC.Hardware;

namespace OtimizaPC.App.ViewModels
{
    /// <summary>Estado visual de uma seção do relatório de hardware.</summary>
    public partial class HardwareSectionViewModel : ObservableObject
    {
        private readonly InfoSection _section;

        public HardwareSectionViewModel(InfoSection section, bool isExpanded)
        {
            _section = section;
            IsExpanded = isExpanded;
        }

        public string SectionId => GetSectionId(_section.Title);
        public string Title => _section.Title;
        public string Icon => _section.Icon;
        public IReadOnlyList<InfoGroup> Groups => _section.Groups;
        public string Summary => _section.Groups
            .SelectMany(group => new[] { group.Header }.Concat(group.Entries.Select(entry => entry.Value)))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Sem detalhes";

        [ObservableProperty]
        private bool _isExpanded;

        public static string GetSectionId(string title)
        {
            switch (title)
            {
                case "Sistema": return "system";
                case "Processador": return "cpu";
                case "Placa-mãe e BIOS": return "motherboard";
                case "Memória RAM": return "memory";
                case "Placa de vídeo e monitores": return "gpu";
                case "Armazenamento": return "storage";
                case "Rede": return "network";
                case "Bateria": return "battery";
                default: return (title ?? string.Empty).Trim().ToLowerInvariant();
            }
        }
    }

    public partial class HardwareViewModel : ObservableObject, IActivatable
    {
        private readonly HardwareInfoService _hardwareInfo;
        private readonly SettingsStore _store;
        private readonly AppSettings _settings;
        private HardwareReport _report;
        private bool _changingExpansion;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ExportHtmlCommand), nameof(CopyTextCommand))]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage;

        public HardwareViewModel(HardwareInfoService hardwareInfo, SettingsStore store)
        {
            _hardwareInfo = hardwareInfo;
            _store = store;
            _settings = _store.Load();
            if (_settings.CollapsedHardwareSections == null)
                _settings.CollapsedHardwareSections = new List<string>();
        }

        public ObservableCollection<HardwareSectionViewModel> Sections { get; } = new ObservableCollection<HardwareSectionViewModel>();

        public void OnActivated()
        {
            if (_report == null && !IsLoading) _ = RefreshAsync();
        }

        public void OnDeactivated()
        {
        }

        private bool CanRefresh() => !IsLoading;

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
        {
            IsLoading = true;
            StatusMessage = "Coletando informações do hardware…";
            try
            {
                _report = await _hardwareInfo.CollectAsync();
                var previousStates = Sections.ToDictionary(section => section.SectionId, section => section.IsExpanded);
                foreach (var section in Sections)
                    section.PropertyChanged -= OnSectionPropertyChanged;
                Sections.Clear();
                foreach (var section in _report.Sections)
                {
                    var sectionId = HardwareSectionViewModel.GetSectionId(section.Title);
                    var isExpanded = previousStates.TryGetValue(sectionId, out var previous)
                        ? previous
                        : !_settings.CollapsedHardwareSections.Contains(sectionId);
                    var viewModel = new HardwareSectionViewModel(section, isExpanded);
                    viewModel.PropertyChanged += OnSectionPropertyChanged;
                    Sections.Add(viewModel);
                }
                StatusMessage = $"Atualizado às {_report.GeneratedAt:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao coletar hardware", ex);
                StatusMessage = "Erro ao coletar informações: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ToggleSection(HardwareSectionViewModel section)
        {
            if (section != null) section.IsExpanded = !section.IsExpanded;
        }

        [RelayCommand]
        private void CollapseAll() => SetAllSectionsExpanded(false);

        [RelayCommand]
        private void ExpandAll() => SetAllSectionsExpanded(true);

        private void SetAllSectionsExpanded(bool isExpanded)
        {
            if (Sections.All(section => section.IsExpanded == isExpanded)) return;
            _changingExpansion = true;
            foreach (var section in Sections) section.IsExpanded = isExpanded;
            _changingExpansion = false;
            SaveExpansionState();
        }

        private void OnSectionPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HardwareSectionViewModel.IsExpanded) && !_changingExpansion)
                SaveExpansionState();
        }

        private void SaveExpansionState()
        {
            _settings.CollapsedHardwareSections.Clear();
            _settings.CollapsedHardwareSections.AddRange(Sections.Where(section => !section.IsExpanded).Select(section => section.SectionId));
            _store.Save(_settings);
        }

        private bool CanUseReport() => _report != null && !IsLoading;

        [RelayCommand(CanExecute = nameof(CanUseReport))]
        private void ExportHtml()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salvar relatório de hardware",
                Filter = "Página HTML (*.html)|*.html",
                FileName = $"Relatorio-{_report.MachineName}-{_report.GeneratedAt:yyyyMMdd-HHmm}.html",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, ReportExporter.ToHtml(_report), new UTF8Encoding(false));
                StatusMessage = "Relatório salvo em " + dialog.FileName;
                Process.Start(dialog.FileName);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao exportar relatório", ex);
                StatusMessage = "Não foi possível salvar o relatório: " + ex.Message;
            }
        }

        [RelayCommand(CanExecute = nameof(CanUseReport))]
        private void CopyText()
        {
            try
            {
                Clipboard.SetText(ReportExporter.ToText(_report));
                StatusMessage = "Relatório copiado para a área de transferência";
            }
            catch (Exception ex)
            {
                StatusMessage = "Não foi possível copiar: " + ex.Message;
            }
        }
    }
}
