using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OtimizaPC.Core;
using OtimizaPC.Core.Models;
using OtimizaPC.Repair.Fixes;
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

    public sealed class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
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
                ? $"Instalado (execute o {AppInfo.Name} como administrador para usá-lo)."
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

    /// <summary>Converte EventDiagnosisClass em rótulo localizado para o selo do cartão.</summary>
    public sealed class DiagnosisClassToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is EventDiagnosisClass cls)) return "?";
            switch (cls)
            {
                case EventDiagnosisClass.Real: return "Real";
                case EventDiagnosisClass.Hardware: return "Hardware";
                case EventDiagnosisClass.Informativo: return "Informativo";
                default: return cls.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Converte EventDiagnosisClass em Brush para o fundo do selo (amarelo/vermelho/azul).</summary>
    public sealed class DiagnosisClassToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is EventDiagnosisClass cls)) return Brushes.Gray;
            switch (cls)
            {
                case EventDiagnosisClass.Real:
                    // Amarelo — problema real
                    return Application.Current.TryFindResource("StatusWarningBrush") as Brush
                           ?? new SolidColorBrush(Color.FromRgb(0xD2, 0x99, 0x22));
                case EventDiagnosisClass.Hardware:
                    // Vermelho — problema de hardware
                    return Application.Current.TryFindResource("StatusCriticalBrush") as Brush
                           ?? new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
                case EventDiagnosisClass.Informativo:
                    // Azul — informativo
                    return Application.Current.TryFindResource("StatusInfoBrush") as Brush
                           ?? new SolidColorBrush(Color.FromRgb(0x4C, 0x8E, 0xDA));
                default:
                    return Brushes.Gray;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
