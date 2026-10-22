using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using OtimizaPC.Core.Models;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.ViewModels
{
    /// <summary>Cartão de métrica do painel (valor grande, detalhe e mini gráfico).</summary>
    public partial class MetricCard : ObservableObject
    {
        private const int HistoryLength = 60;

        private readonly Queue<double> _samples = new Queue<double>();

        [ObservableProperty]
        private string _value = "—";

        [ObservableProperty]
        private string _detail;

        [ObservableProperty]
        private HealthStatus _status = HealthStatus.Ok;

        [ObservableProperty]
        private double[] _history;

        public MetricCard(string title, SymbolRegular icon, bool showChart = true, double maximum = 100)
        {
            Title = title;
            Icon = icon;
            ShowChart = showChart;
            Maximum = maximum;
        }

        public string Title { get; }

        public SymbolRegular Icon { get; }

        public bool ShowChart { get; }

        /// <summary>Máximo do gráfico; NaN = automático.</summary>
        public double Maximum { get; }

        public void Push(double sample)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > HistoryLength) _samples.Dequeue();
            History = _samples.ToArray();
        }
    }
}
