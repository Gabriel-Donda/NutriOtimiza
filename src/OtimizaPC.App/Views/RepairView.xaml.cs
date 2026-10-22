using System;
using System.Collections.Specialized;
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
            if (_subscribed != null) _subscribed.ConsoleLines.CollectionChanged -= OnConsoleChanged;
            _subscribed = DataContext as RepairViewModel;
            if (_subscribed != null) _subscribed.ConsoleLines.CollectionChanged += OnConsoleChanged;
        }

        private void OnConsoleChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Rolar dentro do próprio CollectionChanged força o ListBox a gerar itens antes de processar a mudança
            // ("ItemsControl inconsistente com a origem"); por isso a rolagem é adiada
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
