using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Core.Reports;
using OtimizaPC.Hardware;

namespace OtimizaPC.App.ViewModels
{
    public partial class HardwareViewModel : ObservableObject, IActivatable
    {
        private readonly HardwareInfoService _hardwareInfo;
        private HardwareReport _report;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ExportHtmlCommand), nameof(CopyTextCommand))]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage;

        public HardwareViewModel(HardwareInfoService hardwareInfo)
        {
            _hardwareInfo = hardwareInfo;
        }

        public ObservableCollection<InfoSection> Sections { get; } = new ObservableCollection<InfoSection>();

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
                Sections.Clear();
                foreach (var section in _report.Sections) Sections.Add(section);
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
