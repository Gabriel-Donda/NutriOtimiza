using System.Windows.Controls;
using System.Windows.Input;
using OtimizaPC.App.ViewModels;

namespace OtimizaPC.App.Views
{
    public partial class DiskAnalyzerView : UserControl
    {
        public DiskAnalyzerView()
        {
            InitializeComponent();
        }

        private DiskAnalyzerViewModel ViewModel => DataContext as DiskAnalyzerViewModel;

        // Navegação na árvore: duplo clique / Enter entra na pasta, Backspace sobe
        private void OnEntriesDoubleClick(object sender, MouseButtonEventArgs e) =>
            ViewModel?.Open(EntriesList.SelectedItem as FolderEntryViewModel);

        private void OnEntriesKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel?.Open(EntriesList.SelectedItem as FolderEntryViewModel);
                e.Handled = true;
            }
            else if (e.Key == Key.Back && ViewModel?.GoUpCommand.CanExecute(null) == true)
            {
                ViewModel.GoUpCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
