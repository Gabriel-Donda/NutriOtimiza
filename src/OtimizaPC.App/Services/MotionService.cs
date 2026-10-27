using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OtimizaPC.Core;
using OtimizaPC.Core.Settings;

namespace OtimizaPC.App.Services
{
    /// <summary>
    /// Animações do app: abertura da janela, transição de tela, entrada dos cards, hover/pressionado e mudanças de valor.
    /// Tudo passa por <see cref="Enabled"/>. As regras ficam em <see cref="AnimationPolicy"/> (Core).
    /// Os tokens (durações, curva, deslocamentos) ficam em Resources/Animations.xaml.
    /// </summary>
    public static class MotionService
    {
        /// <summary>Máximo de cards animados em uma entrada, para telas com muitos itens.</summary>
        private const int MaxStaggeredCards = 10;

        /// <summary>Limite de nós percorridos ao procurar cards, para não varrer listas enormes.</summary>
        private const int ScanLimit = 400;

        private const int SmRemoteSession = 0x1000;

        private static readonly CubicEase FallbackEase = CreateFallbackEase();
        private static readonly ConditionalWeakTable<FrameworkElement, MotionTransforms> Transforms = new ConditionalWeakTable<FrameworkElement, MotionTransforms>();

        // Animações em andamento, para cortar todas de uma vez quando o usuário desliga em Configurações
        private static readonly List<RunningAnimation> Running = new List<RunningAnimation>();

        // Cards com a borda acesa pelo hover (valor local de BorderBrush que precisa ser limpo)
        private static readonly List<WeakReference<Control>> Glowing = new List<WeakReference<Control>>();

        private static bool _userEnabled = true;
        private static bool _forceAnimations;
        private static bool _weakHardware;
        private static bool _remoteSession;
        private static int _renderTier;
        private static bool _initialized;

        /// <summary>O estado mudou (preferência, "Forçar" ou capacidade de renderização).</summary>
        public static event EventHandler StateChanged;

        /// <summary>Animações ativas agora: preferência do usuário e liberação do sistema.</summary>
        public static bool Enabled => AnimationPolicy.IsEnabled(_userEnabled, DisabledReason());

        /// <summary>Motivo automático para as animações estarem desligadas, ou null se o sistema as libera.</summary>
        public static string DisabledReason() =>
            AnimationPolicy.SystemBlockReason(SystemParameters.ClientAreaAnimation, _remoteSession, _weakHardware, _forceAnimations);

        /// <summary>"Animações ativas" ou "Desativadas: motivo", para Configurações.</summary>
        public static string StatusText => AnimationPolicy.StatusText(_userEnabled, DisabledReason());

        /// <summary>Verdadeiro quando só o hardware bloqueia (o "Forçar animações" resolveria).</summary>
        public static bool BlockedByHardware => _weakHardware;

        /// <summary>Lê "Forçar animações" do settings.json. Prefira a sobrecarga com os dois valores.</summary>
        public static void Initialize(bool userEnabled) => Initialize(userEnabled, new SettingsStore().Load().ForceAnimations);

        /// <summary>Aplica as preferências salvas, registra o estado no log e o hover/pressionado dos botões e cards (uma vez).</summary>
        public static void Initialize(bool userEnabled, bool forceAnimations)
        {
            _remoteSession = GetSystemMetrics(SmRemoteSession) != 0;
            ReadRenderTier();
            _forceAnimations = forceAnimations;
            FreezeTokens();
            _userEnabled = userEnabled;
            Log.Info(LogLine());
            if (_initialized) return;
            _initialized = true;

            // A placa de vídeo pode mudar (driver, acesso remoto): reavalia em vez de confiar só na leitura inicial
            RenderCapability.TierChanged += (_, __) =>
            {
                ReadRenderTier();
                Log.Info(LogLine());
                ApplyState();
            };

            // Registro na classe: vale para todos os cards e botões, inclusive os criados depois
            RegisterHover(typeof(Wpf.Ui.Controls.Card));
            RegisterHover(typeof(Button));
        }

        /// <summary>Muda a preferência. Ao desligar, tudo que está rodando vai ao estado final na hora.</summary>
        public static void Configure(bool userEnabled)
        {
            _userEnabled = userEnabled;
            ApplyState();
        }

        /// <summary>Liga ou desliga "Forçar animações" (ignora só o bloqueio de hardware).</summary>
        public static void ConfigureForce(bool forceAnimations)
        {
            _forceAnimations = forceAnimations;
            Log.Info(LogLine());
            ApplyState();
        }

