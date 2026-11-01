using FlowScribe.Core;

namespace FlowScribe.Core.Tests;

public class HotkeyStateMachineTests
{
    // A timer that only fires when the test says so.
    sealed class FakeTimer : IDisposable
    {
        public Action? Action;
        public void Dispose() => Action = null;
    }

    readonly List<HotkeyEvent> _events = [];
    FakeTimer? _timer;
    readonly HotkeyStateMachine _keys;
    readonly DateTime _t0 = new(2026, 1, 1);

    public HotkeyStateMachineTests()
    {
        _keys = new HotkeyStateMachine((_, action) => _timer = new FakeTimer { Action = action });
        _keys.Fired += _events.Add;
    }

    DateTime At(int ms) => _t0.AddMilliseconds(ms);
    void FireTimer() => _timer?.Action?.Invoke();

    [Fact]
    public void HoldStartsAndReleaseFinishes()
    {
        _keys.KeyDown(At(0));
        _keys.KeyUp(At(1500));
        Assert.Equal([HotkeyEvent.Start, HotkeyEvent.Finish], _events);
        Assert.True(_keys.IsIdle);
    }

    [Fact]
    public void KeyRepeatWhileHoldingIsIgnored()
    {
        _keys.KeyDown(At(0));
        _keys.KeyDown(At(500));
        _keys.KeyDown(At(1000));
        _keys.KeyUp(At(1500));
        Assert.Equal([HotkeyEvent.Start, HotkeyEvent.Finish], _events);
    }

    [Fact]
    public void SingleShortTapCancels()
    {
        _keys.KeyDown(At(0));
        _keys.KeyUp(At(100));
        FireTimer();
        Assert.Equal([HotkeyEvent.Start, HotkeyEvent.Cancel], _events);
    }

    [Fact]
    public void DoubleTapIsHandsFreeUntilNextTap()
    {
        _keys.KeyDown(At(0));
        _keys.KeyUp(At(100));
        _keys.KeyDown(At(250));
        _keys.KeyUp(At(330));
        FireTimer();  // the cancelled timer must do nothing
        Assert.Equal([HotkeyEvent.Start], _events);
        _keys.KeyDown(At(60_000));
        _keys.KeyUp(At(60_100));
        Assert.Equal([HotkeyEvent.Start, HotkeyEvent.Finish], _events);
    }

    [Fact]
    public void EscapeCancelsHandsFree()
    {
        _keys.KeyDown(At(0));
        _keys.KeyUp(At(100));
        _keys.KeyDown(At(250));
        _keys.OtherKeyDown(isEscape: true);
        Assert.Equal([HotkeyEvent.Start, HotkeyEvent.Cancel], _events);
    }

    [Fact]
    public void OtherKeyWhileHoldingCancelsSoShortcutsWork()
    {
        _keys.KeyDown(At(0));
        _keys.OtherKeyDown(isEscape: false);
        _keys.KeyUp(At(800));
        Assert.Equal([HotkeyEvent.Start, HotkeyEvent.Cancel], _events);
    }

    [Fact]
    public void TypingDuringHandsFreeDoesNotCancel()
    {
        _keys.KeyDown(At(0));
        _keys.KeyUp(At(100));
        _keys.KeyDown(At(250));
        _keys.OtherKeyDown(isEscape: false);
        Assert.Equal([HotkeyEvent.Start], _events);
    }

    [Fact]
    public void OtherKeysWhenIdleDoNothing()
    {
        _keys.OtherKeyDown(isEscape: true);
        _keys.OtherKeyDown(isEscape: false);
        Assert.Empty(_events);
    }
}
