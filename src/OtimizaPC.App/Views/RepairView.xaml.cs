using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OtimizaPC.App.ViewModels;

namespace OtimizaPC.App.Views
{
    public partial class RepairView : UserControl
    {
        private RepairViewModel _subscribed;

        public RepairView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // Rolagem automática do console para a última linha
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_subscribed != null) _subscribed.ConsoleBatchApplied -= OnConsoleBatchApplied;
            _subscribed = DataContext as RepairViewModel;
            if (_subscribed != null) _subscribed.ConsoleBatchApplied += OnConsoleBatchApplied;
        }

        private void OnConsoleBatchApplied(object sender, EventArgs e)
        {
            // Aguarda o ListBox processar as mudanças do lote antes de rolar.
            if (_scrollPending) return;
            _scrollPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _scrollPending = false;
                if (ConsoleList.Items.Count > 0)
                    ConsoleList.ScrollIntoView(ConsoleList.Items[ConsoleList.Items.Count - 1]);
            }));
        }

        private bool _scrollPending;
    }
}
