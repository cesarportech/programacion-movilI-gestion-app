using System.Diagnostics;

namespace GestionLibros.Views;

// Original vector animation inspired by a geometric morphing preloader.
// No web player, downloaded premium asset, or network dependency is used.
internal sealed class CloudLoadingOverlay : Grid
{
    private readonly GraphicsView figure;
    private readonly MorphDrawable drawing = new();
    private readonly Stopwatch elapsed = new();
    private IDispatcherTimer? timer;
    private readonly Label heading = ClassicWorkspaceChrome.Text("Cargando…", true);

    internal CloudLoadingOverlay(Label sourceStatus, Func<Task>? cancel = null)
    {
        IsVisible = false;
        BackgroundColor = Color.FromArgb("#ECE9D8");
        ZIndex = 10;
        figure = new GraphicsView { Drawable = drawing, WidthRequest = 150, HeightRequest = 150,
            HorizontalOptions = LayoutOptions.Center, InputTransparent = true };
        heading.FontSize = 16; heading.TextColor = Color.FromArgb("#000080");
        heading.HorizontalTextAlignment = TextAlignment.Center;
        var detail = ClassicWorkspaceChrome.Text("");
        detail.LineBreakMode = LineBreakMode.WordWrap; detail.HorizontalTextAlignment = TextAlignment.Center;
        detail.SetBinding(Label.TextProperty, new Binding(nameof(Label.Text), source: sourceStatus));
        var center = new VerticalStackLayout { Spacing = 16, Padding = 24,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 450, Children = { figure, heading, detail } };
        if (cancel != null)
        {
            var cancelButton = JournalsPage.ActionButton("Cancelar", "login_cancel.png", cancel, 100);
            cancelButton.HorizontalOptions = LayoutOptions.Center;
            center.Children.Add(cancelButton);
        }
        Add(center);
        SemanticProperties.SetDescription(heading, cancel == null ? "Iniciando FREDI. Verificando la sesión guardada." : "Sincronización en curso. Puede cancelar sin perder los cambios pendientes.");
        Unloaded += (_, _) => Stop();
    }

    internal void Start(string title = "Cargando…")
    {
        Stop(); IsVisible = true; heading.Text = title;
        drawing.Phase = 0; figure.Invalidate();
        var animate = true;
#if WINDOWS
        animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#endif
        if (!animate) return;
#if WINDOWS
        // GPU path: a looping rotation on the Windows compositor thread. It keeps moving even while the UI
        // thread is busy loading data, and costs the UI thread nothing (no per-frame redraw).
        if (StartCompositorSpin()) return;
#endif
        elapsed.Restart();
        timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += Tick; timer.Start();
    }
    internal void Cancelling() => heading.Text = "Cancelando…";
    internal void Stop()
    {
        if (timer != null) { timer.Stop(); timer.Tick -= Tick; timer = null; }
#if WINDOWS
        spinVisual?.StopAnimation("RotationAngleInDegrees");
        spinVisual = null;
#endif
        elapsed.Stop(); IsVisible = false;
    }

#if WINDOWS
    private Microsoft.UI.Composition.Visual? spinVisual;

    private bool StartCompositorSpin()
    {
        if (figure.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element) return false;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        // A fixed morph stage (rounded star) is drawn once; the compositor only rotates the bitmap.
        drawing.Phase = 0.2; figure.Invalidate();
        visual.CenterPoint = new System.Numerics.Vector3((float)figure.WidthRequest / 2, (float)figure.HeightRequest / 2, 0);
        var spin = compositor.CreateScalarKeyFrameAnimation();
        spin.InsertKeyFrame(0f, 0f);
        spin.InsertKeyFrame(1f, 360f, compositor.CreateLinearEasingFunction());
        spin.Duration = TimeSpan.FromSeconds(2.4);
        spin.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("RotationAngleInDegrees", spin);
        spinVisual = visual;
        return true;
    }
#endif
    private void Tick(object? sender, EventArgs e)
    {
        drawing.Phase = elapsed.Elapsed.TotalSeconds % 5 / 5;
        figure.Invalidate();
    }

    private sealed class MorphDrawable : IDrawable
    {
        public double Phase { get; set; }
        // Equal-angle boundary samples keep morphs continuous, including the loop seam.
        private static double Radius(int shape, double angle)
        {
            if (shape == 3) return .85; // circle
            if (shape == 1) return .70 + .25 * Math.Cos(4 * angle); // four-point star
            var sides = shape == 2 ? 3 : 4;
            var sector = Math.Tau / sides;
            var a = ((angle + Math.PI / sides) % sector + sector) % sector - Math.PI / sides;
            return Math.Cos(Math.PI / sides) / Math.Cos(a);
        }
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stage = Phase * 4;
            var from = (int)stage; var to = (from + 1) % 4;
            var t = stage - from; t = t * t * (3 - 2 * t);
            var scale = Math.Min(dirtyRect.Width, dirtyRect.Height) * .34f;
            var cx = dirtyRect.Center.X; var cy = dirtyRect.Center.Y;
            canvas.FillColor = Color.FromArgb("#91F4CE");
            canvas.FillCircle(cx, cy, scale * 1.4f);
            var path = new PathF();
            for (var i = 0; i < 120; i++)
            {
                var a = i * Math.Tau / 120;
                var r = Radius(from, a) * (1 - t) + Radius(to, a) * t;
                var rotated = a + Phase * Math.Tau - Math.PI / 4;
                var x = cx + (float)(Math.Cos(rotated) * r * scale);
                var y = cy + (float)(Math.Sin(rotated) * r * scale);
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            path.Close();
            canvas.FillColor = Color.FromArgb("#000080"); canvas.FillPath(path);
            canvas.StrokeColor = Color.FromArgb("#4F6FBF"); canvas.StrokeSize = 1;
            canvas.DrawPath(path);
        }
    }
}
