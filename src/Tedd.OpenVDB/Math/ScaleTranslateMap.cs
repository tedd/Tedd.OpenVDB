// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public class ScaleTranslateMap : MapBase
{
    private readonly ScaleMap _scaling;
    public ScaleTranslateMap() : this(Vec3d.One, Vec3d.Zero) { }
    public ScaleTranslateMap(Vec3d scale, Vec3d translation) : this(new ScaleMap(scale), translation) { }
    public ScaleTranslateMap(ScaleMap scale, TranslationMap translation) : this(scale.Scale, translation.Translation) { }
    internal ScaleTranslateMap(ScaleMap scale, Vec3d translation)
    {
        if (!translation.IsFinite) throw new ArgumentException("Translation must be finite.", nameof(translation));
        _scaling = scale; Translation = translation;
    }
    public override string MapType => nameof(ScaleTranslateMap);
    public Vec3d Scale => _scaling.Scale;
    public Vec3d Translation { get; }
    public Vec3d InverseScale => _scaling.InverseScale;
    public Vec3d InverseScaleSquared => _scaling.InverseScaleSquared;
    public Vec3d InverseTwiceScale => _scaling.InverseTwiceScale;
    public override Mat4d Matrix => Mat4d.CreateScale(Scale) * Mat4d.CreateTranslation(Translation);
    public sealed override double Determinant => Scale.X * Scale.Y * Scale.Z;
    public sealed override Vec3d VoxelSize => _scaling.VoxelSize;
    public sealed override bool HasUniformScale => _scaling.HasUniformScale;
    public sealed override Vec3d ApplyMap(Vec3d value) => new(value.X * Scale.X + Translation.X, value.Y * Scale.Y + Translation.Y, value.Z * Scale.Z + Translation.Z);
    public sealed override Vec3d ApplyInverseMap(Vec3d value) => new((value.X - Translation.X) * InverseScale.X, (value.Y - Translation.Y) * InverseScale.Y, (value.Z - Translation.Z) * InverseScale.Z);
    public sealed override Vec3d ApplyJacobian(Vec3d value) => value * Scale;
    public sealed override Vec3d ApplyInverseJacobian(Vec3d value) => value * InverseScale;
    public sealed override Vec3d ApplyJacobianTranspose(Vec3d value) => value * Scale;
    public sealed override Vec3d ApplyInverseJacobianTranspose(Vec3d value) => value * InverseScale;
    public sealed override Mat3d ApplyInverseJacobianCurvature(Mat3d value) => value.DiagonalTimes(InverseScale).TimesDiagonal(InverseScale);
    public override ScaleTranslateMap Copy() => new(_scaling.Copy(), Translation);
    public override ScaleTranslateMap InverseMap() => new(InverseScale, -InverseScale * Translation);
    public override bool IsEqual(MapBase? other) => other is ScaleTranslateMap map && MapType == map.MapType && Scale.ApproxEquals(map.Scale) && Translation.ApproxEquals(map.Translation);
    public override MapBase PreTranslate(Vec3d value) => new ScaleTranslateMap(Scale, Translation + value * Scale);
    public override MapBase PostTranslate(Vec3d value) => new ScaleTranslateMap(Scale, Translation + value);
    public override MapBase PreScale(Vec3d value) { Vec3d scale = value * Scale; return IsUniform(scale) ? new UniformScaleTranslateMap(scale.X, Translation) : new ScaleTranslateMap(scale, Translation); }
    public override MapBase PostScale(Vec3d value) { Vec3d scale = value * Scale, translation = Translation * value; return IsUniform(scale) ? new UniformScaleTranslateMap(scale.X, translation) : new ScaleTranslateMap(scale, translation); }
}

public sealed class UniformScaleTranslateMap : ScaleTranslateMap
{
    public UniformScaleTranslateMap() : this(1, Vec3d.Zero) { }
    public UniformScaleTranslateMap(double scale, Vec3d translation) : base(new Vec3d(scale), translation) { }
    public UniformScaleTranslateMap(UniformScaleMap scale, TranslationMap translation) : this(scale.Scale.X, translation.Translation) { }
    internal UniformScaleTranslateMap(ScaleMap scale, Vec3d translation) : base(scale, translation)
    { if (scale.Scale.X != scale.Scale.Y || scale.Scale.X != scale.Scale.Z) throw new InvalidDataException("A uniform scale map has nonuniform primary values."); }
    public override string MapType => nameof(UniformScaleTranslateMap);
    public override UniformScaleTranslateMap Copy() => new(new ScaleMap(Scale, VoxelSize, InverseScale, InverseScaleSquared, InverseTwiceScale), Translation);
    public override UniformScaleTranslateMap InverseMap() => new(InverseScale.X, -InverseScale.X * Translation);
    public override MapBase PreTranslate(Vec3d value) => new UniformScaleTranslateMap(Scale.X, Translation + Scale.X * value);
    public override MapBase PostTranslate(Vec3d value) => new UniformScaleTranslateMap(Scale.X, Translation + value);
}
