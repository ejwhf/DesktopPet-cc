namespace DesktopPet.Core.Behavior;

/// <summary>
/// Small, platform-independent PRNG. A fixed seed and request sequence produce the same choices.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        // SplitMix64 permits a public seed of zero while avoiding an all-zero generator state.
        _state = seed + 0x9E3779B97F4A7C15UL;
        _state = Mix(_state);
    }

    public ulong NextUInt64()
    {
        // xorshift64*; constants and overflow behavior are explicitly fixed.
        var value = _state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        _state = value;
        return unchecked(value * 0x2545F4914F6CDD1DUL);
    }

    public double NextUnitDouble()
    {
        // Use the high 53 bits, matching the precision of an IEEE-754 double mantissa.
        return (NextUInt64() >> 11) * (1.0 / 9_007_199_254_740_992.0);
    }

    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
