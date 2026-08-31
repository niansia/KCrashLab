namespace KCrashLab.Domain;

internal sealed class DeterministicRandom(ulong state)
{
    private ulong state = state;

    public int Next(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exclusiveMaximum, 1);
        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    private ulong NextUInt64()
    {
        state += 0x9e3779b97f4a7c15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }
}
