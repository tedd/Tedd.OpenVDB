// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.IO;

public static partial class VdbFile
{
    private static void ValidateTransform(MapBase map)
    {
        Type type = map.GetType();
        if (type != typeof(AffineMap) && type != typeof(UnitaryMap) && type != typeof(ScaleMap) && type != typeof(UniformScaleMap) &&
            type != typeof(TranslationMap) && type != typeof(ScaleTranslateMap) && type != typeof(UniformScaleTranslateMap) && type != typeof(NonlinearFrustumMap))
            throw new NotSupportedException($"VDB map type '{map.MapType}' is unsupported.");
    }
    private static void WriteTransform(BinaryWriter output, Transform transform) => WriteMap(output, transform.BaseMap);
    private static void WriteMap(BinaryWriter output, MapBase map)
    {
        WriteString(output, map.MapType);
        switch (map)
        {
            case NonlinearFrustumMap frustum:
                WriteVector(output, frustum.BoundingBox.Min); WriteVector(output, frustum.BoundingBox.Max);
                output.Write(frustum.Taper); output.Write(frustum.Depth); WriteMap(output, new AffineMap(frustum.SecondMap)); break;
            case AffineMap or UnitaryMap: WriteMatrix(output, map.Matrix); break;
            case TranslationMap translation: WriteVector(output, translation.Translation); break;
            case ScaleMap scale:
                WriteScaleCache(output, scale.Scale, scale.VoxelSize, scale.InverseScale, scale.InverseScaleSquared, scale.InverseTwiceScale); break;
            case ScaleTranslateMap translated:
                WriteVector(output, translated.Translation);
                WriteScaleCache(output, translated.Scale, translated.VoxelSize, translated.InverseScale, translated.InverseScaleSquared, translated.InverseTwiceScale); break;
            default: throw new NotSupportedException($"VDB map type '{map.MapType}' is unsupported.");
        }
    }
    private static void WriteScaleCache(BinaryWriter output, Vec3d scale, Vec3d voxelSize, Vec3d inverse, Vec3d inverseSquared, Vec3d inverseTwice)
    {
        WriteVector(output, scale); WriteVector(output, voxelSize); WriteVector(output, inverse); WriteVector(output, inverseSquared); WriteVector(output, inverseTwice);
    }
    private static Transform ReadTransform(Input input, bool allowFrustum = true) => Transform.FromMap(ReadMap(input, allowFrustum));
    private static MapBase ReadMap(Input input, bool allowFrustum)
    {
        string type = input.String(); input.Reserve(2048); // Map caches, the retained transform, and immutable copy during materialization.
        try
        {
            if (type == nameof(NonlinearFrustumMap))
            {
                if (!allowFrustum) throw new InvalidDataException("A frustum secondary map must be linear.");
                var bounds = new BBoxd(input.Vector(), input.Vector());
                double taper = input.Scalar<double>(), depth = input.Scalar<double>();
                MapBase secondary = ReadMap(input, false);
                return new NonlinearFrustumMap(bounds, taper, depth, secondary.Matrix);
            }
            if (type == nameof(AffineMap)) return new AffineMap(input.Matrix());
            if (type == nameof(UnitaryMap)) return UnitaryMap.FromSerializedMatrix(input.Matrix());
            if (type == nameof(TranslationMap)) return new TranslationMap(input.Vector());
            if (type is nameof(ScaleMap) or nameof(UniformScaleMap) or nameof(ScaleTranslateMap) or nameof(UniformScaleTranslateMap))
            {
                bool translated = type is nameof(ScaleTranslateMap) or nameof(UniformScaleTranslateMap);
                Vec3d translation = translated ? input.Vector() : Vec3d.Zero;
                Vec3d scale = input.Vector(), voxelSize = input.Vector(), inverse = input.Vector(), inverseSquared = input.Vector(), inverseTwice = input.Vector();
                if (type == nameof(UniformScaleMap)) return new UniformScaleMap(scale, voxelSize, inverse, inverseSquared, inverseTwice);
                var scaling = new ScaleMap(scale, voxelSize, inverse, inverseSquared, inverseTwice);
                return type switch
                {
                    nameof(ScaleMap) => scaling,
                    nameof(UniformScaleTranslateMap) => new UniformScaleTranslateMap(scaling, translation),
                    _ => new ScaleTranslateMap(scaling, translation)
                };
            }
            throw new NotSupportedException($"VDB transform map '{type}' is unsupported.");
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException)
        { throw new InvalidDataException("Invalid VDB transform map.", exception); }
    }
    internal static Transform ReadTransformMap(Stream stream, bool leaveOpen, VdbReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            if (!stream.CanRead) throw new ArgumentException("The stream is not readable.", nameof(stream));
            options ??= new(); options.Validate(); return ReadTransform(new Input(stream, options));
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }
    internal static void WriteTransformMap(Stream stream, Transform transform, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            if (!stream.CanWrite) throw new ArgumentException("The stream is not writable.", nameof(stream));
            ValidateTransform(transform.BaseMap); using var output = new BinaryWriter(stream, Utf8, leaveOpen: true); WriteTransform(output, transform);
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }
}
