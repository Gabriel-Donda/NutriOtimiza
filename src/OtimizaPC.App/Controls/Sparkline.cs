using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OtimizaPC.App.Controls
{
    /// <summary>Mini gráfico de linha para histórico recente (desenho direto, sem biblioteca de gráficos).</summary>
    public sealed class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Valor máximo do eixo; NaN = automático (maior valor do histórico).</summary>
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(Sparkline),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Quantidade de pontos que cabem na largura do gráfico.</summary>
        public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
            nameof(Capacity), typeof(int), typeof(Sparkline),
            new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<double> Values
        {
            get => (IReadOnlyList<double>)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public int Capacity
        {
            get => (int)GetValue(CapacityProperty);
            set => SetValue(CapacityProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var values = Values;
            var width = ActualWidth;
            var height = ActualHeight;
            if (values == null || values.Count < 2 || width <= 0 || height <= 0 || Stroke == null) return;

            var max = double.IsNaN(Maximum) ? Math.Max(1, values.Max() * 1.15) : Maximum;
            var step = width / Math.Max(1, Capacity - 1);
            // Os pontos mais recentes ficam à direita
            var startX = width - (values.Count - 1) * step;

            Point PointAt(int i) => new Point(
                startX + i * step,
                height - Math.Max(0, Math.Min(1, values[i] / max)) * (height - 2) - 1);

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(PointAt(0), false, false);
                for (var i = 1; i < values.Count; i++) ctx.LineTo(PointAt(i), true, true);
            }
            line.Freeze();

            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(startX, height), true, true);
                for (var i = 0; i < values.Count; i++) ctx.LineTo(PointAt(i), true, true);
                ctx.LineTo(new Point(width, height), true, true);
            }
            area.Freeze();

            var fill = Stroke.CloneCurrentValue();
            fill.Opacity = 0.18;
            fill.Freeze();

            var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
            pen.Freeze();

            dc.DrawGeometry(fill, null, area);
            dc.DrawGeometry(null, pen, line);
        }
    }
}
