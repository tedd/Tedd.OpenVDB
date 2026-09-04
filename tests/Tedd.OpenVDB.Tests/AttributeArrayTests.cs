// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;
namespace Tedd.OpenVDB.Tests;

public class AttributeArrayTests
{
    private readonly record struct CustomAttribute(int Id, float Weight);
    [Fact]
    public void CustomUnmanagedStructuresRetainNativeClassTraits()
    {
        var array = new TypedAttributeArray<CustomAttribute>(3, uniformValue: new(17, .5f));
        Assert.True(array.ValueTypeIsClass); Assert.False(array.ValueTypeIsFloatingPoint); Assert.False(array.ValueTypeIsVector);
        Assert.Equal(new CustomAttribute(17, .5f), array.Get(2)); Assert.False(new TypedAttributeArray<Half>().ValueTypeIsClass);
    }
    [Theory]
    [InlineData(0, 1, true, 1, 1)]
    [InlineData(0, 0, false, 1, 1)]
    [InlineData(3, 2, true, 3, 6)]
    [InlineData(3, 7, false, 3, 7)]
    public void ConstructorNormalizesNativeLengthsAndStrides(int n, int stride, bool constant, int size, int count)
    {
        var a = new TypedAttributeArray<int>(n, stride, constant, 17); Assert.True(a.IsUniform);
        Assert.Equal(size, a.Size); Assert.Equal(count, a.DataSize); Assert.Equal(constant ? System.Math.Max(1, stride) : 0, a.Stride);
        for (int i = 0; i < count; i++) Assert.Equal(17, a.Get(i)); Assert.Equal(4, a.StorageByteCount);
    }
    [Theory]
    [InlineData(-1, 1, true)]
    [InlineData(1, -1, true)]
    [InlineData(1, 0, true)]
    [InlineData(3, 2, false)]
    public void InvalidNativeShapesAreRejected(int n, int stride, bool constant) => Assert.Throws<ArgumentOutOfRangeException>(() => new TypedAttributeArray<int>(n, stride, constant));
    [Fact]
    public void ShapeOverflowAndStorageBudgetFailBeforeExpansion()
    {
        Assert.Throws<OverflowException>(() => new TypedAttributeArray<int>(int.MaxValue, 2));
        var array = new TypedAttributeArray<int>(1000, uniformValue: 7, maximumStorageBytes: 64);
        Assert.Throws<InvalidOperationException>(() => array.Expand()); Assert.True(array.IsUniform); Assert.Equal(4, array.StorageByteCount); Assert.Equal(7, array.Get(999));
    }
    [Fact]
    public void SetOfUnchangedValueStillExpandsAndFillRetainsShape()
    {
        var a = new TypedAttributeArray<int>(4, uniformValue: 3); a.Set(0, 3); Assert.False(a.IsUniform);
        a.Fill(7); Assert.False(a.IsUniform); Assert.Equal(16, a.StorageByteCount); Assert.True(a.Compact()); Assert.Equal(4, a.StorageByteCount);
    }
    [Fact]
    public void ExpandWithoutFillInitializesEncodedBytesSafely()
    {
        var a = new TypedAttributeArray<Vec3f, FixedPoint8Codec<Vec3f, PositionRange>>(3, uniformValue: new(.2f));
        a.Expand(false); Assert.All(a.GetStorageBytes().ToArray(), value => Assert.Equal(0, value)); Assert.Equal(new Vec3f(-.5f), a.Get(2));
    }
    [Fact]
    public void NativeEqualityPreservesTheStridedTailQuirkWhileFullComparisonFindsIt()
    {
        var a = new TypedAttributeArray<int>(3, 4); a.Expand(); var b = a.DeepCopy(); b.Set(11, 99);
        Assert.True(a.NativeEquals(b)); Assert.False(a.StorageEquals(b)); b.Set(1, 99); Assert.False(a.NativeEquals(b));
    }
    [Fact]
    public void VariableStrideEqualityAlsoChecksEveryValueWithFullComparison()
    {
        var a = new TypedAttributeArray<int>(2, 9, false); a.Expand(); var b = a.DeepCopy(); b.Set(8, 4);
        Assert.True(a.NativeEquals(b)); Assert.False(a.StorageEquals(b));
    }
    [Fact]
    public void NumericSignedZerosCompareEqualButNanDoesNotCompact()
    {
        var a = new TypedAttributeArray<float>(3, uniformValue: -0f); var b = new TypedAttributeArray<float>(3, uniformValue: 0f);
        Assert.True(a.NativeEquals(b)); Assert.NotEqual(Convert.ToHexString(a.GetStorageBytes()), Convert.ToHexString(b.GetStorageBytes()));
        a.Fill(float.NaN); a.Expand(); Assert.False(a.NativeEquals(a)); Assert.False(a.Compact()); Assert.False(a.IsUniform);
    }
    [Fact]
    public void VectorNanCompactionChecksEveryComponent()
    {
        var a = new TypedAttributeArray<Vec4d>(3, uniformValue: new(1, 2, double.NaN, 4)); a.Expand();
        Assert.False(a.Compact()); Assert.False(a.NativeEquals(a.DeepCopy()));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void BoundsRemainCheckedForUnsafeEntryPoints(int index)
    {
        var a = new TypedAttributeArray<int>(3); Assert.Throws<ArgumentOutOfRangeException>(() => a.GetUnsafe(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.SetUnsafe(index, 1)); Assert.True(a.IsUniform);
    }
    [Fact]
    public void UnsafeUniformWritesRemainBoundedAndUpdateTheSharedValue()
    {
        var a = new TypedAttributeArray<int>(3); a.SetUnsafe(2, 9); Assert.True(a.IsUniform); Assert.Equal(9, a.Get(0));
    }
    [Fact]
    public void HandlesExposeNativeFlattenedIndexingAndVariableStrideLayout()
    {
        var fixedArray = new TypedAttributeArray<int>(2, 3); using var fixedHandle = new AttributeWriteHandle<int>(fixedArray);
        fixedHandle.Set(1, 1, 17); Assert.Equal(17, fixedHandle.Get(0, 4)); Assert.Equal(17, fixedArray.Get(4));
        var variable = new TypedAttributeArray<int>(2, 7, false); using var handle = new AttributeWriteHandle<int>(variable);
        Assert.Equal(7, handle.Size); Assert.Equal(1, handle.Stride); Assert.False(handle.HasConstantStride); handle.Set(6, 41); Assert.Equal(41, variable.Get(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => handle.Get(int.MaxValue, int.MaxValue));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownAndUnknownCodecHandlesApplyIdenticalQuantization(bool known)
    {
        var a = new TypedAttributeArray<float, FixedPoint16Codec<float, UnitRange>>(3);
        if (known) { using var handle = new AttributeWriteHandle<float, FixedPoint16Codec<float, UnitRange>>(a); handle.Set(2, .75f); }
        else { using var handle = new AttributeWriteHandle<float>(a); handle.Set(2, .75f); }
        Assert.Equal(49151f / 65535, a.Get(2));
        Assert.Throws<ArgumentException>(() => new AttributeHandle<int>(a));
        Assert.Throws<ArgumentException>(() => new AttributeHandle<float, NullCodec<float>>(a));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReadHandleCapturesStreamingAndCollapsePolicy(bool streaming, bool collapse)
    {
        var a = new TypedAttributeArray<int>(3, uniformValue: 17) { IsStreaming = streaming }; a.Expand();
        var handle = new AttributeHandle<int>(a, collapse); a.IsStreaming = !streaming; handle.Dispose(); handle.Dispose();
        Assert.Equal(streaming && collapse, a.IsUniform); Assert.Equal(streaming && collapse ? 0 : 17, a.Get(0));
        Assert.Throws<ObjectDisposedException>(() => handle.Get(0)); Assert.Throws<ObjectDisposedException>(() => _ = handle.Array);
    }
    [Fact]
    public void WriteHandleDoesNotDiscardStreamingData()
    {
        var a = new TypedAttributeArray<int>(2) { IsStreaming = true };
        using (var handle = new AttributeWriteHandle<int>(a)) { handle.Set(1, 7); }
        Assert.Equal(7, a.Get(1)); Assert.False(a.IsUniform);
    }
    [Fact]
    public void FlagsAndDeepCopiesAreIndependent()
    {
        var a = new TypedAttributeArray<int>(2) { IsHidden = true, IsTransient = true, IsStreaming = true }; a.Expand(); a.Set(1, 7);
        var b = a.DeepCopy(); Assert.True(a.NativeEquals(b)); b.IsHidden = false; b.Set(1, 3);
        Assert.True(a.IsHidden); Assert.Equal(7, a.Get(1)); Assert.False(a.NativeEquals(b)); Assert.Equal((AttributeFlags)27, a.Flags);
    }
    [Fact]
    public void StorageCopyRetainsRawCodecWordsInsteadOfRequantizing()
    {
        var a = new TypedAttributeArray<float, FixedPoint8Codec<float, UnitRange>>(4); a.Expand();
        for (int i = 0; i < 4; i++) a.Set(i, i / 3f);
        var b = new TypedAttributeArray<float, FixedPoint8Codec<float, PositionRange>>(4);
        b.CopyValues(a, [new(3, 0), new(1, 1)], compact: false);
        Assert.Equal(.5f, b.Get(0)); Assert.Equal(a.GetStorageBytes()[1], b.GetStorageBytes()[1]);
    }
    [Fact]
    public void UniformStridedCopiesRepeatOneEncodedValueWithoutNativeOverread()
    {
        var source = new TypedAttributeArray<int>(2, 3, uniformValue: 7);
        var target = new TypedAttributeArray<int>(3, 3, uniformValue: 2);
        target.CopyValues(source, [new(999, 1)], compact: false);
        Assert.Equal(new[] { 2, 2, 2, 7, 7, 7, 2, 2, 2 }, Enumerable.Range(0, target.DataSize).Select(target.Get));
    }
    [Fact]
    public void SelfCopiesObservePriorSequentialWrites()
    {
        var a = new TypedAttributeArray<int>(4); for (int i = 0; i < 4; i++) a.Set(i, i + 1);
        a.CopyValues(a, [new(0, 1), new(1, 2), new(2, 3)]);
        Assert.True(a.IsUniform); Assert.Equal(1, a.Get(3));
    }
    [Fact]
    public void CopyRejectsIncompatibleStorageAndOutOfBufferStrideIndices()
    {
        var target = new TypedAttributeArray<float>(3, 2);
        Assert.Throws<ArgumentException>(() => target.CopyValues(new TypedAttributeArray<double>(3, 2), []));
        Assert.Throws<ArgumentException>(() => target.CopyValues(new TypedAttributeArray<int>(3, 2), []));
        Assert.True(target.IsUniform);
        var source = new TypedAttributeArray<float>(3, 2); source.Expand();
        Assert.Throws<ArgumentOutOfRangeException>(() => target.CopyValues(source, [new(4, 1)], compact: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => target.CopyValues(source, [new(0, 4)], compact: false));
    }
    [Fact]
    public void FactoriesValidateTypesAndTreatWrongMetadataAsDefault()
    {
        var registry = new AttributeArrayRegistry(); var type = new AttributeType("float", "trnc");
        Assert.Throws<KeyNotFoundException>(() => registry.Create(type, 1));
        registry.Register<float, TruncateCodec<float>>(); Assert.True(registry.IsRegistered(type));
        var a = Assert.IsType<TypedAttributeArray<float, TruncateCodec<float>>>(registry.Create(type, 3, metadata: new MetadataValue(.125f)));
        Assert.Equal(.125f, a.Get(2));
        var b = Assert.IsType<TypedAttributeArray<float, TruncateCodec<float>>>(registry.Create(type, 3, metadata: new MetadataValue(17)));
        Assert.Equal(0, b.Get(2));
        Assert.Throws<ArgumentException>(() => registry.Register(type, (_, _, _, _) => new TypedAttributeArray<int>()));
        Assert.True(registry.Unregister(type)); Assert.False(registry.Unregister(type)); registry.Register<float, TruncateCodec<float>>(); registry.Clear(); Assert.False(registry.IsRegistered(type));
    }
    [Fact]
    public void RegistryFactoriesCanReenterAndConcurrentReadersHaveIndependentArrays()
    {
        var registry = new AttributeArrayRegistry(); var type = new AttributeType("int32", "null");
        registry.Register(type, (length, stride, constant, metadata) => { _ = registry.IsRegistered(type); return new TypedAttributeArray<int>(length, stride, constant); });
        Parallel.For(0, 100, i =>
        {
            var array = Assert.IsAssignableFrom<AttributeArray<int>>(registry.Create(type, 3)); array.Set(1, i); Assert.Equal(i, array.Get(1));
        });
    }
    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 0)]
    [InlineData(.5, 127)]
    [InlineData(1, 255)]
    [InlineData(2, 255)]
    public void UnitFixedPointClampsAndTruncates(double value, int expected)
    {
        Span<byte> bytes = stackalloc byte[1]; FixedPoint8Codec<double, UnitRange>.Encode(value, bytes); Assert.Equal(expected, bytes[0]);
    }
    [Fact]
    public void UndefinedNanFixedPointConversionsAndUnsupportedCodecsFailExplicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypedAttributeArray<float, FixedPoint8Codec<float, UnitRange>>(1, uniformValue: float.NaN));
        Assert.Throws<NotSupportedException>(() => new TypedAttributeArray<double, TruncateCodec<double>>());
        Assert.Throws<NotSupportedException>(() => new TypedAttributeArray<int, FixedPoint8Codec<int, UnitRange>>());
        Assert.Throws<NotSupportedException>(() => new TypedAttributeArray<float, UnitVecCodec<float>>());
    }
    [Fact]
    public void UnitVectorZeroDecodesToPositiveZAndDoubleDecodeWidensFloat()
    {
        var a = new TypedAttributeArray<Vec3d, UnitVecCodec<Vec3d>>(1); Assert.Equal(Vec3d.UnitZ, a.Get(0));
        a.Set(0, new(1, 2, 3)); var value = a.Get(0); Assert.Equal((double)(float)value.X, value.X); Assert.Equal((double)(float)value.Y, value.Y);
    }
    [Fact]
    public void WarmPointAttributeAccessAllocatesNoManagedBytes()
    {
        var array = new TypedAttributeArray<Vec3f, FixedPoint16Codec<Vec3f, PositionRange>>(512);
        using var handle = new AttributeWriteHandle<Vec3f, FixedPoint16Codec<Vec3f, PositionRange>>(array); Vec3f value = new(.125f, -.25f, .375f);
        for (int i = 0; i < 20000; i++) { handle.Set(i & 511, value); _ = handle.Get(i & 511); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { handle.Set(i & 511, value); _ = handle.Get(i & 511); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
