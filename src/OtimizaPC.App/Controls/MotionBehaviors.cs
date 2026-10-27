using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
            MotionService.Animate(bar, RangeBase.ValueProperty, to, MotionService.Duration("MotionProgressMs", 450));
        }

        // ---------- Valor de texto que mudou ----------

        /// <summary>
        /// Em um TextBlock: quando o texto muda, números contam do valor antigo ao novo (ex.: "45 %" → "60 %")
        /// e textos sem número "acendem" de leve. Na entrada da tela, os números contam a partir de zero
        /// (<see cref="PlayCountUp"/>). Só anima o que está na tela.
        /// </summary>
        public static readonly DependencyProperty PulseOnChangeProperty = DependencyProperty.RegisterAttached(
            "PulseOnChange", typeof(bool), typeof(MotionBehaviors), new PropertyMetadata(false, OnPulseOnChangeChanged));

        /// <summary>Intervalo mínimo entre duas animações do mesmo texto (ms).</summary>
        private const int PulseIntervalMs = 2000;

        /// <summary>Máximo de números contando de uma vez na entrada da tela.</summary>
        private const int MaxCountUps = 16;

        /// <summary>Limite de nós percorridos ao procurar números, para não varrer listas enormes.</summary>
        private const int CountScanLimit = 800;

        // "45 %", "3,2 GHz", "-12": prefixo sem dígitos, um número e sufixo sem dígitos (evita "1.234,5" e "8 / 16 GB")
        private static readonly Regex NumberPattern = new Regex(@"^(\D*?)(-?\d+)(?:([.,])(\d+))?(\D*)$", RegexOptions.CultureInvariant);

        // Estado de cada texto. Chave fraca: não segura o TextBlock depois que ele é descartado.
        private static readonly ConditionalWeakTable<TextBlock, TextState> States = new ConditionalWeakTable<TextBlock, TextState>();

        /// <summary>Valor numérico animado; a cada quadro vira texto no mesmo formato do valor final.</summary>
        private static readonly DependencyProperty CountValueProperty = DependencyProperty.RegisterAttached(
            "CountValue", typeof(double), typeof(MotionBehaviors), new PropertyMetadata(0d, OnCountValueChanged));

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
            StateOf(text).LastText = text.Text;
            TextDescriptor.RemoveValueChanged(text, TextChangedHandler);
            TextDescriptor.AddValueChanged(text, TextChangedHandler);
        }

        private static void UnwatchText(TextBlock text) => TextDescriptor.RemoveValueChanged(text, TextChangedHandler);

        private static void OnTextChanged(object sender, EventArgs e)
        {
            if (!(sender is TextBlock text)) return;
            var state = StateOf(text);
            if (state.Writing) return; // quadro da própria contagem

            var previous = state.LastText;
            state.LastText = text.Text;
            if (!MotionService.Enabled || !MotionService.IsInViewport(text)) return;

            // Valor novo no meio de uma contagem: segue até ele, sem esperar o intervalo
            if (state.Counting && TryParse(text.Text, out var retarget))
            {
                StartCount(text, state, (double)text.GetValue(CountValueProperty), retarget);
                return;
            }

            // Sensores mudam a cada 1 s: sem limite, o valor piscaria em todo tick. No máximo uma animação por intervalo.
            var now = Environment.TickCount;
            if (now - state.LastPulse < PulseIntervalMs) return;
            state.LastPulse = now;

            // Número novo: conta do anterior ou, se antes não havia número ("—", "Carregando"), de zero
            if (TryParse(text.Text, out var to))
            {
                var from = TryParse(previous, out var old) ? old.Value : 0;
                if (from != to.Value) StartCount(text, state, from, to);
                return;
            }

            var baseOpacity = (double)text.GetAnimationBaseValue(UIElement.OpacityProperty);
            var start = baseOpacity * Token("MotionPulseFromOpacity", 0.55d);
            MotionService.Animate(text, UIElement.OpacityProperty, baseOpacity, MotionService.Duration("MotionFastMs", 150), from: start);
        }

        /// <summary>Entrada da tela: os números visíveis marcados com PulseOnChange contam a partir de zero.</summary>
        public static void PlayCountUp(FrameworkElement root)
        {
            if (!MotionService.Enabled || root == null) return;
            var found = new List<TextBlock>();
            var scanned = 0;
            CollectCountable(root, found, ref scanned);
            foreach (var text in found)
            {
                if (!TryParse(text.Text, out var to) || to.Value == 0) continue;
                var state = StateOf(text);
                state.LastText = text.Text;
                state.LastPulse = Environment.TickCount;
                StartCount(text, state, 0, to);
            }
        }

        private static void CollectCountable(DependencyObject node, List<TextBlock> found, ref int scanned)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count && scanned < CountScanLimit && found.Count < MaxCountUps; i++)
            {
                scanned++;
                var child = VisualTreeHelper.GetChild(node, i);
                if (!(child is UIElement element) || element.Visibility != Visibility.Visible) continue;
                if (child is TextBlock text && GetPulseOnChange(text)) { if (MotionService.IsInViewport(text)) found.Add(text); }
                else CollectCountable(child, found, ref scanned);
            }
        }

        private static void StartCount(TextBlock text, TextState state, double from, ParsedNumber to)
        {
            state.Format = to;
            state.Counting = true;
            MotionService.Animate(text, CountValueProperty, to.Value, MotionService.Duration("MotionCountMs", 600), from: from,
                completed: () =>
                {
                    state.Counting = false;
                    // Termina exatamente no texto real: o último recebido, e a ligação (se houver) confirma o valor atual
                    Write(text, state, state.LastText);
                    BindingOperations.GetBindingExpression(text, TextBlock.TextProperty)?.UpdateTarget();
                });
        }

        private static void OnCountValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock text) || !States.TryGetValue(text, out var state) || !state.Counting || state.Format == null) return;
            Write(text, state, state.Format.Render((double)e.NewValue));
        }

        // SetCurrentValue mantém a ligação do Text: a próxima atualização do ViewModel continua valendo
        private static void Write(TextBlock text, TextState state, string value)
        {
            state.Writing = true;
            try { text.SetCurrentValue(TextBlock.TextProperty, value); }
            finally { state.Writing = false; }
        }

        private static bool TryParse(string text, out ParsedNumber number)
        {
            number = null;
            if (string.IsNullOrEmpty(text) || text.Length > 40) return false;
            var match = NumberPattern.Match(text);
            if (!match.Success) return false;
            var digits = match.Groups[2].Value + (match.Groups[4].Success ? "." + match.Groups[4].Value : string.Empty);
            if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
            number = new ParsedNumber
            {
                Prefix = match.Groups[1].Value,
                Suffix = match.Groups[5].Value,
                Separator = match.Groups[3].Success ? match.Groups[3].Value : ",",
                Decimals = match.Groups[4].Success ? match.Groups[4].Value.Length : 0,
                Value = value,
            };
            return true;
        }

        private static TextState StateOf(TextBlock text) =>
            States.GetValue(text, _ => new TextState { LastPulse = Environment.TickCount - PulseIntervalMs });

        private static double Token(string key, double fallback) => MotionService.Token(key, fallback);

        private sealed class TextState
        {
            public int LastPulse;
            public string LastText;
            public bool Writing;
            public bool Counting;
            public ParsedNumber Format;
        }

        /// <summary>Número dentro de um texto, com o formato para escrever os valores intermediários.</summary>
        private sealed class ParsedNumber
        {
            public string Prefix;
            public string Suffix;
            public string Separator;
            public int Decimals;
            public double Value;

            public string Render(double value)
            {
                var number = value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
                if (Decimals > 0) number = number.Replace(".", Separator);
                return Prefix + number + Suffix;
            }
        }
    }
}
