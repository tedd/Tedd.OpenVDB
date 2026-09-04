// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Standard MT19937 engine and OpenVDB math/Math.h engine-copy semantics.
namespace Tedd.OpenVDB.Math;

/// <summary>Cloneable generator whose output covers the full unsigned 32-bit range.</summary>
public interface IRandomEngine32
{
    void Seed(uint seed);
    uint NextUInt32();
    IRandomEngine32 Clone();
}
/// <summary>MT19937 with the standardized 32-bit state transition, seeding and tempering sequence.</summary>
public sealed class Mt19937 : IRandomEngine32
{
    private readonly uint[] _state;
    private int _index;
    public Mt19937(uint seed = 5489) { _state = GC.AllocateUninitializedArray<uint>(624); Seed(seed); }
    private Mt19937(uint[] state, int index) { _state = state; _index = index; }
    public void Seed(uint seed)
    {
        uint[] state = _state; state[0] = seed;
        for (uint i = 1; i < 624; ++i) state[i] = unchecked(1812433253u * (state[i - 1] ^ (state[i - 1] >> 30)) + i);
        _index = 624;
    }
    public Mt19937 Clone() => new((uint[])_state.Clone(), _index);
    IRandomEngine32 IRandomEngine32.Clone() => Clone();
    public uint NextUInt32()
    {
        uint[] state = _state;
        if (_index == 624)
        {
            for (int i = 0; i < 227; ++i) { uint mix = (state[i] & 0x80000000u) | (state[i + 1] & 0x7fffffffu); state[i] = state[i + 397] ^ (mix >> 1) ^ ((mix & 1) == 0 ? 0 : 0x9908b0dfu); }
            for (int i = 227; i < 623; ++i) { uint mix = (state[i] & 0x80000000u) | (state[i + 1] & 0x7fffffffu); state[i] = state[i - 227] ^ (mix >> 1) ^ ((mix & 1) == 0 ? 0 : 0x9908b0dfu); }
            uint last = (state[623] & 0x80000000u) | (state[0] & 0x7fffffffu); state[623] = state[396] ^ (last >> 1) ^ ((last & 1) == 0 ? 0 : 0x9908b0dfu); _index = 0;
        }
        uint value = state[_index++]; value ^= value >> 11; value ^= (value << 7) & 0x9d2c5680u; value ^= (value << 15) & 0xefc60000u; return value ^ (value >> 18);
    }
    public void Discard(ulong count) { for (ulong i = 0; i < count; ++i) NextUInt32(); }
}
