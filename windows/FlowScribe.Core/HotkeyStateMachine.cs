namespace FlowScribe.Core;

public enum HotkeyEvent { Start, Finish, Cancel }

/// <summary>
/// The dictation key's behaviour, kept apart from the keyboard hook so it can
/// be tested. Same rules as the macOS HotkeyMonitor:
/// - Hold it: records while held, transcribes on release (push to talk).
/// - Double tap it: records hands free until tapped once more.
/// - Escape cancels a recording. Another key while holding it (a shortcut)
///   cancels too, so normal shortcuts still work.
/// </summary>
public sealed class HotkeyStateMachine
{
    enum State { Idle, Holding, WaitingForSecondTap, HandsFree }

    /// A press shorter than this is a tap, not a hold.
    public static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(300);
    /// The second tap of a double tap must arrive within this window.
    public static readonly TimeSpan DoubleTapWindow = TimeSpan.FromMilliseconds(350);

    readonly Func<TimeSpan, Action, IDisposable> _schedule;
    State _state = State.Idle;
    DateTime _pressedAt;
    IDisposable? _tapTimer;

    public event Action<HotkeyEvent>? Fired;

    /// <param name="schedule">Runs the action once after the delay, on the same
    /// thread as the key calls; disposing the result cancels it.</param>
    public HotkeyStateMachine(Func<TimeSpan, Action, IDisposable> schedule) => _schedule = schedule;

    public bool IsIdle => _state == State.Idle;

    public void KeyDown(DateTime now)
    {
        switch (_state)
        {
            case State.Idle:
                _state = State.Holding;
                _pressedAt = now;
                Fired?.Invoke(HotkeyEvent.Start);  // start at once so the first word is not clipped
                break;
            case State.WaitingForSecondTap:
                CancelTimer();
                _state = State.HandsFree;  // already recording since the first tap
                break;
            case State.HandsFree:
                _state = State.Idle;
                Fired?.Invoke(HotkeyEvent.Finish);
                break;
            case State.Holding:
                break;  // key repeat
        }
    }

    public void KeyUp(DateTime now)
    {
        if (_state != State.Holding) return;
        if (now - _pressedAt >= TapThreshold)
        {
            _state = State.Idle;
            Fired?.Invoke(HotkeyEvent.Finish);
            return;
        }
        // A short tap: wait briefly to see whether it is a double tap.
        _state = State.WaitingForSecondTap;
        CancelTimer();
        _tapTimer = _schedule(DoubleTapWindow, () =>
        {
            if (_state == State.WaitingForSecondTap) Cancel();  // a single accidental tap records nothing
        });
    }

    public void OtherKeyDown(bool isEscape)
    {
        if (isEscape && _state != State.Idle)
            Cancel();
        else if (_state is State.Holding or State.WaitingForSecondTap)
            Cancel();  // the key was used as a modifier for a shortcut, not to dictate
    }

    /// Called by the app when recording had to stop for another reason.
    public void Reset()
    {
        CancelTimer();
        _state = State.Idle;
    }

    void Cancel()
    {
        CancelTimer();
        _state = State.Idle;
        Fired?.Invoke(HotkeyEvent.Cancel);
    }

    void CancelTimer()
    {
        _tapTimer?.Dispose();
        _tapTimer = null;
    }
}
