using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.Cleaning.Analysis;
using OtimizaPC.Core;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    public sealed class DriveOption
    {
        public DriveOption(DriveInfo drive)
        {
            RootPath = drive.Name;
            var label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : $"{drive.Name.TrimEnd('\\')} ({drive.VolumeLabel})";
            DisplayName = $"{label} — {Format.Bytes(drive.TotalFreeSpace)} livres de {Format.Bytes(drive.TotalSize)}";
        }

        public string RootPath { get; }

        public string DisplayName { get; }
    }

    /// <summary>Linha da lista de conteúdo: uma subpasta ou o agregado de arquivos soltos da pasta atual.</summary>
    public sealed class FolderEntryViewModel
    {
        public FolderEntryViewModel(FolderNode node, long parentSize)
        {
            Node = node;
            Name = node.Name;
            Size = node.Size;
            Icon = SymbolRegular.Folder24;
            Detail = node.IsAccessible ? $"{node.FileCount:N0} arquivos" : $"{node.FileCount:N0} arquivos · acesso parcial";
            Percent = parentSize > 0 ? node.Size * 100.0 / parentSize : 0;
        }

        public FolderEntryViewModel(string name, long size, int fileCount, long parentSize)
        {
            Name = name;
            Size = size;
            Icon = SymbolRegular.Document24;
            Detail = $"{fileCount:N0} arquivos";
            Percent = parentSize > 0 ? size * 100.0 / parentSize : 0;
        }

        /// <summary>Null para a linha de "arquivos nesta pasta".</summary>
        public FolderNode Node { get; }

        public string Name { get; }

        public long Size { get; }

        public string SizeText => Format.Bytes(Size);

        public string Detail { get; }

        public double Percent { get; }

        public string PercentText => Format.Percent(Percent, 1);

        public SymbolRegular Icon { get; }

        public bool CanOpen => Node != null && Node.Children.Count > 0;
    }

    public sealed class LargeFileViewModel
    {
        public LargeFileViewModel(LargeFile file)
        {
            Path = file.Path;
            Name = System.IO.Path.GetFileName(file.Path);
            Folder = System.IO.Path.GetDirectoryName(file.Path);
            SizeText = Format.Bytes(file.Size);
        }

        public string Path { get; }

        public string Name { get; }

        public string Folder { get; }

        public string SizeText { get; }
    }

    public partial class DiskAnalyzerViewModel : ObservableObject, IActivatable
    {
        private const int LargestFilesShown = 50;

        private CancellationTokenSource _cts;
        private DiskAnalysis _analysis;

        [ObservableProperty]
        private DriveOption _selectedDrive;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(CancelCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Escolha uma unidade e clique em Analisar.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentPath), nameof(CurrentSizeText), nameof(CanGoUp), nameof(HasResult))]
        [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
        private FolderNode _currentFolder;

        public ObservableCollection<DriveOption> Drives { get; } = new ObservableCollection<DriveOption>();

        public ObservableCollection<FolderEntryViewModel> Entries { get; } = new ObservableCollection<FolderEntryViewModel>();

        public ObservableCollection<LargeFileViewModel> LargestFiles { get; } = new ObservableCollection<LargeFileViewModel>();

        public string CurrentPath => CurrentFolder?.FullPath;

        public string CurrentSizeText => CurrentFolder == null ? null : Format.Bytes(CurrentFolder.Size);

        public bool CanGoUp => CurrentFolder?.Parent != null;

        public bool HasResult => CurrentFolder != null;

        public void OnActivated()
        {
            if (Drives.Count > 0) return;
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                Drives.Add(new DriveOption(drive));
            var system = Path.GetPathRoot(Environment.SystemDirectory);
            SelectedDrive = Drives.FirstOrDefault(d => string.Equals(d.RootPath, system, StringComparison.OrdinalIgnoreCase))
                            ?? Drives.FirstOrDefault();
        }

        public void OnDeactivated()
        {
        }

        private bool CanAnalyze() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanAnalyze))]
        private async Task AnalyzeAsync()
        {
            if (SelectedDrive == null) return;

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var progress = new Progress<string>(path => StatusMessage = "Lendo " + path);
            try
            {
                _analysis = await DiskAnalyzer.AnalyzeAsync(SelectedDrive.RootPath, progress, _cts.Token);

                LargestFiles.Clear();
                foreach (var file in _analysis.LargestFiles.Take(LargestFilesShown))
                    LargestFiles.Add(new LargeFileViewModel(file));

                ShowFolder(_analysis.Root);
                StatusMessage = $"{Format.Bytes(_analysis.Root.Size)} em {_analysis.Root.FileCount:N0} arquivos · análise em {_analysis.Duration.TotalSeconds:N0} s"
                                + (_analysis.InaccessibleFolders > 0
                                    ? $" · {_analysis.InaccessibleFolders:N0} pasta(s) sem acesso" + (AppInfo.IsAdministrator ? string.Empty : " (execute como administrador)")
                                    : string.Empty);
                Log.Info($"Análise de disco {SelectedDrive.RootPath}: {StatusMessage}");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Análise cancelada.";
            }
            catch (Exception ex)
            {
                Log.Error("Falha na análise de disco", ex);
                StatusMessage = "Erro na análise: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanCancel() => IsBusy;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel() => _cts?.Cancel();

        [RelayCommand(CanExecute = nameof(CanGoUp))]
        private void GoUp()
        {
            if (CurrentFolder?.Parent != null) ShowFolder(CurrentFolder.Parent);
        }

        public void Open(FolderEntryViewModel entry)
        {
            if (entry?.CanOpen == true) ShowFolder(entry.Node);
        }

        [RelayCommand]
        private void OpenInExplorer(object item)
        {
            string path;
            switch (item)
            {
                case FolderEntryViewModel entry:
                    path = entry.Node?.FullPath ?? CurrentPath;
                    break;
                case LargeFileViewModel file:
                    path = file.Path;
                    break;
                default:
                    path = CurrentPath;
                    break;
            }
            if (path == null) return;

            try
            {
                // /select destaca o item dentro da pasta pai
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                StatusMessage = "Não foi possível abrir o Explorador: " + ex.Message;
            }
        }

        private void ShowFolder(FolderNode folder)
        {
            CurrentFolder = folder;
            Entries.Clear();
            foreach (var child in folder.Children)
                Entries.Add(new FolderEntryViewModel(child, folder.Size));

            if (folder.DirectFileCount > 0)
            {
                var loose = new FolderEntryViewModel("(arquivos nesta pasta)", folder.DirectFilesSize, folder.DirectFileCount, folder.Size);
                var index = Entries.TakeWhile(e => e.Size >= loose.Size).Count();
                Entries.Insert(index, loose);
            }
        }
    }
}
