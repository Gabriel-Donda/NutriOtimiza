using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OtimizaPC.Core.Models;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.Converters
{
    /// <summary>Esconde o elemento quando o valor é nulo ou texto vazio.</summary>
    public sealed class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value == null || value is string s && s.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Visível só quando o número é zero (mensagem de lista vazia).</summary>
    public sealed class ZeroToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public sealed class PawnIoStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool installed && installed
                ? "Instalado (execute o OtimizaPC como administrador para usá-lo)."
                : "Não instalado.";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    }

    /// <summary>Converte o nome do ícone (string vinda do Core) em SymbolRegular.</summary>
    public sealed class StringToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string name && Enum.TryParse(name, out SymbolRegular symbol) ? symbol : SymbolRegular.Info24;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public sealed class HealthStatusToSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as HealthStatus?)
            {
                case HealthStatus.Ok: return SymbolRegular.CheckmarkCircle24;
                case HealthStatus.Warning: return SymbolRegular.Warning24;
                case HealthStatus.Critical: return SymbolRegular.ErrorCircle24;
                default: return SymbolRegular.Info24;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public sealed class HealthStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key;
            switch (value as HealthStatus?)
            {
                case HealthStatus.Ok: key = "StatusOkBrush"; break;
                case HealthStatus.Warning: key = "StatusWarningBrush"; break;
                case HealthStatus.Critical: key = "StatusCriticalBrush"; break;
                default: key = "StatusInfoBrush"; break;
            }
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
