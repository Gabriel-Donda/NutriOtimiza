using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OtimizaPC.Core.Settings;

namespace OtimizaPC.App.Services
{
    /// <summary>
    /// Animações do app: transição de tela, entrada dos cards, hover/pressionado e mudanças de valor.
    /// Tudo passa por <see cref="Enabled"/>. As regras ficam em <see cref="AnimationPolicy"/> (Core).
    /// Os tokens (durações, curva, deslocamentos) ficam em Resources/Animations.xaml.
    /// </summary>
    public static class MotionService
    {
        /// <summary>Máximo de cards animados em uma entrada, para telas com muitos itens.</summary>
        private const int MaxStaggeredCards = 12;

        /// <summary>Limite de nós percorridos ao procurar cards, para não varrer listas enormes.</summary>
        private const int ScanLimit = 400;

        private const int SmRemoteSession = 0x1000;

        private static readonly CubicEase FallbackEase = CreateFallbackEase();
        private static readonly ConditionalWeakTable<FrameworkElement, MotionTransforms> Transforms = new ConditionalWeakTable<FrameworkElement, MotionTransforms>();

        // Animações em andamento, para cortar todas de uma vez quando o usuário desliga em Configurações
        private static readonly List<RunningAnimation> Running = new List<RunningAnimation>();

        private static bool _userEnabled = true;
        private static bool _weakHardware;
        private static bool _remoteSession;
        private static bool _initialized;

        /// <summary>Animações ativas agora: preferência do usuário e liberação do sistema.</summary>
        public static bool Enabled => AnimationPolicy.IsEnabled(_userEnabled, DisabledReason());

        /// <summary>Motivo automático para as animações estarem desligadas, ou null se o sistema as libera.</summary>
        public static string DisabledReason() =>
            AnimationPolicy.SystemBlockReason(SystemParameters.ClientAreaAnimation, _remoteSession, _weakHardware);

        /// <summary>Aplica a preferência salva e registra o hover/pressionado dos botões e cards (uma vez).</summary>
        public static void Initialize(bool userEnabled)
        {
            _remoteSession = GetSystemMetrics(SmRemoteSession) != 0;
            _weakHardware = AnimationPolicy.IsWeakHardware(RenderCapability.Tier, Environment.ProcessorCount);
            FreezeTokens();
            Configure(userEnabled);
            if (_initialized) return;
            _initialized = true;

            // Registro na classe: vale para todos os cards e botões, inclusive os criados depois
            RegisterHover(typeof(Wpf.Ui.Controls.Card));
            RegisterHover(typeof(Button));
        }

        /// <summary>Muda a preferência. Ao desligar, tudo que está rodando vai ao estado final na hora.</summary>
        public static void Configure(bool userEnabled)
        {
            _userEnabled = userEnabled;
            if (!Enabled) CancelAll();
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        // ---------- Transições de tela e cards ----------

        /// <summary>Troca de tela: fade e leve deslocamento vertical do conteúdo, depois a entrada dos cards visíveis.</summary>
        public static void PlayPageEnter(ContentControl host, TranslateTransform offset)
        {
            if (!Enabled || host == null || offset == null) return;
            var duration = Duration("MotionNormalMs", 220);
            Animate(host, UIElement.OpacityProperty, 1, duration, from: 0.6);
            Animate(offset, TranslateTransform.YProperty, 0, duration, from: Token("MotionPageOffsetPx", 8d));
            PlayCardEntrance(host);
        }

        /// <summary>Entrada escalonada dos primeiros cards visíveis dentro de <paramref name="root"/>.</summary>
        public static void PlayCardEntrance(FrameworkElement root)
        {
            if (!Enabled || root == null) return;
            var cards = new List<Wpf.Ui.Controls.Card>();
            var scanned = 0;
            CollectVisibleCards(root, cards, ref scanned);

            var duration = Duration("MotionNormalMs", 220);
            var step = Token("MotionStaggerStepMs", 30);
            var offset = Token("MotionCardOffsetPx", 10d);
            var index = 0;
            foreach (var card in cards)
            {
                if (index >= MaxStaggeredCards) break;
                var transforms = GetTransforms(card);
                if (transforms == null) continue;
                var delay = TimeSpan.FromMilliseconds(index * step);
                Animate(card, UIElement.OpacityProperty, 1, duration, delay, from: 0);
                Animate(transforms.Enter, TranslateTransform.YProperty, 0, duration, delay, from: offset);
                index++;
            }
        }

        /// <summary>Verdadeiro se o elemento aparece na área visível (ScrollViewer mais próximo, ou a janela).</summary>
        public static bool IsInViewport(FrameworkElement element)
        {
            if (element == null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);

            for (DependencyObject node = VisualTreeHelper.GetParent(element); node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is ScrollViewer scroll)
                {
                    var area = new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight);
                    return area.IntersectsWith(element.TransformToAncestor(scroll).TransformBounds(bounds));
                }
            }

