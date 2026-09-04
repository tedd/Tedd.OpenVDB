// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Tests exercise the behaviors of upstream openvdb/unittest/TestNodeMask.cc.

using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tests;

public sealed class NodeMaskTests
{
    [Theory]
    [InlineData(1, 8, 1)]
    [InlineData(2, 64, 1)]
    [InlineData(3, 512, 8)]
    [InlineData(4, 4096, 64)]
    [InlineData(5, 32768, 512)]
    public void DimensionsAndDefaultStateMatchUpstream(int log2Dim, int size, int words)
    {
        var mask = new NodeMask(log2Dim);
        Assert.Equal(log2Dim, mask.Log2Dim);
        Assert.Equal(1 << log2Dim, mask.Dim);
        Assert.Equal(size, mask.Size);
        Assert.Equal(words, mask.WordCount);
        Assert.Equal(0, mask.CountOn());
        Assert.Equal(size, mask.CountOff());
        Assert.True(mask.IsOff());
        Assert.False(mask.IsOn());
        Assert.True(mask.IsConstant(out var state));
        Assert.False(state);
        Assert.Equal(size, mask.FindFirstOn());
        Assert.Equal(0, mask.FindFirstOff());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void FullMaskAndComplementNeverExposeUnusedBits(int log2Dim)
    {
        var mask = new NodeMask(log2Dim, true);
        Assert.True(mask.IsOn());
        Assert.False(mask.IsOff());
        Assert.True(mask.IsConstant(out var state));
        Assert.True(state);
        Assert.Equal(mask.Size, mask.CountOn());
        Assert.Equal(0, mask.CountOff());
        Assert.Equal(mask.Size, mask.FindFirstOff());
        Assert.Equal(Enumerable.Range(0, mask.Size), mask.OnIndices);
        Assert.Empty(mask.OffIndices);
        mask.Toggle();
        Assert.True(mask.IsOff());
        Assert.Empty(mask.OnIndices);
        Assert.Equal(Enumerable.Range(0, mask.Size), mask.OffIndices);
        mask.Toggle();
        Assert.True(mask.IsOn());
        Assert.Equal(mask.Size, mask.CountOn());
        if (log2Dim == 1) Assert.Equal(0xffUL, mask.Words[0]);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void EveryBitCanBeSetClearedAndToggledIndependently(int log2Dim)
    {
        var mask = new NodeMask(log2Dim);
        for (var index = 0; index < mask.Size; ++index)
        {
            mask.SetOn(index);
            Assert.True(mask.IsOn(index));
            Assert.False(mask.IsOff(index));
            Assert.Equal(1, mask.CountOn());
            Assert.Equal(index, mask.FindFirstOn());
            Assert.Equal(index, mask.FindNextOn(index));
            Assert.Equal(mask.Size, mask.FindNextOn(index + 1));
            mask.SetOff(index);
            Assert.True(mask.IsOff());
            mask.Toggle(index);
            Assert.True(mask[index]);
            mask.Toggle(index);
            Assert.False(mask.Get(index));
        }
    }

    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void WordEdgesHaveLeastSignificantBitFirstLayout(int log2Dim)
    {
        var mask = new NodeMask(log2Dim);
        mask.SetOn(0);
        mask.SetOn(63);
        mask.SetOn(64);
        mask.SetOn(127);
        mask.SetOn(mask.Size - 1);
        Assert.Equal(0x8000000000000001UL, mask.Words[0]);
        Assert.Equal(0x8000000000000001UL, mask.Words[1]);
        Assert.Equal(0x8000000000000000UL, mask.Words[^1]);
        Assert.Equal(new[] { 0, 63, 64, 127, mask.Size - 1 }, mask.OnIndices);
        Assert.Equal(63, mask.FindNextOn(1));
        Assert.Equal(64, mask.FindNextOn(64));
        Assert.Equal(127, mask.FindNextOn(65));
        Assert.Equal(mask.Size - 1, mask.FindNextOn(128));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void OffSearchIsInclusiveAndBounded(int log2Dim)
    {
        var mask = new NodeMask(log2Dim, true);
        mask.SetOff(0);
        mask.SetOff(mask.Size / 2);
        mask.SetOff(mask.Size - 1);
        Assert.Equal(0, mask.FindFirstOff());
        Assert.Equal(mask.Size / 2, mask.FindNextOff(1));
        Assert.Equal(mask.Size / 2, mask.FindNextOff(mask.Size / 2));
        Assert.Equal(mask.Size - 1, mask.FindNextOff(mask.Size / 2 + 1));
        Assert.Equal(mask.Size, mask.FindNextOff(mask.Size));
        Assert.Equal(mask.Size, mask.FindNextOn(int.MaxValue));
        Assert.Equal(mask.Size, mask.FindNextOff(int.MaxValue));
        Assert.Equal(new[] { 0, mask.Size / 2, mask.Size - 1 }, mask.OffIndices);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void FillSetAliasesAndBoundaryConveniencesPreserveCounts(int log2Dim)
    {
        var mask = new NodeMask(log2Dim);
        mask.SetFirstOn();
        mask.SetLastOn();
        Assert.Equal(2, mask.CountOn());
        Assert.False(mask.IsConstant(out _));
        mask.SetFirstOff();
        mask.SetLastOff();
        Assert.True(mask.IsOff());
        mask.SetOn();
        Assert.True(mask.IsOn());
        mask.SetOff();
        Assert.True(mask.IsOff());
        mask.Set(true);
        Assert.True(mask.IsOn());
        mask.Fill(false);
        mask[mask.Size / 2] = true;
        Assert.Equal(1, mask.CountOn());
        mask.Set(mask.Size / 2, false);
        Assert.True(mask.IsOff());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void BooleanOperationsMatchIndependentDenseModel(int log2Dim)
    {
        var random = new Random(0x51a7 + log2Dim);
        var left = new NodeMask(log2Dim);
        var right = new NodeMask(log2Dim);
        var a = new bool[left.Size];
        var b = new bool[right.Size];
        for (var i = 0; i < a.Length; ++i)
        {
            a[i] = random.Next(3) == 0;
            b[i] = random.Next(2) == 0;
            left.Set(i, a[i]);
            right.Set(i, b[i]);
        }
        var and = left & right;
        var or = left | right;
        var xor = left ^ right;
        var except = left - right;
        var not = ~left;
        var logicalNot = !left;
        for (var i = 0; i < a.Length; ++i)
        {
            Assert.Equal(a[i] && b[i], and[i]);
            Assert.Equal(a[i] || b[i], or[i]);
            Assert.Equal(a[i] != b[i], xor[i]);
            Assert.Equal(a[i] && !b[i], except[i]);
            Assert.Equal(!a[i], not[i]);
            Assert.Equal(!a[i], logicalNot[i]);
            Assert.Equal(a[i], left[i]);
            Assert.Equal(b[i], right[i]);
        }
        var copy = left.DeepCopy(); copy.AndWith(right); Assert.Equal(and, copy);
        copy = left.DeepCopy(); copy.OrWith(right); Assert.Equal(or, copy);
        copy = left.DeepCopy(); copy.XorWith(right); Assert.Equal(xor, copy);
        copy = left.DeepCopy(); copy.ExceptWith(right); Assert.Equal(except, copy);
        Assert.Equal(a.Count(static x => x), left.CountOn());
        Assert.Equal(Enumerable.Range(0, a.Length).Where(i => a[i]), left.EnumerateOn());
        Assert.Equal(Enumerable.Range(0, a.Length).Where(i => !a[i]), left.EnumerateOff());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void SelfAliasingBooleanOperationsAreDefined(int log2Dim)
    {
        var mask = new NodeMask(log2Dim);
        mask.SetOn(mask.Size / 3);
        var expected = mask.DeepCopy();
        mask.AndWith(mask); Assert.Equal(expected, mask);
        mask.OrWith(mask); Assert.Equal(expected, mask);
        mask.XorWith(mask); Assert.True(mask.IsOff());
        mask.SetOn();
        mask.ExceptWith(mask); Assert.True(mask.IsOff());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void RandomMutationsMatchDenseReference(int log2Dim)
    {
        var random = new Random(2112 + log2Dim);
        var mask = new NodeMask(log2Dim);
        var expected = new bool[mask.Size];
        for (var step = 0; step < 5_000; ++step)
        {
            var index = random.Next(mask.Size);
            switch (random.Next(4))
            {
                case 0: mask.SetOn(index); expected[index] = true; break;
                case 1: mask.SetOff(index); expected[index] = false; break;
                case 2: mask.Toggle(index); expected[index] = !expected[index]; break;
                default:
                    var state = random.Next(2) == 0;
                    mask.Set(index, state); expected[index] = state; break;
            }
            Assert.Equal(expected[index], mask[index]);
            if (step % 101 != 0) continue;
            Assert.Equal(expected.Count(static value => value), mask.CountOn());
            Assert.Equal(Enumerable.Range(0, mask.Size).Where(i => expected[i]), mask.OnIndices);
            Assert.Equal(Enumerable.Range(0, mask.Size).Where(i => !expected[i]), mask.OffIndices);
            var nextOn = Array.FindIndex(expected, index, static value => value);
            var nextOff = Array.FindIndex(expected, index, static value => !value);
            Assert.Equal(nextOn < 0 ? mask.Size : nextOn, mask.FindNextOn(index));
            Assert.Equal(nextOff < 0 ? mask.Size : nextOff, mask.FindNextOff(index));
        }
    }

    [Fact]
    public void CopiesAreIndependentAndEqualityIncludesDimensions()
    {
        var original = new NodeMask(3);
        original.SetOn(77);
        var copy = original.DeepCopy();
        Assert.Equal(original, copy);
        Assert.True(original == copy);
        Assert.False(original != copy);
        Assert.Equal(original.GetHashCode(), copy.GetHashCode());
        copy.Toggle(78);
        Assert.NotEqual(original, copy);
        Assert.False(original.Get(78));
        Assert.False(original.Equals(null));
        Assert.False(original.Equals((object)42));
        Assert.NotEqual(new NodeMask(2), new NodeMask(3));
        NodeMask? missing = null;
        Assert.True(missing == null);
        Assert.True(original != missing);
    }

    [Fact]
    public void EnumeratorsCanResetAndHaveStableExhaustion()
    {
        var mask = new NodeMask(3);
        mask.SetOn(12);
        mask.SetOn(500);
        var iterator = mask.EnumerateOn().GetEnumerator();
        Assert.True(iterator.MoveNext()); Assert.Equal(12, iterator.Current);
        Assert.True(iterator.MoveNext()); Assert.Equal(500, iterator.Current);
        Assert.False(iterator.MoveNext());
        Assert.False(iterator.MoveNext());
        iterator.Reset();
        Assert.True(iterator.MoveNext()); Assert.Equal(12, iterator.Current);
        iterator.Dispose();
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(6)] [InlineData(int.MaxValue)]
    public void UnsupportedDimensionsAreRejected(int log2Dim) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NodeMask(log2Dim));

    [Theory]
    [InlineData(-1)] [InlineData(512)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void InvalidIndicesThrowWithoutChangingMask(int index)
    {
        var mask = new NodeMask(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.Get(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.IsOn(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.IsOff(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.SetOn(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.SetOff(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.Set(index, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.Toggle(index));
        Assert.True(mask.IsOff());
    }

    [Fact]
    public void NegativeSearchOffsetsAndMismatchedMasksAreRejectedBeforeMutation()
    {
        var mask = new NodeMask(3, true);
        var other = new NodeMask(4);
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.FindNextOn(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.FindNextOff(-1));
        Assert.Throws<ArgumentException>(() => mask.AndWith(other));
        Assert.Throws<ArgumentException>(() => mask.OrWith(other));
        Assert.Throws<ArgumentException>(() => mask.XorWith(other));
        Assert.Throws<ArgumentException>(() => mask.ExceptWith(other));
        Assert.Throws<ArgumentNullException>(() => mask.AndWith(null!));
        Assert.True(mask.IsOn());
    }
}