        private static void ApplyState()
        {
            if (!Enabled) CancelAll();
            StateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void ReadRenderTier()
        {
            _renderTier = RenderCapability.Tier;
            _weakHardware = AnimationPolicy.IsWeakHardware(_renderTier);
        }

        private static string LogLine() => AnimationPolicy.LogLine(_userEnabled, DisabledReason(), _renderTier,
            Environment.ProcessorCount, SystemParameters.ClientAreaAnimation, _remoteSession, _forceAnimations);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        // ---------- Janela, transições de tela e cards ----------

        /// <summary>
        /// Antes do primeiro quadro: esconde o conteúdo para o fade de abertura. Window.Opacity só funciona com janela
        /// transparente, então o fade é no conteúdo, sobre o fundo da janela.
        /// </summary>
        public static void PrepareWindowEnter(UIElement content)
        {
            if (Enabled && content != null) content.Opacity = 0;
        }

        /// <summary>
        /// Depois do primeiro quadro (ContentRendered): o conteúdo surge em fade. Começar antes faria a animação
        /// correr durante a montagem da primeira tela e terminar sem ser vista.
        /// </summary>
        public static void PlayWindowEnter(UIElement content)
        {
            if (content == null) return;
            if (!Enabled)
            {
                content.ClearValue(UIElement.OpacityProperty);
                return;
            }
            Animate(content, UIElement.OpacityProperty, 1, Duration("MotionWindowMs", 260), from: 0, clearAtEnd: true);
        }

        /// <summary>Troca de tela: fade e deslocamento vertical do conteúdo, depois a entrada escalonada dos cards visíveis.</summary>
        public static void PlayPageEnter(ContentControl host, TranslateTransform offset)
        {
            if (!Enabled || host == null || offset == null) return;
            var duration = Duration("MotionPageMs", 280);
            Animate(host, UIElement.OpacityProperty, 1, duration, from: 0, clearAtEnd: true);
            Animate(offset, TranslateTransform.YProperty, 0, duration, from: Token("MotionPageOffsetPx", 24d));
            PlayCardEntrance(host);
        }

        /// <summary>Entrada escalonada dos primeiros cards visíveis dentro de <paramref name="root"/>.</summary>
        public static void PlayCardEntrance(FrameworkElement root)
        {
            if (!Enabled || root == null) return;
            var cards = new List<Wpf.Ui.Controls.Card>();
            var scanned = 0;
            CollectVisibleCards(root, cards, ref scanned);

            var duration = Duration("MotionPageMs", 280);
            var step = Token("MotionStaggerStepMs", 45);
            var offset = Token("MotionCardOffsetPx", 16d);
            var index = 0;
            foreach (var card in cards)
            {
                if (index >= MaxStaggeredCards) break;
                var transforms = GetTransforms(card);
                if (transforms == null) continue;
                var delay = TimeSpan.FromMilliseconds(index * step);
                Animate(card, UIElement.OpacityProperty, 1, duration, delay, from: 0, clearAtEnd: true);
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
        /// Com <paramref name="clearAtEnd"/>, o valor local é removido no fim e estilos/gatilhos voltam a valer.
        /// Uma animação nova no mesmo alvo e propriedade substitui a anterior.
        /// </summary>
        internal static void Animate(DependencyObject target, DependencyProperty property, double to, TimeSpan duration,
            TimeSpan delay = default, double? from = null, Action completed = null, bool clearAtEnd = false)
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
            var run = new RunningAnimation(target, property, to, completed, delay + duration, clearAtEnd);
            Running.Add(run);
            animation.Completed += (_, __) =>
            {
                // Se já foi cortada ou substituída, não aplica o estado final de novo
                if (!Running.Remove(run)) return;
                Finish(target, property, to, clearAtEnd);
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
                Finish(target, run.Property, run.To, run.ClearAtEnd);
                run.Completed?.Invoke();
            }

            foreach (var weak in Glowing.ToArray())
            {
                if (weak.TryGetTarget(out var card) && card.ReadLocalValue(Control.BorderBrushProperty) is SolidColorBrush) ClearGlow(card);
            }
            Glowing.Clear();
        }

        /// <summary>Define o valor final direto, sem animação, e remove qualquer animação da propriedade.</summary>
        internal static void Snap(DependencyObject target, DependencyProperty property, double to)
        {
            if (target is IAnimatable animatable) animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
        }

        private static void Finish(DependencyObject target, DependencyProperty property, double to, bool clear)
        {
            if (clear) target.ClearValue(property);
            else target.SetValue(property, to);
            (target as IAnimatable)?.BeginAnimation(property, null);
        }

        internal static TimeSpan Duration(string key, int fallbackMs) => TimeSpan.FromMilliseconds(Token(key, fallbackMs));

        internal static T Token<T>(string key, T fallback) =>
            Application.Current?.TryFindResource(key) is T value ? value : fallback;

        internal static IEasingFunction Ease => Token<IEasingFunction>("MotionEaseOut", FallbackEase);

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
            var isCard = element is Wpf.Ui.Controls.Card;
            Move(element, -(isCard ? Token("MotionCardLiftPx", 3d) : Token("MotionButtonLiftPx", 1d)));
            if (isCard) Glow((Control)element, on: true);
        }

        private static void OnHoverLeave(object sender, MouseEventArgs e)
        {
            if (!(sender is FrameworkElement element)) return;
            Move(element, 0);
            Press(element, 1);
            if (element is Wpf.Ui.Controls.Card card) Glow(card, on: false);
        }

        private static void OnPressDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element) Press(element, Token("MotionPressScale", 0.97d));
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

