using System.Diagnostics;

namespace v2rayN.Desktop.Geksa;

/// <summary>A continuous, time-based contour: no keyframe or loop-boundary jumps.</summary>
public sealed class GeksaConnectingOrb : Control
{
    public static readonly StyledProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.Register<GeksaConnectingOrb, bool>(nameof(IsAnimationEnabled));

    public static readonly StyledProperty<string> AccentColorProperty =
        AvaloniaProperty.Register<GeksaConnectingOrb, string>(nameof(AccentColor), "#72E5BD");

    public static readonly StyledProperty<string?> StateKeyProperty =
        AvaloniaProperty.Register<GeksaConnectingOrb, string?>(nameof(StateKey));

    public static readonly StyledProperty<bool> IsIdleProperty =
        AvaloniaProperty.Register<GeksaConnectingOrb, bool>(nameof(IsIdle), true);

    private const double BoostDurationSeconds = 0.5;
    private const double BoostPeakMultiplier = 8.5;
    private const double TransitionDurationSeconds = 0.35;

    private static readonly IBrush IdleBackgroundBrush = new SolidColorBrush(Color.Parse("#657184"));
    private static readonly IPen IdleBorderPen = new Pen(new SolidColorBrush(Color.Parse("#8A96A8")), 2.0);

    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer;
    private bool _attached;
    private OrbBrushes _brushes = CreateBrushes(Color.Parse("#72E5BD"));

    private double _animationTime;
    private double _lastClockSeconds;
    private double _boostStartClockSeconds = double.NegativeInfinity;

    private double _activityLevel;
    private double _targetActivity;

