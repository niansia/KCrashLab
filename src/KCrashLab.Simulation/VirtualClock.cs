namespace KCrashLab.Simulation;

public sealed class VirtualClock(DateTimeOffset epoch)
{
    public DateTimeOffset Epoch { get; } = epoch;

    public long ElapsedMilliseconds { get; private set; }

    public DateTimeOffset UtcNow => Epoch.AddMilliseconds(ElapsedMilliseconds);

    public void AdvanceTo(long elapsedMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMilliseconds);

        if (elapsedMilliseconds < ElapsedMilliseconds)
        {
            throw new InvalidOperationException("Virtual clock cannot move backwards.");
        }

        if (elapsedMilliseconds > (DateTimeOffset.MaxValue - Epoch).TotalMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds), "Virtual clock advance exceeds DateTimeOffset range.");
        }

        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public void Reset() => ElapsedMilliseconds = 0;
}
