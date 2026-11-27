using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OtimizaPC.App.Services;
using OtimizaPC.Core;
using OtimizaPC.Tweaks.Uninstall;

namespace OtimizaPC.App.ViewModels
{
    public sealed class InstalledProgramItemViewModel
    {
        public InstalledProgramItemViewModel(InstalledProgram program)
        {
            Program = program;
            Icon = LoadIcon(program.DisplayIcon);
        }

        public InstalledProgram Program { get; }
        public BitmapSource Icon { get; }
        public string InstallDateText => Format.Date(Program.InstallDate) ?? "—";
        public string EstimatedSizeText => Program.EstimatedSizeKb.HasValue
            ? Format.Bytes(Program.EstimatedSizeKb.Value * 1024d)
            : "—";

        private static BitmapSource LoadIcon(string displayIcon)
        {
            if (string.IsNullOrEmpty(displayIcon)) return null;

            var path = displayIcon;
            var separator = path.LastIndexOf(',');
            if (separator >= 0 &&
                int.TryParse(path.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                // O serviço extrai somente o ícone associado ao executável.
                if (index != 0) return null;
                path = path.Substring(0, separator);
            }

            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? IconService.FromExecutable(path) : null;
        }
    }

    public partial class UninstallViewModel : ObservableObject, IActivatable
    {
        private readonly InstalledProgramService _service = new InstalledProgramService();
        private IReadOnlyList<InstalledProgramItemViewModel> _programs = Array.Empty<InstalledProgramItemViewModel>();
        private bool _loaded;

        [ObservableProperty]
        private string _searchText;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Summary))]
        private IReadOnlyList<InstalledProgramItemViewModel> _items = Array.Empty<InstalledProgramItemViewModel>();

        public string Summary => Items.Count == _programs.Count
            ? $"{Items.Count} programa(s)"
            : $"{Items.Count} de {_programs.Count} programa(s)";

        public void OnActivated()
        {
            if (!_loaded && !IsLoading) _ = RefreshAsync();
        }

        public void OnDeactivated()
        {
        }

        partial void OnSearchTextChanged(string value) => ApplySearch();

        private bool CanRefresh() => !IsLoading;

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
        {
            IsLoading = true;
            StatusMessage = null;
            try
            {
                _programs = await Task.Run(() => _service.ListInstalled()
                    .OrderBy(program => program.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(program => new InstalledProgramItemViewModel(program)).ToList());
                ApplySearch();
                _loaded = true;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao listar programas instalados", ex);
                StatusMessage = "Não foi possível listar os programas instalados.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplySearch()
        {
            var query = SearchText?.Trim();
            Items = string.IsNullOrEmpty(query) ? _programs : _programs.Where(item =>
                item.Program.DisplayName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (item.Program.Publisher?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) ?? -1) >= 0).ToList();
        }
    }
}
