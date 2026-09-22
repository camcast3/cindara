using Cindara.Core.Diagnostics;
using Cindara.Desktop.Input;
using SDL3;

namespace Cindara.Desktop.Tests.Input;

public sealed class SdlGamepadInputSourceTests
{
    [Fact]
    public void DiagnosticsCaptureNativeFailureWithoutExceptionTextOrDeviceNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cindara-sdl-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var diagnostics = new LocalDiagnostics(directory);
            var backend = new FakeSdlGamepadBackend
            {
                InitializationException = new DllNotFoundException(@"C:\Users\private-user\private-runtime.dll"),
            };
            using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider(), diagnostics);
            input.Initialize();
            Assert.False(input.IsAvailable);
            Assert.Contains(diagnostics.RecentErrors, entry => entry.Errors.Contains("MissingNativeLibrary"));
            Assert.All(Directory.GetFiles(directory), file =>
                Assert.DoesNotContain("private", File.ReadAllText(file), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InitializeLoadsBundledNativeRuntime()
    {
        using var input = new SdlGamepadInputSource();

        input.Initialize();

        Assert.True(input.IsAvailable, input.InitializationError);
    }

    [Fact]
    public void InitializationEnumeratesConnectedDevicesAndIgnoresDuplicateAddEvents()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        var connections = new List<ControllerConnectionEventArgs>();
        input.ConnectionChanged += (_, args) => connections.Add(args);

        input.Initialize();
        input.Initialize();

        Assert.True(input.IsAvailable);
        Assert.Equal(1, input.ConnectedGamepads);
        Assert.Equal(1u, input.ActiveController?.Id);
        Assert.Equal(ControllerLayout.Xbox, Assert.Single(connections).Layout);
        Assert.Single(backend.Opened);
        Assert.Equal(1, backend.InitializeCount);
    }

    [Fact]
    public void FreshButtonSwitchesControllerAndRemovalFallsBackWithoutLosingNavigation()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        using var input = CreateActiveInput(backend);
        var actions = Listen(input);
        backend.Add(2, ControllerLayout.PlayStation);
        input.Poll();
        Assert.Equal(1u, input.ActiveController?.Id);

        backend.Button(2, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Equal(2u, input.ActiveController?.Id);
        Assert.Equal(ControllerLayout.PlayStation, input.ActiveController?.Layout);
        Assert.Equal(ControllerAction.Accept, Assert.Single(actions).Action);

        backend.DeviceEvent(2, SDL.EventType.GamepadRemoved);
        backend.Button(2, SDL.GamepadButton.East, true);
        backend.Button(1, SDL.GamepadButton.DPadDown, true);
        input.Poll();
        Assert.Equal(1u, input.ActiveController?.Id);
        Assert.Equal(1, input.ConnectedGamepads);
        Assert.Equal(2, actions.Count);
        Assert.Equal(ControllerAction.NavigateDown, actions[^1].Action);
        Assert.Contains((nint)2, backend.Closed);
    }

    [Fact]
    public void SourceStartsInactiveAndDiscardsQueuedBackgroundTapsOnActivation()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        var actions = Listen(input);
        input.Initialize();
        backend.Button(1, SDL.GamepadButton.South, true);
        backend.Button(1, SDL.GamepadButton.South, false);

        input.SetApplicationActive(true);
        input.Poll();
        Assert.Empty(actions);

        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Equal(ControllerAction.Accept, Assert.Single(actions).Action);
    }

    [Fact]
    public void ActivatingBeforeInitializationStillDiscardsStartupInput()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        backend.Button(1, SDL.GamepadButton.South, true);
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        var actions = Listen(input);
        input.SetApplicationActive(true);
        input.Initialize();
        Assert.Empty(actions);

        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Empty(actions);
        backend.Button(1, SDL.GamepadButton.South, false);
        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Single(actions);
    }

    [Fact]
    public void FocusResumeSuppressesHeldButtonEvenWhenNoDownEventWasDelivered()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        using var input = CreateActiveInput(backend);
        var actions = Listen(input);
        input.SetApplicationActive(false);
        backend.ButtonStates[(1, SDL.GamepadButton.South)] = true;
        input.SetApplicationActive(true);
        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Empty(actions);

        backend.Button(1, SDL.GamepadButton.South, false);
        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Single(actions);
    }

    [Fact]
    public void FocusResumeRequiresHeldStickToReturnToNeutral()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        var clock = new ManualInputTimeProvider();
        using var input = new SdlGamepadInputSource(backend, clock);
        input.Initialize();
        input.SetApplicationActive(true);
        var actions = Listen(input);
        backend.Axis(1, SDL.GamepadAxis.LeftX, 20_000);
        input.Poll();
        input.SetApplicationActive(false);
        clock.Advance(1_000);
        input.SetApplicationActive(true);
        input.Poll();
        backend.Axis(1, SDL.GamepadAxis.LeftX, -20_000);
        input.Poll();
        Assert.Single(actions);

        backend.Axis(1, SDL.GamepadAxis.LeftX, 0);
        backend.Axis(1, SDL.GamepadAxis.LeftX, -20_000);
        input.Poll();
        Assert.Equal(2, actions.Count);
        Assert.Equal(ControllerAction.NavigateLeft, actions[^1].Action);
        clock.Advance(400);
        input.Poll();
        Assert.True(actions[^1].IsRepeat);
        Assert.Equal(3, actions.Count);
    }

    [Fact]
    public void HotplugWhileInactiveStillUpdatesMetadataWithoutActivating()
    {
        var backend = new FakeSdlGamepadBackend();
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        var actions = Listen(input);
        input.Initialize();
        backend.Add(2, ControllerLayout.Nintendo);
        backend.Button(2, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Equal(1, input.ConnectedGamepads);
        Assert.Equal(ControllerLayout.Nintendo, input.ActiveController?.Layout);
        Assert.Empty(actions);

        input.SetApplicationActive(true);
        backend.Button(2, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Empty(actions);
        backend.Button(2, SDL.GamepadButton.South, false);
        backend.Button(2, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Single(actions);
    }

    [Fact]
    public void HotplugWithHeldAcceptRequiresReleaseBeforeActivation()
    {
        var backend = new FakeSdlGamepadBackend();
        using var input = CreateActiveInput(backend);
        var actions = Listen(input);
        backend.Add(1, ControllerLayout.Nintendo);
        backend.ButtonStates[(1, SDL.GamepadButton.South)] = true;
        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Empty(actions);

        backend.Button(1, SDL.GamepadButton.South, false);
        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Single(actions);
    }

    [Fact]
    public void RemappingUpdatesActiveLayoutAndSuppressesHeldControls()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Generic);
        using var input = CreateActiveInput(backend);
        var changes = 0;
        var actions = Listen(input);
        input.ActiveControllerChanged += (_, _) => changes++;
        backend.Controllers[1] = new ControllerInfo(1, "Remapped", ControllerLayout.Nintendo);
        backend.ButtonStates[(1, SDL.GamepadButton.South)] = true;
        backend.DeviceEvent(1, SDL.EventType.GamepadRemapped);
        backend.Button(1, SDL.GamepadButton.South, true);
        input.Poll();
        Assert.Equal(1, changes);
        Assert.Equal(ControllerLayout.Nintendo, input.ActiveController?.Layout);
        Assert.Empty(actions);
    }

    [Fact]
    public void ButtonReleaseStopsDirectionalRepeatAndUnknownDeviceInputIsIgnored()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        var clock = new ManualInputTimeProvider();
        using var input = new SdlGamepadInputSource(backend, clock);
        input.Initialize();
        input.SetApplicationActive(true);
        var actions = Listen(input);
        backend.Button(9, SDL.GamepadButton.South, true);
        backend.Button(1, SDL.GamepadButton.DPadRight, true);
        backend.Button(1, SDL.GamepadButton.DPadRight, true);
        input.Poll();
        Assert.Single(actions);
        clock.Advance(400);
        input.Poll();
        Assert.Equal(2, actions.Count);

        backend.Button(1, SDL.GamepadButton.DPadRight, false);
        input.Poll();
        clock.Advance(1_000);
        input.Poll();
        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public void FailedOpenAndUnknownRemovalDoNotPublishPhantomConnections()
    {
        var backend = new FakeSdlGamepadBackend { FailOpen = true };
        backend.Add(1, ControllerLayout.Xbox);
        backend.DeviceEvent(9, SDL.EventType.GamepadRemoved);
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        input.ConnectionChanged += (_, _) => Assert.Fail("No device was opened.");
        input.Initialize();
        Assert.Equal(0, input.ConnectedGamepads);
        Assert.Null(input.ActiveController);
    }

    [Fact]
    public void InitializationFailureIsReportedWithoutPollingOrQuittingNativeBackend()
    {
        var backend = new FakeSdlGamepadBackend { InitializeResult = false };
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        input.Initialize();
        input.Poll();
        input.SetApplicationActive(true);
        input.Dispose();
        Assert.False(input.IsAvailable);
        Assert.Contains("fake failure", input.InitializationError);
        Assert.Equal(0, backend.QuitCount);
        Assert.Equal(0, backend.PollCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrIncompatibleRuntimeDoesNotCrashStartup(bool missing)
    {
        var backend = new FakeSdlGamepadBackend
        {
            InitializationException = missing ? new DllNotFoundException() : new EntryPointNotFoundException(),
        };
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        input.Initialize();
        Assert.False(input.IsAvailable);
        Assert.NotNull(input.InitializationError);
    }

    [Fact]
    public void RuntimeFailureAfterInitializationDisablesPollingAndStillReleasesSubsystem()
    {
        var backend = new FakeSdlGamepadBackend { EnumerationException = new EntryPointNotFoundException() };
        using var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        input.Initialize();
        input.SetApplicationActive(true);
        input.Poll();
        Assert.False(input.IsAvailable);
        Assert.NotNull(input.InitializationError);
        Assert.Equal(0, backend.PollCount);
        input.Dispose();
        Assert.Equal(1, backend.QuitCount);
    }

    [Fact]
    public void DisposalClosesEveryHandleExactlyOnceAndRejectsFurtherUse()
    {
        var backend = new FakeSdlGamepadBackend();
        backend.Add(1, ControllerLayout.Xbox);
        backend.Add(2, ControllerLayout.PlayStation);
        var input = CreateActiveInput(backend);
        input.Dispose();
        input.Dispose();
        Assert.Equal(2, backend.Closed.Count);
        Assert.Equal(1, backend.QuitCount);
        Assert.Equal(0, input.ConnectedGamepads);
        Assert.Null(input.ActiveController);
        Assert.False(input.IsAvailable);
        Assert.Throws<ObjectDisposedException>(input.Initialize);
        Assert.Throws<ObjectDisposedException>(input.Poll);
        Assert.Throws<ObjectDisposedException>(() => input.SetApplicationActive(true));
    }

    private static SdlGamepadInputSource CreateActiveInput(FakeSdlGamepadBackend backend)
    {
        var input = new SdlGamepadInputSource(backend, new ManualInputTimeProvider());
        input.Initialize();
        input.SetApplicationActive(true);
        return input;
    }

    private static List<ControllerActionEventArgs> Listen(SdlGamepadInputSource input)
    {
        var actions = new List<ControllerActionEventArgs>();
        input.ActionPressed += (_, args) => actions.Add(args);
        return actions;
    }
}

internal sealed class FakeSdlGamepadBackend : ISdlGamepadBackend
{
    private readonly Queue<SDL.Event> _events = [];
    public Dictionary<uint, ControllerInfo> Controllers { get; } = [];
    public Dictionary<(uint Id, SDL.GamepadButton Button), bool> ButtonStates { get; } = [];
    private readonly Dictionary<(uint Id, SDL.GamepadAxis Axis), short> _axisStates = [];
    public List<nint> Opened { get; } = [];
    public List<nint> Closed { get; } = [];
    public int InitializeCount { get; private set; }
    public int PollCount { get; private set; }
    public int QuitCount { get; private set; }
    public bool InitializeResult { get; init; } = true;
    public bool FailOpen { get; init; }
    public Exception? InitializationException { get; init; }
    public Exception? EnumerationException { get; init; }

    public bool Initialize()
    {
        InitializeCount++;
        if (InitializationException is not null)
        {
            throw InitializationException;
        }

        return InitializeResult;
    }

    public string GetError() => "fake failure";

    public uint[] GetGamepads() => EnumerationException is null
        ? [.. Controllers.Keys]
        : throw EnumerationException;

    public bool PollEvent(out SDL.Event sdlEvent)
    {
        PollCount++;
        if (!_events.TryDequeue(out sdlEvent))
        {
            return false;
        }

        if ((SDL.EventType)sdlEvent.Type is SDL.EventType.GamepadButtonDown or SDL.EventType.GamepadButtonUp)
        {
            ButtonStates[(sdlEvent.GButton.Which, (SDL.GamepadButton)sdlEvent.GButton.Button)] =
                (SDL.EventType)sdlEvent.Type == SDL.EventType.GamepadButtonDown;
        }
        else if ((SDL.EventType)sdlEvent.Type == SDL.EventType.GamepadAxisMotion)
        {
            _axisStates[(sdlEvent.GAxis.Which, (SDL.GamepadAxis)sdlEvent.GAxis.Axis)] = sdlEvent.GAxis.Value;
        }

        return true;
    }

    public nint OpenGamepad(uint controllerId)
    {
        if (FailOpen)
        {
            return 0;
        }

        Opened.Add((nint)controllerId);
        return (nint)controllerId;
    }

    public void CloseGamepad(nint gamepad) => Closed.Add(gamepad);

    public ControllerInfo GetInfo(uint controllerId, nint gamepad) => Controllers[controllerId];

    public bool GetButton(nint gamepad, SDL.GamepadButton button) =>
        ButtonStates.GetValueOrDefault(((uint)gamepad, button));

    public short GetAxis(nint gamepad, SDL.GamepadAxis axis) =>
        _axisStates.GetValueOrDefault(((uint)gamepad, axis));

    public void Quit() => QuitCount++;

    public void Add(uint controllerId, ControllerLayout layout)
    {
        Controllers[controllerId] = new ControllerInfo(controllerId, $"Controller {controllerId}", layout);
        DeviceEvent(controllerId, SDL.EventType.GamepadAdded);
    }

    public void DeviceEvent(uint controllerId, SDL.EventType type) =>
        _events.Enqueue(new SDL.Event
        {
            GDevice = new SDL.GamepadDeviceEvent { Type = type, Which = controllerId },
        });

    public void Button(uint controllerId, SDL.GamepadButton button, bool isDown) =>
        _events.Enqueue(new SDL.Event
        {
            GButton = new SDL.GamepadButtonEvent
            {
                Type = isDown ? SDL.EventType.GamepadButtonDown : SDL.EventType.GamepadButtonUp,
                Which = controllerId,
                Button = (byte)button,
            },
        });

    public void Axis(uint controllerId, SDL.GamepadAxis axis, short value) =>
        _events.Enqueue(new SDL.Event
        {
            GAxis = new SDL.GamepadAxisEvent
            {
                Type = SDL.EventType.GamepadAxisMotion,
                Which = controllerId,
                Axis = (byte)axis,
                Value = value,
            },
        });
}