            if (!(PresentationSource.FromVisual(element)?.RootVisual is FrameworkElement top)) return true;
            var window = new Rect(0, 0, top.ActualWidth, top.ActualHeight);
            return window.IntersectsWith(element.TransformToAncestor(top).TransformBounds(bounds));
        }

        // ---------- Recursos compartilhados ----------

        /// <summary>
        /// Anima uma propriedade double até <paramref name="to"/>. A propriedade fica no valor final ao terminar,
        /// sem deixar animação pendurada. Quando <paramref name="from"/> é null, parte do valor atual.
        /// Uma animação nova no mesmo alvo e propriedade substitui a anterior.
        /// </summary>
        internal static void Animate(DependencyObject target, DependencyProperty property, double to, TimeSpan duration,
            TimeSpan delay = default, double? from = null, Action completed = null)
        {
            var animatable = target as IAnimatable;
            if (animatable == null) return;

            var start = from ?? (double)target.GetValue(property);
            // Remove registros de alvos coletados, expirados e da mesma propriedade (substituídos: não terão Completed)
            Running.RemoveAll(r => r.Target == null || r.Expired() || (ReferenceEquals(r.Target, target) && r.Property == property));

            // Base igual ao início: sem salto visual durante o atraso (BeginTime)
            target.SetValue(property, start);

            var animation = new DoubleAnimation(start, to, duration)
            {
                BeginTime = delay,
                EasingFunction = Token("MotionEaseOut", FallbackEase),
                FillBehavior = FillBehavior.Stop,
            };
            var run = new RunningAnimation(target, property, to, completed, delay + duration);
            Running.Add(run);
            animation.Completed += (_, __) =>
            {
                // Se já foi cortada ou substituída, não aplica o estado final de novo
                if (!Running.Remove(run)) return;
                target.SetValue(property, to);
                animatable.BeginAnimation(property, null);
                completed?.Invoke();
            };
            animatable.BeginAnimation(property, animation);
        }

        /// <summary>Corta todas as animações em andamento: cada alvo vai ao valor final na hora, sem reiniciar o app.</summary>
        public static void CancelAll()
        {
            var snapshot = Running.ToArray();
            Running.Clear();
            foreach (var run in snapshot)
            {
                var target = run.Target;
                if (target == null) continue;
                Snap(target, run.Property, run.To);
                run.Completed?.Invoke();
            }
        }

        /// <summary>Define o valor final direto, sem animação, e remove qualquer animação da propriedade.</summary>
        internal static void Snap(DependencyObject target, DependencyProperty property, double to)
        {
            if (target is IAnimatable animatable) animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
        }

        internal static TimeSpan Duration(string key, int fallbackMs) => TimeSpan.FromMilliseconds(Token(key, fallbackMs));

        internal static T Token<T>(string key, T fallback) =>
            Application.Current?.TryFindResource(key) is T value ? value : fallback;

        /// <summary>Congela a curva compartilhada: recurso estático não precisa ser monitorado pelo WPF.</summary>
        private static void FreezeTokens()
        {
            if (Application.Current?.TryFindResource("MotionEaseOut") is Freezable ease && ease.CanFreeze && !ease.IsFrozen)
            {
                ease.Freeze();
            }
        }

        // ---------- Hover e pressionado ----------