            if (active) Animate(transforms.Hover, TranslateTransform.YProperty, to, Duration("MotionNormalMs", 220));
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
        /// Borda do card acende na cor de destaque no hover e apaga ao sair. No fim o valor local é removido,
        /// e a borda volta a seguir o tema (e os gatilhos de estilo, como o foco por teclado).
        /// </summary>
        private static void Glow(Control card, bool on)
        {
            var accent = BrushColor("FocusStrokeBrush");
            var rest = BrushColor("CardStrokeColorDefaultBrush");
            if (accent == null || rest == null) return;

            var brush = OwnGlowBrush(card);
            // Borda definida pelo próprio XAML: não mexe, para não perder o valor dela ao limpar
            if (brush == null && card.ReadLocalValue(Control.BorderBrushProperty) != DependencyProperty.UnsetValue) return;

            if (!Enabled || !card.IsEnabled)
            {
                if (brush != null) ClearGlow(card);
                return;
            }

            var glow = accent.Value;
            glow.A = (byte)(255 * Math.Max(0, Math.Min(1, Token("MotionCardGlowOpacity", 0.85d))));

            // Pincel próprio (não congelado) por card, partindo da cor de repouso da borda
            if (brush == null)
            {
                if (!on) return;
                brush = new SolidColorBrush(rest.Value);
                card.BorderBrush = brush;
                Glowing.RemoveAll(w => !w.TryGetTarget(out var c) || ReferenceEquals(c, card));
                Glowing.Add(new WeakReference<Control>(card));
            }

            var animation = new ColorAnimation(on ? glow : rest.Value, Duration("MotionNormalMs", 220))
            {
                EasingFunction = Ease,
                FillBehavior = FillBehavior.HoldEnd,
            };
            if (!on)
            {
                animation.Completed += (_, __) =>
                {
                    // Só limpa se ninguém acendeu de novo no meio do caminho
                    if (ReferenceEquals(OwnGlowBrush(card), brush) && !card.IsMouseOver) ClearGlow(card);
                };
            }
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        /// <summary>O pincel de brilho que este serviço pôs no card, ou null se a borda não é nossa.</summary>
        private static SolidColorBrush OwnGlowBrush(Control card)
        {
            foreach (var weak in Glowing)
            {
                if (weak.TryGetTarget(out var c) && ReferenceEquals(c, card))
                {
                    return card.ReadLocalValue(Control.BorderBrushProperty) as SolidColorBrush;
                }
            }
            return null;
        }

        private static void ClearGlow(Control card)
        {
            card.ClearValue(Control.BorderBrushProperty);
            Glowing.RemoveAll(w => !w.TryGetTarget(out var c) || ReferenceEquals(c, card));
        }

        private static Color? BrushColor(string key) =>
            Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : (Color?)null;

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
        /// Uma transformação identidade (o Card do WPF-UI já vem com um MatrixTransform vazio) é substituída;
        /// elementos com transformação real definida pelo XAML não recebem animação de movimento.
        /// </summary>
        private static MotionTransforms GetTransforms(FrameworkElement element)
        {
            if (Transforms.TryGetValue(element, out var existing)) return existing;
            var current = element.RenderTransform;
            if (current != null && !(current.Value.IsIdentity && !current.HasAnimatedProperties)) return null;

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

            public RunningAnimation(DependencyObject target, DependencyProperty property, double to, Action completed, TimeSpan length, bool clearAtEnd)
            {
                _target = new WeakReference<DependencyObject>(target);
                Property = property;
                To = to;
                Completed = completed;
                ClearAtEnd = clearAtEnd;
                // Margem de 1 s além da duração, para não podar uma animação ainda em curso
                _deadline = Environment.TickCount + (int)length.TotalMilliseconds + 1000;
            }

            public DependencyProperty Property { get; }

            public double To { get; }

            public Action Completed { get; }

            public bool ClearAtEnd { get; }

            public DependencyObject Target => _target.TryGetTarget(out var target) ? target : null;

            /// <summary>Comparação com sinal: segura mesmo quando o contador do sistema dá a volta.</summary>
            public bool Expired() => Environment.TickCount - _deadline > 0;
        }
    }
}
