using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OtimizaPC.App.Controls;
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
                MotionService.PrepareWindowEnter(RootContent);
                UpdateNavIndicatorLater(animate: false);
            };
            // Abertura: fade do conteúdo e entrada da primeira tela, a partir do primeiro quadro desenhado
            ContentRendered += (_, __) =>
            {
                MotionService.PlayWindowEnter(RootContent);
                PlayPageTransition();
            };
            DataContextChanged += OnDataContextChanged;

            // Indicador do menu: desliza na troca de tela e acompanha rolagem e redimensionamento sem animar
            NavList.SelectionChanged += (_, __) => UpdateNavIndicatorLater(animate: true);
            NavList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, __) => UpdateNavIndicator(animate: false)));
            NavArea.SizeChanged += (_, __) => UpdateNavIndicator(animate: false);
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MotionService.PlayPageEnter(PageHost, PageOffset);
                MotionBehaviors.PlayCountUp(PageHost);
            }), DispatcherPriority.Loaded);
        }

        private void UpdateNavIndicatorLater(bool animate) =>
            Dispatcher.BeginInvoke(new Action(() => UpdateNavIndicator(animate)), DispatcherPriority.Loaded);

        /// <summary>Leva o indicador até o meio do item selecionado; some se o item não estiver na tela.</summary>
        private void UpdateNavIndicator(bool animate)
        {
            var item = NavList.SelectedItem == null ? null : NavList.ItemContainerGenerator.ContainerFromItem(NavList.SelectedItem) as ListBoxItem;
            if (item == null || !item.IsVisible || item.ActualHeight <= 0)
            {
                NavIndicator.Opacity = 0;
                return;
            }

            var top = item.TransformToAncestor(NavArea).Transform(new Point(0, 0)).Y;
            var y = Math.Round(top + (item.ActualHeight - NavIndicator.Height) / 2);
            if (animate && MotionService.Enabled && NavIndicator.Opacity > 0)
            {
                MotionService.Animate(NavOffset, TranslateTransform.YProperty, y, MotionService.Duration("MotionPageMs", 280));
            }
            else
            {
                MotionService.Snap(NavOffset, TranslateTransform.YProperty, y);
            }
            NavIndicator.Opacity = 1;
        }
    }
}
