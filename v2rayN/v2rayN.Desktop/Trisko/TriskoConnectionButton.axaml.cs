namespace v2rayN.Desktop.Trisko;

/// <summary>The shared connection action and animated contour for both window sizes.</summary>
public partial class TriskoConnectionButton : UserControl
{
    public static readonly StyledProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.Register<TriskoConnectionButton, bool>(nameof(IsAnimationEnabled), true);

    public bool IsAnimationEnabled
    {
        get => GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    public TriskoConnectionButton() => InitializeComponent();
}
