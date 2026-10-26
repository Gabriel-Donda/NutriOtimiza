using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using OtimizaPC.App.Services;
using OtimizaPC.App.ViewModels;
using OtimizaPC.Core.Settings;
using Wpf.Ui.Controls;

namespace OtimizaPC.App
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            // Acompanha a troca de tema claro/escuro do Windows 10/11 quando a preferência é "Sistema"
            Loaded += (_, __) =>
            {
                ThemeService.Watch(new SettingsStore().Load().Theme);
                // A primeira tela também entra com animação
                PlayPageTransition();
            };
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ViewModels.MainViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is ViewModels.MainViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.MainViewModel.SelectedItem)) PlayPageTransition();
        }

        private void PlayPageTransition()
        {
            // Espera o layout da nova tela: os cards precisam existir para entrar escalonados
            Dispatcher.BeginInvoke(new Action(() => MotionService.PlayPageEnter(PageHost, PageOffset)), DispatcherPriority.Loaded);
        }
    }
}
