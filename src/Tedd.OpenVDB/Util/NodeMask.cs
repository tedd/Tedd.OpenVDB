// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/util/NodeMasks.h.

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tedd.OpenVDB.Util;

/// <summary>A compact activity mask using OpenVDB's least-significant-bit-first word layout.</summary>
/// <remarks>Instances are mutable and are not thread-safe. Enumeration requires no mutation of the mask.</remarks>
public sealed class NodeMask : IEquatable<NodeMask>
{
    private readonly ulong[] _words;
    private readonly ulong _lastWordMask;

    public NodeMask(int log2Dim, bool on = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(log2Dim, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(log2Dim, 5);
        Log2Dim = log2Dim;
        Size = 1 << (3 * log2Dim);
        _words = GC.AllocateUninitializedArray<ulong>((Size + 63) >> 6);
        _lastWordMask = Size < 64 ? (1UL << Size) - 1 : ulong.MaxValue;
        Fill(on); // Every element is initialized before the buffer is exposed.
    }

    private NodeMask(NodeMask other)
    {
        Log2Dim = other.Log2Dim;
        Size = other.Size;
        _lastWordMask = other._lastWordMask;
        _words = (ulong[])other._words.Clone();
    }

    public int Log2Dim { get; }
    public int Dim => 1 << Log2Dim;
    public int Size { get; }
    public int WordCount => _words.Length;
    public ReadOnlySpan<ulong> Words => _words;
    internal ulong[] Storage => _words;
    public IndexEnumerable OnIndices => new(this, true);
    public IndexEnumerable OffIndices => new(this, false);

    public bool this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsOn(index);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Set(index, value);
    }

    public NodeMask DeepCopy() => new(this);
    public IndexEnumerable EnumerateOn() => new(this, true);
    public IndexEnumerable EnumerateOff() => new(this, false);

    public void Fill(bool on)
    {
        _words.AsSpan().Fill(on ? ulong.MaxValue : 0UL);
        _words[^1] &= _lastWordMask;
    }

