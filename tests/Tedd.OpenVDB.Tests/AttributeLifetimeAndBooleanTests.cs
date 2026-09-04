// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Points;
namespace Tedd.OpenVDB.Tests;

public class AttributeLifetimeAndBooleanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DisposedHandlesReleaseAttributesEvenWhenTheHandleRemainsReferenced(int kind)
    {
        var (handle, reference) = CreateHandle(kind); Assert.True(Alive(reference));
        handle.Dispose(); GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.False(Alive(reference)); GC.KeepAlive(handle);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Alive(WeakReference<AttributeArray> reference) => reference.TryGetTarget(out _);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IDisposable Handle, WeakReference<AttributeArray> Array) CreateHandle(int kind)
    {
        var array = new TypedAttributeArray<int>(10000); array.Expand();
        IDisposable handle = kind switch
        {
            0 => new AttributeHandle<int>(array),
            1 => new AttributeWriteHandle<int>(array),
            2 => new AttributeHandle<int, NullCodec<int>>(array),
            _ => new AttributeWriteHandle<int, NullCodec<int>>(array)
        };
        return (handle, new WeakReference<AttributeArray>(array));
    }
    [Theory]
    [InlineData(2)]
    [InlineData(127)]
    [InlineData(255)]
    public void RawBooleanCodecRejectsNoncanonicalClrRepresentations(byte encoded)
        => Assert.Throws<InvalidDataException>(() => NullCodec<bool>.Decode(new byte[] { encoded }));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BooleanAttributeStreamsRejectInvalidStorageBeforePublishingAnArray(bool uniform)
    {
        var array = new TypedAttributeArray<bool>(17, uniformValue: true); if (!uniform) array.Expand();
        using var stream = new MemoryStream(); AttributeStreams.Write(stream, array); byte[] bytes = stream.ToArray(); bytes[^1] = 2;
        Assert.Throws<InvalidDataException>(() => AttributeStreams.Read<bool>(new MemoryStream(bytes)));
    }
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ValidBooleanCodecValuesObeyClrEqualityAndLogic(byte encoded, bool expected)
    {
        bool value = NullCodec<bool>.Decode(new byte[] { encoded });
        Assert.Equal(expected, value); Assert.Equal(expected, value & true); Assert.Equal(expected, value.Equals(true));
    }
}
