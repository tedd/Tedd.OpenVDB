// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public class ScaleMap : MapBase
{
    public ScaleMap() : this(Vec3d.One) { }
    public ScaleMap(Vec3d scale)
    {
        ValidateScale(scale); Scale = scale;
        VoxelSize = new(System.Math.Abs(scale.X), System.Math.Abs(scale.Y), System.Math.Abs(scale.Z));
        InverseScale = new(1.0 / scale.X, 1.0 / scale.Y, 1.0 / scale.Z); InverseScaleSquared = InverseScale * InverseScale; InverseTwiceScale = InverseScale / 2;
        if (!InverseScale.IsFinite || !InverseScaleSquared.IsFinite || !InverseTwiceScale.IsFinite) throw new ArithmeticException("Scale acceleration values are not finite.");
    }
    internal ScaleMap(Vec3d scale, Vec3d voxelSize, Vec3d inverseScale, Vec3d inverseScaleSquared, Vec3d inverseTwiceScale) : this(scale)
    {
        if (!voxelSize.IsFinite || !inverseScale.IsFinite || !inverseScaleSquared.IsFinite || !inverseTwiceScale.IsFinite) throw new InvalidDataException("Nonfinite cached scale data.");
        VoxelSize = voxelSize; InverseScale = inverseScale; InverseScaleSquared = inverseScaleSquared; InverseTwiceScale = inverseTwiceScale;
    }
    internal static void ValidateScale(Vec3d scale)
    {
        if (!scale.IsFinite) throw new ArgumentException("Scale must be finite.", nameof(scale));
        double determinant = scale.X * scale.Y * scale.Z;
        if (!double.IsFinite(determinant) || System.Math.Abs(determinant) < 3e-15) throw new ArithmeticException("Nonzero scale values are required.");
    }
    public override string MapType => nameof(ScaleMap);
    public Vec3d Scale { get; }
    public Vec3d InverseScale { get; }
    public Vec3d InverseScaleSquared { get; }
    public Vec3d InverseTwiceScale { get; }
    public override Mat4d Matrix => Mat4d.CreateScale(Scale);
    public sealed override double Determinant => Scale.X * Scale.Y * Scale.Z;
    public sealed override Vec3d VoxelSize { get; }
    public sealed override bool HasUniformScale => MathComparison.IsApproxEqual(System.Math.Abs(Scale.X), System.Math.Abs(Scale.Y), 5e-7) && MathComparison.IsApproxEqual(System.Math.Abs(Scale.X), System.Math.Abs(Scale.Z), 5e-7);
    public sealed override Vec3d ApplyMap(Vec3d value) => value * Scale;
    public sealed override Vec3d ApplyInverseMap(Vec3d value) => value * InverseScale;
    public sealed override Vec3d ApplyJacobian(Vec3d value) => ApplyMap(value);
    public sealed override Vec3d ApplyInverseJacobian(Vec3d value) => ApplyInverseMap(value);
    public sealed override Vec3d ApplyJacobianTranspose(Vec3d value) => ApplyMap(value);
    public sealed override Vec3d ApplyInverseJacobianTranspose(Vec3d value) => ApplyInverseMap(value);
    public sealed override Mat3d ApplyInverseJacobianCurvature(Mat3d value) => value.DiagonalTimes(InverseScale).TimesDiagonal(InverseScale);
    public override ScaleMap Copy() => new(Scale, VoxelSize, InverseScale, InverseScaleSquared, InverseTwiceScale);
    public override ScaleMap InverseMap() => new(InverseScale);
    public override bool IsEqual(MapBase? other) => other is ScaleMap map && MapType == map.MapType && Scale.ApproxEquals(map.Scale);
    public override MapBase PreTranslate(Vec3d value) => new ScaleTranslateMap(Scale, value * Scale);
    public override MapBase PostTranslate(Vec3d value) => new ScaleTranslateMap(Scale, value);
    public override MapBase PreScale(Vec3d value) { Vec3d scale = value * Scale; return IsUniform(scale) ? new UniformScaleMap(scale.X) : new ScaleMap(scale); }
    public override MapBase PostScale(Vec3d value) => PreScale(value);
}

public sealed class UniformScaleMap : ScaleMap
{
    public UniformScaleMap(double scale = 1) : base(new Vec3d(scale)) { }
    internal UniformScaleMap(Vec3d scale, Vec3d voxelSize, Vec3d inverseScale, Vec3d inverseScaleSquared, Vec3d inverseTwiceScale) : base(scale, voxelSize, inverseScale, inverseScaleSquared, inverseTwiceScale)
    { if (scale.X != scale.Y || scale.X != scale.Z) throw new InvalidDataException("A uniform scale map has nonuniform primary values."); }
    public override string MapType => nameof(UniformScaleMap);
    public override UniformScaleMap Copy() => new(Scale, VoxelSize, InverseScale, InverseScaleSquared, InverseTwiceScale);
    public override UniformScaleMap InverseMap() => new(InverseScale.X);
    public override MapBase PreTranslate(Vec3d value) => new UniformScaleTranslateMap(Scale.X, Scale.X * value);
    public override MapBase PostTranslate(Vec3d value) => new UniformScaleTranslateMap(Scale.X, value);
}
