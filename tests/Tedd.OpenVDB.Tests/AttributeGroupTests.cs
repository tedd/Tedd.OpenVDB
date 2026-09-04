// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Points;
namespace Tedd.OpenVDB.Tests;

public class AttributeGroupTests
{
    [Fact]
    public void HandlesPreserveOtherGroupsAndConstructionKeepsUniformStorage()
    {
        var array = new GroupAttributeArray(9, uniformValue: 0b10100101); var group = new GroupWriteHandle(array, 1);
        Assert.True(array.IsUniform); group.Set(3, true); Assert.Equal(0b10100111, array.Get(3)); Assert.Equal(0b10100101, array.Get(2));
        group.Set(3, false); Assert.Equal(0b10100101, array.Get(3)); Assert.True(group.Compact()); Assert.True(group.IsUniform);
    }
    [Fact]
    public void CollapseDoesNotPerformASecondCompactionAfterChangingMembership()
    {
        var array = new GroupAttributeArray(3); array.Set(1, 1); var group = new GroupWriteHandle(array, 0);
        Assert.False(group.Collapse(false)); Assert.False(array.IsUniform); Assert.Equal(0, array.Get(1)); Assert.True(group.Compact()); Assert.True(array.IsUniform);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(31)]
    [InlineData(32)]
    public void InvalidOffsetsRejectNativeUndefinedOrTruncatedShifts(int offset) => Assert.Throws<ArgumentOutOfRangeException>(() => new GroupHandle(new GroupAttributeArray(), offset));
    [Fact]
    public void EmptyAndMultibitMasksUseAllBitMembership()
    {
        var array = new GroupAttributeArray(3); array.Set(1, 3); array.Set(2, 1);
        var both = GroupHandle.FromBitMask(array, 3); Assert.False(both.Get(0)); Assert.True(both.Get(1)); Assert.False(both.Get(2));
        var empty = GroupHandle.FromBitMask(array, 0); Assert.True(empty.Get(0)); Assert.True(empty.Get(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Get(3));
    }
    [Fact]
    public void StridedGroupCollapsePreservesTheNativeSizeLoop()
    {
        var array = new GroupAttributeArray(2, 3); array.Set(5, 8); var group = new GroupWriteHandle(array, 0);
        Assert.False(group.Collapse(true)); Assert.Equal(1, array.Get(0)); Assert.Equal(1, array.Get(1)); Assert.Equal(0, array.Get(2)); Assert.Equal(8, array.Get(5));
    }
    [Fact]
    public void GroupCodecIdentityAndStreamPayloadAreRetained()
    {
        var array = new GroupAttributeArray(3, uniformValue: 0xA5); Assert.True(GroupAttributeArray.IsGroup(array)); Assert.False(GroupAttributeArray.IsGroup(new TypedAttributeArray<byte>()));
        using var stream = new MemoryStream(); AttributeStreams.Write(stream, array); stream.Position = 0;
        var restored = AttributeStreams.Read<byte, GroupCodec>(stream); Assert.True(GroupAttributeArray.IsGroup(restored)); Assert.True(array.StorageEquals(restored));
    }
}