    public bool IsAnimationEnabled
    {
        get => GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    public string AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public string? StateKey
    {
        get => GetValue(StateKeyProperty);
        set => SetValue(StateKeyProperty, value);
    }

    public bool IsIdle
    {
        get => GetValue(IsIdleProperty);
        set => SetValue(IsIdleProperty, value);
    }

    public GeksaConnectingOrb()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _targetActivity = IsIdle ? 0.0 : 1.0;
        _activityLevel = _targetActivity;
        UpdateAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UpdateAnimation();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == IsAnimationEnabledProperty)
        {
            UpdateAnimation();
        }
        else if (change.Property == IsIdleProperty)
        {
            var isIdle = change.GetNewValue<bool>();
            _targetActivity = isIdle ? 0.0 : 1.0;
            if (!isIdle)
            {
                TriggerTransitionBoost();
            }
            UpdateAnimation();
            InvalidateVisual();
        }
        else if (change.Property == AccentColorProperty)
        {
            var oldColor = change.GetOldValue<string?>();
            var newColor = change.GetNewValue<string?>();
            if (!string.Equals(oldColor, newColor, StringComparison.OrdinalIgnoreCase))
            {
                _brushes = CreateBrushes(ParseAccentColor(newColor));
                if (!IsIdle)
                {
                    TriggerTransitionBoost();
                }
            }
        }
        else if (change.Property == StateKeyProperty)
        {
            var oldState = change.GetOldValue<string?>();
            var newState = change.GetNewValue<string?>();
            if (!string.Equals(oldState, newState, StringComparison.Ordinal))
            {
                if (!IsIdle)
                {
                    TriggerTransitionBoost();
                }
            }
        }
    }

    public void TriggerTransitionBoost()
    {
        _boostStartClockSeconds = _clock.Elapsed.TotalSeconds;
        InvalidateVisual();
    }

    private void UpdateAnimation()
    {
        bool shouldRun = _attached && IsVisible && IsAnimationEnabled && (!IsIdle || _activityLevel > 0.001);
        if (shouldRun)
        {
            if (!_clock.IsRunning)
            {
                _clock.Start();
                _lastClockSeconds = _clock.Elapsed.TotalSeconds;
            }
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _clock.Stop();
        }
    }

    private (double time, double boost, double activity) AdvanceAnimationTime()
    {
        var currentClock = _clock.Elapsed.TotalSeconds;
        var dt = currentClock - _lastClockSeconds;
        _lastClockSeconds = currentClock;

        if (dt < 0) dt = 0;
        else if (dt > 0.1) dt = 0.1;

        if (Math.Abs(_activityLevel - _targetActivity) > 0.0001)
        {
            var step = dt / TransitionDurationSeconds;
            if (_activityLevel < _targetActivity)
            {
                _activityLevel = Math.Min(_targetActivity, _activityLevel + step);
            }
            else
            {
                _activityLevel = Math.Max(_targetActivity, _activityLevel - step);
                if (_activityLevel <= 0.001 && IsIdle)
                {
                    _activityLevel = 0.0;
                    UpdateAnimation();
                }
            }
        }

        var elapsedSinceBoost = currentClock - _boostStartClockSeconds;
        var multiplier = 1.0;
        var boostStrength = 0.0;

        if (elapsedSinceBoost >= 0 && elapsedSinceBoost < BoostDurationSeconds)
        {
            var progress = elapsedSinceBoost / BoostDurationSeconds;
            boostStrength = Math.Pow(1.0 - progress, 1.25);
            multiplier = 1.0 + (BoostPeakMultiplier - 1.0) * boostStrength;
        }

        _animationTime += dt * multiplier * _activityLevel;
        return (_animationTime, boostStrength, _activityLevel);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var (time, boost, activity) = AdvanceAnimationTime();
        var buttonRadius = size * (88.0 / 244.0);

        // 1. Состояние покоя: серая круглая кнопка (плавно растворяется при активации)
        if (activity < 0.999)
        {
            using (context.PushOpacity(1.0 - activity))
            {
                context.DrawEllipse(IdleBackgroundBrush, IdleBorderPen, center, buttonRadius, buttonRadius);
            }
        }

        // 2. Активная анимация: орбиты, волны и ореол (плавно проявляются при активации)
        if (activity > 0.001)
        {
            using (context.PushOpacity(activity))
            {
                var haloRadius = size * (0.49 + 0.045 * boost);
                context.DrawEllipse(_brushes.Halo, null, center, haloRadius, haloRadius);
                context.DrawGeometry(_brushes.Outer, null, CreateContour(center, size * 0.405, time, 2.1, -0.22, boost, activity));
                context.DrawGeometry(_brushes.Inner, null, CreateContour(center, size * 0.382, time, 0.9, 0.28, boost, activity));
                context.DrawGeometry(_brushes.Body, null, CreateContour(center, size * 0.35, time, 0, 0.2, boost, activity));
            }
        }
    }

    private static OrbBrushes CreateBrushes(Color accent) => new(
            new RadialGradientBrush
            {
                Center = new RelativePoint(0.35, 0.28, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.35, 0.28, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Mix(accent, Colors.White, 0.42), 0),
                    new GradientStop(Mix(accent, Colors.White, 0.1), 0.45),
                    new GradientStop(Mix(accent, Colors.Black, 0.24), 1)
                }
            },
            new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(WithAlpha(accent, 0x50), 0),
                    new GradientStop(WithAlpha(accent, 0x28), 0.65),
                    new GradientStop(Color.Parse("#00000000"), 1)
                }
            },
            new SolidColorBrush(WithAlpha(Mix(accent, Colors.White, 0.06), 0x38)),
            new SolidColorBrush(WithAlpha(Mix(accent, Colors.White, 0.22), 0x52)));

    private static Color ParseAccentColor(string? value)
    {
        try
        {
            return Color.Parse(value ?? "#72E5BD");
        }
        catch (FormatException)
        {
            return Color.Parse("#72E5BD");
        }
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(first.R + (second.R - first.R) * amount),
            (byte)Math.Round(first.G + (second.G - first.G) * amount),
            (byte)Math.Round(first.B + (second.B - first.B) * amount));
    }

    private static StreamGeometry CreateContour(
        Point center,
        double radius,
        double time,
        double phase,
        double rotation,
        double boost,
        double activity)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();

        // During transition boost, wave deformation becomes deeper and breathing surges;
        // scaled by activity so waves emerge seamlessly from a circular contour.
        var waveAmp = (1.0 + 1.25 * boost) * activity;
        var breathingAmp = (1.0 + 1.5 * boost) * activity;

        // Dense samples keep the contour smooth; sine functions also keep velocity
        // continuous, including when the direction of deformation changes.
        for (var i = 0; i < 128; i++)
        {
            var angle = i * Math.Tau / 128;
            var wave = 1.0 + (0.055 * Math.Sin(3 * angle + time * 1.25 + phase)
                + 0.028 * Math.Sin(2 * angle - time * 0.85 + phase)
                + 0.012 * Math.Sin(5 * angle + time * 0.65)) * waveAmp;
            var breathing = 1.0 + 0.018 * Math.Sin(time * 1.45 + phase) * breathingAmp;
            var rotated = angle + time * rotation;
            var point = new Point(center.X + radius * wave * breathing * Math.Cos(rotated),
                center.Y + radius * wave * breathing * Math.Sin(rotated));
            if (i == 0) path.BeginFigure(point, true);
            else path.LineTo(point);
        }
        path.EndFigure(true);
        return geometry;
    }

    private sealed record OrbBrushes(IBrush Body, IBrush Halo, IBrush Outer, IBrush Inner);
}
