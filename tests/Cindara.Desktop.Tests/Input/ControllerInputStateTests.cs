using Cindara.Desktop.Input;

namespace Cindara.Desktop.Tests.Input;

public sealed class ControllerInputStateTests
{
    private readonly ManualInputTimeProvider _clock = new();

    [Fact]
    public void FreshInputSwitchesControllerBeforeDeliveringAction()
    {
        var state = CreateState();
        var second = new ControllerInfo(2, "Second", ControllerLayout.PlayStation);
        state.Connect(second);
        var events = new List<string>();
        state.ActiveControllerChanged += (_, _) => events.Add("controller");
        state.ActionPressed += (_, args) =>
        {
            Assert.Equal(second, state.ActiveController);
            Assert.Equal(2u, args.ControllerId);
            events.Add("action");
        };

        state.SetControl(2, 0, ControllerAction.Accept);

        Assert.Collection(events,
            value => Assert.Equal("controller", value),
            value => Assert.Equal("action", value));
    }

    [Fact]
    public void DirectionRepeatsAfterDelayWithoutCatchUpBursts()
    {
        var state = CreateState();
        var actions = Listen(state);
        state.SetControl(1, 0, ControllerAction.NavigateRight);
        _clock.Advance(399);
        state.Poll();
        Assert.Single(actions);

        _clock.Advance(1);
        state.Poll();
        Assert.Equal(2, actions.Count);
        Assert.False(actions[0].IsRepeat);
        Assert.True(actions[1].IsRepeat);

        _clock.Advance(99);
        state.Poll();
        Assert.Equal(2, actions.Count);
        _clock.Advance(1);
        state.Poll();
        Assert.Equal(3, actions.Count);
        _clock.Advance(5_000);
        state.Poll();
        state.Poll();
        Assert.Equal(4, actions.Count);

        state.SetControl(1, 0, null);
        _clock.Advance(1_000);
        state.Poll();
        Assert.Equal(4, actions.Count);
    }

    [Theory]
    [InlineData(ControllerAction.Accept)]
    [InlineData(ControllerAction.Back)]
    [InlineData(ControllerAction.Menu)]
    public void NonDirectionalActionsNeverRepeat(ControllerAction action)
    {
        var state = CreateState();
        var actions = Listen(state);
        state.SetControl(1, 0, action);
        state.SetControl(1, 0, action);
        _clock.Advance(5_000);
        state.Poll();
        Assert.Single(actions);

        state.SetControl(1, 0, null);
        state.SetControl(1, 0, action);
        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public void InactiveControllersCannotStealActiveControllerWithRepeats()
    {
        var state = CreateState();
        var actions = Listen(state);
        state.Connect(new ControllerInfo(2, "Second", ControllerLayout.Nintendo));
        state.SetControl(1, 0, ControllerAction.NavigateRight);
        state.SetControl(2, 0, ControllerAction.Accept);
        _clock.Advance(1_000);
        state.Poll();
        Assert.Equal(2, actions.Count);
        Assert.Equal(2u, state.ActiveController?.Id);

        state.SetControl(2, 1, ControllerAction.NavigateDown);
        _clock.Advance(400);
        state.Poll();
        Assert.Equal(ControllerAction.NavigateDown, actions[^1].Action);
        Assert.True(actions[^1].IsRepeat);
        Assert.Equal(2u, actions[^1].ControllerId);
    }

    [Fact]
    public void FocusLossSuppressesHeldAxesUntilNeutralEvenIfDirectionChanges()
    {
        var state = CreateState();
        var actions = Listen(state);
        state.SetControl(1, 0, ControllerAction.NavigateRight);
        state.SetApplicationActive(false);
        _clock.Advance(1_000);
        state.Poll();
        state.SetApplicationActive(true);
        state.Poll();
        state.SetControl(1, 0, ControllerAction.NavigateLeft);
        Assert.Single(actions);

        state.SetControl(1, 0, null);
        state.SetControl(1, 0, ControllerAction.NavigateLeft);
        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public void BackgroundInputsDoNotSwitchControllerOrActivateOnResume()
    {
        var state = CreateState();
        var actions = Listen(state);
        state.Connect(new ControllerInfo(2, "Second", ControllerLayout.Nintendo));
        state.SetApplicationActive(false);
        state.SetControl(2, 0, ControllerAction.Accept);
        state.SetApplicationActive(true);
        state.SetControl(2, 0, ControllerAction.Accept);
        Assert.Empty(actions);
        Assert.Equal(1u, state.ActiveController?.Id);

        state.SetControl(2, 0, null);
        state.SetControl(2, 0, ControllerAction.Accept);
        Assert.Single(actions);
        Assert.Equal(2u, state.ActiveController?.Id);
    }

    [Fact]
    public void DisconnectDropsHeldStateAndFallsBackWithoutBlockingNewInput()
    {
        var state = CreateState();
        var actions = Listen(state);
        state.Connect(new ControllerInfo(2, "Second", ControllerLayout.Xbox));
        state.SetControl(1, 0, ControllerAction.NavigateRight);
        state.Disconnect(1);
        _clock.Advance(1_000);
        state.Poll();
        Assert.Single(actions);
        Assert.Equal(2u, state.ActiveController?.Id);
        state.SetControl(1, 0, ControllerAction.Accept);
        Assert.Single(actions);

        state.SetControl(2, 0, ControllerAction.Accept);
        Assert.Equal(2, actions.Count);
        state.Disconnect(2);
        Assert.Null(state.ActiveController);
        state.Connect(new ControllerInfo(1, "Reconnected", ControllerLayout.Xbox));
        state.SetControl(1, 0, ControllerAction.NavigateRight);
        Assert.Equal(3, actions.Count);
    }

    [Fact]
    public void MostRecentHeldDirectionRepeatsAndFallsBackAfterRelease()
    {
        var state = CreateState();
        var actions = Listen(state);
        state.SetControl(1, 0, ControllerAction.NavigateRight);
        state.SetControl(1, 1, ControllerAction.NavigateDown);
        _clock.Advance(400);
        state.Poll();
        Assert.Equal(ControllerAction.NavigateDown, actions[^1].Action);
        Assert.Equal(3, actions.Count);

        state.SetControl(1, 1, null);
        state.Poll();
        Assert.Equal(ControllerAction.NavigateRight, actions[^1].Action);
        Assert.Equal(4, actions.Count);
    }

    private ControllerInputState CreateState()
    {
        var state = new ControllerInputState(_clock);
        state.Connect(new ControllerInfo(1, "First", ControllerLayout.Xbox));
        state.SetApplicationActive(true);
        return state;
    }

    private static List<ControllerActionEventArgs> Listen(ControllerInputState state)
    {
        var actions = new List<ControllerActionEventArgs>();
        state.ActionPressed += (_, args) => actions.Add(args);
        return actions;
    }
}

internal sealed class ManualInputTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => 1_000;

    public override long GetTimestamp() => _timestamp;

    public void Advance(long milliseconds) => _timestamp += milliseconds;
}