    public void Set(bool on) => Fill(on);
    public void SetOn() => Fill(true);
    public void SetOff() => Fill(false);
    public void SetFirstOn() => SetOn(0);
    public void SetLastOn() => SetOn(Size - 1);
    public void SetFirstOff() => SetOff(0);
    public void SetLastOff() => SetOff(Size - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOn(int index)
    {
        ValidateIndex(index);
        _words[index >> 6] |= 1UL << (index & 63);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOff(int index)
    {
        ValidateIndex(index);
        _words[index >> 6] &= ~(1UL << (index & 63));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index, bool on)
    {
        if (on) SetOn(index);
        else SetOff(index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsOn(int index)
    {
        ValidateIndex(index);
        return (_words[index >> 6] & (1UL << (index & 63))) != 0;
    }

    public bool Get(int index) => IsOn(index);
    public bool IsOff(int index) => !IsOn(index);

    public bool IsOn()
    {
        var words = _words.AsSpan();
        return !words[..^1].ContainsAnyExcept(ulong.MaxValue) && words[^1] == _lastWordMask;
    }

    public bool IsOff() => !_words.AsSpan().ContainsAnyExcept(0UL);

    public bool IsConstant(out bool on)
    {
        on = IsOn(0);
        return on ? IsOn() : IsOff();
    }

    public int CountOn()
    {
        var words = _words;
        var count = 0;
        for (var i = 0; i < words.Length; ++i)
            count += BitOperations.PopCount(words[i]);
        return count;
    }

    public int CountOff() => Size - CountOn();

    public void Toggle(int index)
    {
        ValidateIndex(index);
        _words[index >> 6] ^= 1UL << (index & 63);
    }

    public void Toggle()
    {
        var words = _words.AsSpan();
        var index = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var width = Vector<ulong>.Count;
            for (; index <= words.Length - width; index += width)
                (~new Vector<ulong>(words.Slice(index, width))).CopyTo(words.Slice(index, width));
        }
        for (; index < words.Length; ++index) words[index] = ~words[index];
        words[^1] &= _lastWordMask;
    }

    public int FindFirstOn() => FindNextOn(0);
    public int FindFirstOff() => FindNextOff(0);

    /// <summary>Finds the first on bit at or after start; returns Size when none exists.</summary>
    public int FindNextOn(int start) => FindNext(start, true);

    /// <summary>Finds the first off bit at or after start; returns Size when none exists.</summary>
    public int FindNextOff(int start) => FindNext(start, false);

    private int FindNext(int start, bool on)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (start >= Size) return Size;
        var words = _words;
        var wordIndex = start >> 6;
        var bits = ReadBits(wordIndex, on) & (ulong.MaxValue << (start & 63));
        while (true)
        {
            if (bits != 0) return (wordIndex << 6) + BitOperations.TrailingZeroCount(bits);
            if (++wordIndex >= words.Length) return Size;
            bits = ReadBits(wordIndex, on);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong ReadBits(int index, bool on)
    {
        var word = on ? _words[index] : ~_words[index];
        return index == _words.Length - 1 ? word & _lastWordMask : word;
    }

    public void AndWith(NodeMask other) => Apply(other, BinaryOperation.And);
    public void OrWith(NodeMask other) => Apply(other, BinaryOperation.Or);
    public void XorWith(NodeMask other) => Apply(other, BinaryOperation.Xor);
    public void ExceptWith(NodeMask other) => Apply(other, BinaryOperation.Except);

    private void Apply(NodeMask other, BinaryOperation operation)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Size != other.Size) throw new ArgumentException("Mask dimensions must match.", nameof(other));
        var destination = _words.AsSpan();
        var source = other._words.AsSpan();
        var index = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var width = Vector<ulong>.Count;
            for (; index <= destination.Length - width; index += width)
            {
                var left = new Vector<ulong>(destination.Slice(index, width));
                var right = new Vector<ulong>(source.Slice(index, width));
                var result = operation switch
                {
                    BinaryOperation.And => left & right,
                    BinaryOperation.Or => left | right,
                    BinaryOperation.Xor => left ^ right,
                    _ => left & ~right
                };
                result.CopyTo(destination.Slice(index, width));
            }
        }
        for (; index < destination.Length; ++index)
        {
            var left = destination[index];
            var right = source[index];
            destination[index] = operation switch
            {
                BinaryOperation.And => left & right,
                BinaryOperation.Or => left | right,
                BinaryOperation.Xor => left ^ right,
                _ => left & ~right
            };
        }
    }

    public static NodeMask operator &(NodeMask left, NodeMask right)
    {
        ArgumentNullException.ThrowIfNull(left);
        var result = left.DeepCopy();
        result.AndWith(right);
        return result;
    }

    public static NodeMask operator |(NodeMask left, NodeMask right)
    {
        ArgumentNullException.ThrowIfNull(left);
        var result = left.DeepCopy();
        result.OrWith(right);
        return result;
    }

    public static NodeMask operator ^(NodeMask left, NodeMask right)
    {
        ArgumentNullException.ThrowIfNull(left);
        var result = left.DeepCopy();
        result.XorWith(right);
        return result;
    }

    public static NodeMask operator -(NodeMask left, NodeMask right)
    {
        ArgumentNullException.ThrowIfNull(left);
        var result = left.DeepCopy();
        result.ExceptWith(right);
        return result;
    }

    public static NodeMask operator ~(NodeMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var result = mask.DeepCopy();
        result.Toggle();
        return result;
    }

    public static NodeMask operator !(NodeMask mask) => ~mask;

    public bool Equals(NodeMask? other) =>
        other is not null && Log2Dim == other.Log2Dim && _words.AsSpan().SequenceEqual(other._words);

    public override bool Equals(object? obj) => obj is NodeMask mask && Equals(mask);
    public static bool operator ==(NodeMask? left, NodeMask? right) => ReferenceEquals(left, right) || (left?.Equals(right) ?? false);
    public static bool operator !=(NodeMask? left, NodeMask? right) => !(left == right);

    /// <remarks>Do not mutate a mask while it is used as a hash key.</remarks>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Log2Dim);
        foreach (var word in _words) hash.Add(word);
        return hash.ToHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)Size) throw new ArgumentOutOfRangeException(nameof(index));
    }

    private enum BinaryOperation : byte { And, Or, Xor, Except }

    /// <summary>An allocation-free foreach view over the indices of bits in one state.</summary>
    public readonly struct IndexEnumerable : IEnumerable<int>
    {
        private readonly NodeMask _mask;
        private readonly bool _on;
        internal IndexEnumerable(NodeMask mask, bool on) { _mask = mask; _on = on; }
        public Enumerator GetEnumerator() => new(_mask, _on);
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct Enumerator : IEnumerator<int>
    {
        private readonly NodeMask _mask;
        private readonly bool _on;
        private int _wordIndex;
        private ulong _remaining;
        internal Enumerator(NodeMask mask, bool on)
        {
            _mask = mask;
            _on = on;
            _wordIndex = -1;
            _remaining = 0;
            Current = -1;
        }

        public int Current { get; private set; }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (_remaining == 0)
            {
                if (++_wordIndex >= _mask.WordCount)
                {
                    Current = _mask.Size;
                    return false;
                }
                _remaining = _mask.ReadBits(_wordIndex, _on);
            }
            Current = (_wordIndex << 6) + BitOperations.TrailingZeroCount(_remaining);
            _remaining &= _remaining - 1; // Remove the least significant set bit.
            return true;
        }

        public void Reset() { _wordIndex = -1; _remaining = 0; Current = -1; }
        public void Dispose() { }
    }
}
