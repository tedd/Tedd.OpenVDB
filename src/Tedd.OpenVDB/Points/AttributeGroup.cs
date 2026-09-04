// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/AttributeGroup.h and AttributeGroup.cc.
namespace Tedd.OpenVDB.Points;

public readonly struct GroupCodec : IAttributeCodec<byte>
{
    public static string Name => "grp";
    public static int StorageSize => 1;
    public static void Encode(byte value, Span<byte> storage) => storage[0] = value;
    public static byte Decode(ReadOnlySpan<byte> storage) => storage[0];
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left[0] == right[0];
}
public sealed class GroupAttributeArray(int length = 1, int strideOrTotalSize = 1, bool constantStride = true, byte uniformValue = 0, int maximumStorageBytes = 268_435_456)
    : TypedAttributeArray<byte, GroupCodec>(length, strideOrTotalSize, constantStride, uniformValue, maximumStorageBytes)
{
    public static bool IsGroup(AttributeArray array) => array is TypedAttributeArray<byte, GroupCodec>;
}
public readonly record struct GroupIndex(int AttributeIndex, byte BitOffset);

/// <summary>Reads membership in one group or all bits in an explicit mask. An empty mask matches every valid index.</summary>
public class GroupHandle
{
    protected readonly TypedAttributeArray<byte, GroupCodec> Storage;
    protected readonly byte Mask;
    public int Size => Storage.Size;
    public bool IsUniform => Storage.IsUniform;
    public GroupHandle(TypedAttributeArray<byte, GroupCodec> array, int offset) : this(array, OffsetMask(offset), true) { }
    private protected GroupHandle(TypedAttributeArray<byte, GroupCodec> array, byte mask, bool _) { Storage = array ?? throw new ArgumentNullException(nameof(array)); Mask = mask; }
    public static GroupHandle FromBitMask(TypedAttributeArray<byte, GroupCodec> array, byte mask) => new(array, mask, true);
    public bool Get(int index) => (Storage.Get(index) & Mask) == Mask;
    public bool GetUnsafe(int index) => (Storage.GetUnsafe(index) & Mask) == Mask;
    private static byte OffsetMask(int offset)
    {
        if ((uint)offset >= 8) throw new ArgumentOutOfRangeException(nameof(offset));
        return (byte)(1 << offset);
    }
}
/// <summary>Updates one membership bit while preserving the seven neighboring groups.</summary>
public sealed class GroupWriteHandle(TypedAttributeArray<byte, GroupCodec> array, int offset) : GroupHandle(array, offset)
{
    public void Set(int index, bool on) { byte value = Storage.Get(index); Storage.Set(index, on ? (byte)(value | Mask) : (byte)(value & ~Mask)); }
    public void SetUnsafe(int index, bool on) { byte value = Storage.GetUnsafe(index); Storage.SetUnsafe(index, on ? (byte)(value | Mask) : (byte)(value & ~Mask)); }
    /// <summary>Native collapse compacts before changing the bit, and returns false if it had to visit expanded elements.</summary>
    /// <remarks>It does not compact again after the change. Native group attributes use stride one; for explicitly strided arrays only Size entries are changed.</remarks>
    public bool Collapse(bool on)
    {
        Storage.Compact();
        if (Storage.IsUniform)
        {
            byte value = Storage.Get(0); Storage.Collapse(on ? (byte)(value | Mask) : (byte)(value & ~Mask)); return true;
        }
        for (int i = 0; i < Storage.Size; i++) Set(i, on);
        return false;
    }
    public bool Compact() => Storage.Compact();
}
