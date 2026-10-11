namespace Slides.Core;

// Live timing belongs to the current operator session, not the saved show plan.
public sealed class ServiceRun(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, TimeSpan> _itemElapsed = [];
    private long? _runningSince;
    private TimeSpan _serviceElapsed;

    public string? CurrentItemId { get; private set; }
    public bool IsRunning => _runningSince.HasValue;
    public TimeSpan ServiceElapsed => _serviceElapsed + SinceLastTick();
    public TimeSpan CurrentItemElapsed => CurrentItemId is null ? TimeSpan.Zero : ItemElapsed(CurrentItemId);

    public TimeSpan ItemElapsed(string itemId)
    {
        _itemElapsed.TryGetValue(itemId, out var stored);
        return stored + (CurrentItemId == itemId ? SinceLastTick() : TimeSpan.Zero);
    }

    public void Select(string? itemId)
    {
        if (itemId == CurrentItemId) return;
        Capture();
        CurrentItemId = itemId;
        if (IsRunning) _runningSince = _clock.GetTimestamp();
    }

    public void Start()
    {
        if (CurrentItemId is null || IsRunning) return;
        _runningSince = _clock.GetTimestamp();
    }

    public void Pause()
    {
        Capture();
        _runningSince = null;
    }

    public void Reset()
    {
        _runningSince = null;
        _serviceElapsed = TimeSpan.Zero;
        _itemElapsed.Clear();
    }

    private TimeSpan SinceLastTick() => _runningSince is { } since
        ? _clock.GetElapsedTime(since, _clock.GetTimestamp()) : TimeSpan.Zero;

    private void Capture()
    {
        if (!IsRunning) return;
        var elapsed = SinceLastTick();
        _serviceElapsed += elapsed;
        if (CurrentItemId is { } id)
        {
            _itemElapsed.TryGetValue(id, out var stored);
            _itemElapsed[id] = stored + elapsed;
        }
    }
}
