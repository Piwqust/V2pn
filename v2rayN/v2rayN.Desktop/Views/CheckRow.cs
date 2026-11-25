using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace v2rayN.Desktop.Views;

/// <summary>
/// V2pn: a row button that ticks on and off (the apps to move from, in the welcome card). It looks and styles
/// exactly like a Button (the "active" class draws the tick), and tells screen readers its state through the
/// Toggle pattern; a plain Button with a class showed the tick to the eye only.
/// </summary>
public sealed class CheckRow : Button
{
    public static readonly StyledProperty<bool> IsTickedProperty =
        AvaloniaProperty.Register<CheckRow, bool>(nameof(IsTicked));

    public bool IsTicked
    {
        get => GetValue(IsTickedProperty);
        set => SetValue(IsTickedProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsTickedProperty)
        {
            Classes.Set("active", IsTicked);
            (ControlAutomationPeer.FromElement(this) as Peer)?.Changed(change.GetOldValue<bool>(), change.GetNewValue<bool>());
        }
    }

    protected override void OnClick()
    {
        IsTicked = !IsTicked;
        base.OnClick();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(CheckRow owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.IsTicked ? ToggleState.On : ToggleState.Off;

        // Like a click, so whoever listens to Click (the move button's count) hears about it.
        public void Toggle() => owner.OnClick();

        public void Changed(bool was, bool now) => RaisePropertyChangedEvent(
            TogglePatternIdentifiers.ToggleStateProperty,
            was ? ToggleState.On : ToggleState.Off,
            now ? ToggleState.On : ToggleState.Off);
    }
}
