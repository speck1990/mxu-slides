namespace Slides.Core;

// A live operator timer; it does not change a saved show or audience output.
public sealed class CountdownTimer(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long? _startedAt;
    private TimeSpan _elapsed;

    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(5);
    public bool IsRunning => _startedAt.HasValue;
    public TimeSpan Elapsed => _elapsed + (_startedAt is { } start
        ? _clock.GetElapsedTime(start, _clock.GetTimestamp()) : TimeSpan.Zero);
    public TimeSpan Remaining => Duration > Elapsed ? Duration - Elapsed : TimeSpan.Zero;
    public bool IsComplete => Elapsed >= Duration;

    public void SetDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(8))
            throw new ArgumentOutOfRangeException(nameof(duration), "Choose a timer between one second and eight hours.");
        Duration = duration;
        Reset();
    }

    public void Start()
    {
        if (IsRunning) return;
        if (IsComplete) Reset();
        _startedAt = _clock.GetTimestamp();
    }

    public void Pause()
    {
        if (_startedAt is not { } start) return;
        _elapsed += _clock.GetElapsedTime(start, _clock.GetTimestamp());
        _startedAt = null;
    }

    public void Reset()
    {
        _startedAt = null;
        _elapsed = TimeSpan.Zero;
    }
}
