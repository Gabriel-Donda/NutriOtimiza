using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OtimizaPC.App.Services;

namespace OtimizaPC.App.Controls
{
    /// <summary>
    /// Propriedades anexadas que ligam o <see cref="MotionService"/> ao XAML. Quando as animações estão
    /// desligadas, cada uma aplica o estado final na hora, sem animação.
    /// </summary>
    public static class MotionBehaviors
    {
        // ---------- Expandir e recolher (altura + opacidade) ----------

        /// <summary>Em um Border com ClipToBounds: mostra ou recolhe o conteúdo com altura e opacidade animadas.</summary>
        public static readonly DependencyProperty ExpandedProperty = DependencyProperty.RegisterAttached(
            "Expanded", typeof(bool), typeof(MotionBehaviors), new PropertyMetadata(true, OnExpandedChanged));

        public static void SetExpanded(DependencyObject element, bool value) => element.SetValue(ExpandedProperty, value);

        public static bool GetExpanded(DependencyObject element) => (bool)element.GetValue(ExpandedProperty);

        private static void OnExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement element)) return;
            var expanded = (bool)e.NewValue;
            var duration = MotionService.Duration("MotionNormalMs", 220);

            if (!element.IsLoaded || !MotionService.Enabled)
            {
                ApplyInstant(element, expanded);
                return;
            }

            if (expanded)
            {
                var from = element.ActualHeight;
                element.BeginAnimation(FrameworkElement.HeightProperty, null);
                element.Height = double.NaN;
                element.Visibility = Visibility.Visible;
                element.Measure(new Size(WidthOf(element), double.PositiveInfinity));
                var to = element.DesiredSize.Height;

                MotionService.Animate(element, FrameworkElement.HeightProperty, to, duration, from: from,
                    completed: () => element.Height = double.NaN);
                MotionService.Animate(element, UIElement.OpacityProperty, 1, duration, from: 0);
            }
            else
            {
                // Parte da altura atual: funciona mesmo se o usuário clicar no meio de uma animação
                MotionService.Animate(element, FrameworkElement.HeightProperty, 0, duration, from: element.ActualHeight,
                    completed: () =>
                    {
                        element.Visibility = Visibility.Collapsed;
                        element.Height = double.NaN;
                        element.Opacity = 1;
                    });
                MotionService.Animate(element, UIElement.OpacityProperty, 0, duration, from: element.Opacity);
            }
        }

        private static void ApplyInstant(FrameworkElement element, bool expanded)
        {
            element.BeginAnimation(FrameworkElement.HeightProperty, null);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Height = double.NaN;
            element.Opacity = 1;
            element.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Largura usada para medir o conteúdo; recolhido, o elemento tem largura 0, então usa o pai.</summary>
        private static double WidthOf(FrameworkElement element)
        {
            if (element.ActualWidth > 0) return element.ActualWidth;
            var parentWidth = (element.Parent as FrameworkElement)?.ActualWidth ?? 0;
            return parentWidth > 0 ? parentWidth : double.PositiveInfinity;
        }

        // ---------- Chevron de expansão ----------

        /// <summary>Gira o ícone de chevron 180° ao expandir, em vez de virar de uma vez.</summary>
        public static readonly DependencyProperty ChevronExpandedProperty = DependencyProperty.RegisterAttached(
            "ChevronExpanded", typeof(bool), typeof(MotionBehaviors), new PropertyMetadata(false, OnChevronExpandedChanged));

        public static void SetChevronExpanded(DependencyObject element, bool value) => element.SetValue(ChevronExpandedProperty, value);

        public static bool GetChevronExpanded(DependencyObject element) => (bool)element.GetValue(ChevronExpandedProperty);

        private static void OnChevronExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement icon)) return;
            var rotate = GetRotate(icon);
            var angle = (bool)e.NewValue ? 180d : 0d;

            if (!icon.IsLoaded || !MotionService.Enabled)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                rotate.Angle = angle;
                return;
            }
            MotionService.Animate(rotate, RotateTransform.AngleProperty, angle, MotionService.Duration("MotionFastMs", 150));
        }

        private static RotateTransform GetRotate(FrameworkElement icon)
        {
            if (icon.RenderTransform is RotateTransform existing) return existing;
            var rotate = new RotateTransform(0);
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = rotate;
            return rotate;
        }

        // ---------- Barra de progresso ----------

        /// <summary>Move a barra até o novo valor com animação curta, em vez de pular.</summary>
        public static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.RegisterAttached(
            "AnimatedValue", typeof(double), typeof(MotionBehaviors), new PropertyMetadata(0d, OnAnimatedValueChanged));

        public static void SetAnimatedValue(DependencyObject element, double value) => element.SetValue(AnimatedValueProperty, value);

        public static double GetAnimatedValue(DependencyObject element) => (double)element.GetValue(AnimatedValueProperty);

        private static void OnAnimatedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is RangeBase bar)) return;
            var to = (double)e.NewValue;

            if (!bar.IsLoaded || !MotionService.Enabled)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Value = to;
                return;
            }
            MotionService.Animate(bar, RangeBase.ValueProperty, to, MotionService.Duration("MotionNormalMs", 220));
        }

        // ---------- Valor de texto que mudou ----------

        /// <summary>Em um TextBlock: quando o texto muda, ele "acende" de leve (opacidade volta ao normal). Só anima o que está na tela.</summary>
        public static readonly DependencyProperty PulseOnChangeProperty = DependencyProperty.RegisterAttached(
            "PulseOnChange", typeof(bool), typeof(MotionBehaviors), new PropertyMetadata(false, OnPulseOnChangeChanged));

        /// <summary>Intervalo mínimo entre dois pulsos do mesmo texto (ms).</summary>
        private const int PulseIntervalMs = 2000;

        // Último pulso de cada texto. Chave fraca: não segura o TextBlock depois que ele é descartado.
        private static readonly ConditionalWeakTable<TextBlock, StrongBox<int>> LastPulse = new ConditionalWeakTable<TextBlock, StrongBox<int>>();

        private static readonly EventHandler TextChangedHandler = OnTextChanged;
        private static readonly RoutedEventHandler TextBlockLoadedHandler = (sender, _) => WatchText((TextBlock)sender);
        private static readonly RoutedEventHandler TextBlockUnloadedHandler = (sender, _) => UnwatchText((TextBlock)sender);
        private static readonly DependencyPropertyDescriptor TextDescriptor =
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

        public static void SetPulseOnChange(DependencyObject element, bool value) => element.SetValue(PulseOnChangeProperty, value);

        public static bool GetPulseOnChange(DependencyObject element) => (bool)element.GetValue(PulseOnChangeProperty);

        private static void OnPulseOnChangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock text)) return;
            if ((bool)e.NewValue)
            {
                text.Loaded += TextBlockLoadedHandler;
                text.Unloaded += TextBlockUnloadedHandler;
                if (text.IsLoaded) WatchText(text);
            }
            else
            {
                text.Loaded -= TextBlockLoadedHandler;
                text.Unloaded -= TextBlockUnloadedHandler;
                UnwatchText(text);
            }
        }

        // Assina a mudança do texto só enquanto o elemento está carregado, para não segurar o TextBlock depois de descarregado
        private static void WatchText(TextBlock text)
        {
            TextDescriptor.RemoveValueChanged(text, TextChangedHandler);
            TextDescriptor.AddValueChanged(text, TextChangedHandler);
        }

        private static void UnwatchText(TextBlock text) => TextDescriptor.RemoveValueChanged(text, TextChangedHandler);

        private static void OnTextChanged(object sender, EventArgs e)
        {
            if (!(sender is TextBlock text) || !MotionService.Enabled || !MotionService.IsInViewport(text)) return;

            // Sensores mudam a cada 1 s: sem limite, o valor piscaria em todo tick. No máximo um pulso por intervalo.
            var now = Environment.TickCount;
            if (!LastPulse.TryGetValue(text, out var last))
            {
                last = new StrongBox<int>(now - PulseIntervalMs);
                LastPulse.Add(text, last);
            }
            if (now - last.Value < PulseIntervalMs) return;
            last.Value = now;

            var baseOpacity = (double)text.GetAnimationBaseValue(UIElement.OpacityProperty);
            var from = baseOpacity * Token("MotionPulseFromOpacity", 0.55d);
            MotionService.Animate(text, UIElement.OpacityProperty, baseOpacity, MotionService.Duration("MotionFastMs", 150), from: from);
        }

        private static double Token(string key, double fallback) => MotionService.Token(key, fallback);
    }
}