        private static void RegisterHover(Type type)
        {
            EventManager.RegisterClassHandler(type, UIElement.MouseEnterEvent, new MouseEventHandler(OnHoverEnter));
            EventManager.RegisterClassHandler(type, UIElement.MouseLeaveEvent, new MouseEventHandler(OnHoverLeave));
            EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPressDown));
            EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnPressUp));
        }

        private static void OnHoverEnter(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement element)) return;
            var lift = element is Wpf.Ui.Controls.Card ? Token("MotionCardLiftPx", 2d) : Token("MotionButtonLiftPx", 1d);
            Move(element, -lift);
        }

        private static void OnHoverLeave(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement element)) return;
            Move(element, 0);
            Press(element, 1);
        }

        private static void OnPressDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element) Press(element, Token("MotionPressScale", 0.985d));
        }

        private static void OnPressUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element) Press(element, 1);
        }

        /// <summary>
        /// Leva o elemento até <paramref name="to"/>. Com animação desligada, ou com o elemento desabilitado,
        /// volta direto ao repouso, para nunca ficar "levantado" depois de desligar.
        /// </summary>
        private static void Move(FrameworkElement element, double to)
        {
            var active = Enabled && element.IsEnabled;
            var transforms = TransformsFor(element, needed: active && to != 0);
            if (transforms == null) return;

            if (active) Animate(transforms.Hover, TranslateTransform.YProperty, to, Duration("MotionFastMs", 150));
            else Snap(transforms.Hover, TranslateTransform.YProperty, 0);
        }

        private static void Press(FrameworkElement element, double scale)
        {
            var active = Enabled && element.IsEnabled;
            var transforms = TransformsFor(element, needed: active && scale != 1);
            if (transforms == null) return;

            if (active)
            {
                var duration = Duration("MotionFastMs", 150);
                Animate(transforms.Press, ScaleTransform.ScaleXProperty, scale, duration);
                Animate(transforms.Press, ScaleTransform.ScaleYProperty, scale, duration);
            }
            else
            {
                Snap(transforms.Press, ScaleTransform.ScaleXProperty, 1);
                Snap(transforms.Press, ScaleTransform.ScaleYProperty, 1);
            }
        }

        /// <summary>
        /// Transformações já criadas do elemento. Só cria as que vão ser usadas (<paramref name="needed"/>),
        /// para não mexer em elementos que nunca precisaram de movimento.
        /// </summary>
        private static MotionTransforms TransformsFor(FrameworkElement element, bool needed)
        {
            if (Transforms.TryGetValue(element, out var existing)) return existing;
            return needed ? GetTransforms(element) : null;
        }

        /// <summary>
        /// Transformações próprias do elemento (escala ao pressionar, deslocamento de entrada e de hover).
        /// Elementos que já têm uma transformação definida pelo XAML não recebem animação de movimento.
        /// </summary>
        private static MotionTransforms GetTransforms(FrameworkElement element)
        {
            if (Transforms.TryGetValue(element, out var existing)) return existing;
            if (element.RenderTransform != null && element.RenderTransform != Transform.Identity) return null;

            var created = new MotionTransforms
            {
                Press = new ScaleTransform(1, 1),
                Enter = new TranslateTransform(),
                Hover = new TranslateTransform(),
            };
            var group = new TransformGroup();
            group.Children.Add(created.Press);
            group.Children.Add(created.Enter);
            group.Children.Add(created.Hover);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = group;
            Transforms.Add(element, created);
            return created;
        }

        private static void CollectVisibleCards(DependencyObject node, List<Wpf.Ui.Controls.Card> found, ref int scanned)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count && scanned < ScanLimit; i++)
            {
                scanned++;
                var child = VisualTreeHelper.GetChild(node, i);
                if (!(child is UIElement element) || element.Visibility != Visibility.Visible) continue;

                if (child is Wpf.Ui.Controls.Card card)
                {
                    if (IsInViewport(card)) found.Add(card);
                    continue;
                }
                CollectVisibleCards(child, found, ref scanned);
            }
        }

        private static CubicEase CreateFallbackEase()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ease.Freeze();
            return ease;
        }

        private sealed class MotionTransforms
        {
            public ScaleTransform Press { get; set; }
            public TranslateTransform Enter { get; set; }
            public TranslateTransform Hover { get; set; }
        }

        /// <summary>
        /// Uma animação em andamento. Guarda o alvo com referência fraca e um prazo, para que registros
        /// de elementos descartados ou de animações que nunca completaram sejam podados.
        /// </summary>
        private sealed class RunningAnimation
        {
            private readonly WeakReference<DependencyObject> _target;
            private readonly int _deadline;

            public RunningAnimation(DependencyObject target, DependencyProperty property, double to, Action completed, TimeSpan length)
            {
                _target = new WeakReference<DependencyObject>(target);
                Property = property;
                To = to;
                Completed = completed;
                // Margem de 1 s além da duração, para não podar uma animação ainda em curso
                _deadline = Environment.TickCount + (int)length.TotalMilliseconds + 1000;
            }

            public DependencyProperty Property { get; }

            public double To { get; }

            public Action Completed { get; }

            public DependencyObject Target => _target.TryGetTarget(out var target) ? target : null;

            /// <summary>Comparação com sinal: segura mesmo quando o contador do sistema dá a volta.</summary>
            public bool Expired() => Environment.TickCount - _deadline > 0;
        }
    }
}
