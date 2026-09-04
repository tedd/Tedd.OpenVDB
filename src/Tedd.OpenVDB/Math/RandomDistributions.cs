// Copyright (c) Microsoft Corporation.
// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// Adapted distribution algorithms from Microsoft STL random/algorithm, with OpenVDB engine-copy ownership.
namespace Tedd.OpenVDB.Math;

/// <summary>OpenVDB Rand01 engine ownership with the pinned Microsoft STL canonical double distribution.</summary>
/// <remarks>The constructor copies its engine. Standard-library distribution bitstreams can differ on other native toolchains.</remarks>
public sealed class Random01
{
    private IRandomEngine32 _engine;
    public Random01(uint seed = 5489) => _engine = new Mt19937(seed);
    public Random01(IRandomEngine32 engine) { ArgumentNullException.ThrowIfNull(engine); _engine = engine.Clone(); }
    public IRandomEngine32 EngineSnapshot => _engine.Clone();
    public void SetSeed(uint seed) => _engine.Seed(seed);
    public double Next()
    {
        ulong low = _engine.NextUInt32() >> 11, high = (ulong)_engine.NextUInt32() << 21;
        return (low + high) * (1.0 / 9007199254740992.0);
    }
}
/// <summary>OpenVDB RandInt&lt;uint64&gt; engine-copy/range contract with the pinned Microsoft STL bounded distribution.</summary>
public sealed class RandomUInt64
{
    private IRandomEngine32 _engine;
    private ulong _minimum, _maximum;
    public RandomUInt64(uint seed, ulong minimum = 0, ulong maximum = ulong.MaxValue) { _engine = new Mt19937(seed); SetRange(minimum, maximum); }
    public RandomUInt64(IRandomEngine32 engine, ulong minimum = 0, ulong maximum = ulong.MaxValue) { ArgumentNullException.ThrowIfNull(engine); _engine = engine.Clone(); SetRange(minimum, maximum); }
    public IRandomEngine32 EngineSnapshot => _engine.Clone();
    public ulong Minimum => _minimum;
    public ulong Maximum => _maximum;
    public void SetSeed(uint seed) => _engine.Seed(seed);
    public void SetRange(ulong minimum, ulong maximum) { _minimum = System.Math.Min(minimum, maximum); _maximum = System.Math.Max(minimum, maximum); }
    public ulong Next() => Next(_minimum, _maximum);
    public ulong Next(ulong minimum, ulong maximum)
    {
        if (minimum > maximum) (minimum, maximum) = (maximum, minimum);
        ulong range = unchecked(maximum - minimum + 1);
        if (range == 0) return ((ulong)_engine.NextUInt32() << 32) | _engine.NextUInt32();
        if (range <= 0x100000000ul)
        {
            ulong threshold = (0x100000000ul - range) % range, product;
            do { product = _engine.NextUInt32() * range; } while ((product & uint.MaxValue) < threshold);
            return minimum + (product >> 32);
        }
        else
        {
            ulong threshold = unchecked(0ul - range) % range; UInt128 product;
            do { ulong sample = ((ulong)_engine.NextUInt32() << 32) | _engine.NextUInt32(); product = (UInt128)sample * range; } while (unchecked((ulong)product) < threshold);
            return minimum + (ulong)(product >> 64);
        }
    }
}
