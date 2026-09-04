// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public sealed class TranslationMap : MapBase
{
    public TranslationMap() : this(Vec3d.Zero) { }
    public TranslationMap(Vec3d translation)
    {
        if (!translation.IsFinite) throw new ArgumentException("Translation must be finite.", nameof(translation));
        Translation = translation;
    }
    public Vec3d Translation { get; }
    public override string MapType => nameof(TranslationMap);
    public override Mat4d Matrix => Mat4d.CreateTranslation(Translation);
    public override bool HasUniformScale => true;
    public override double Determinant => 1;
    public override Vec3d VoxelSize => Vec3d.One;
    public override Vec3d ApplyMap(Vec3d value) => value + Translation;
    public override Vec3d ApplyInverseMap(Vec3d value) => value - Translation;
    public override Vec3d ApplyJacobian(Vec3d value) => value;
    public override Vec3d ApplyInverseJacobian(Vec3d value) => value;
    public override Vec3d ApplyJacobianTranspose(Vec3d value) => value;
    public override Vec3d ApplyInverseJacobianTranspose(Vec3d value) => value;
    public override Mat3d ApplyInverseJacobianCurvature(Mat3d value) => value;
    public override TranslationMap Copy() => new(Translation);
    public override TranslationMap InverseMap() => new(-Translation);
    public override bool IsEqual(MapBase? other) => other is TranslationMap map && Translation.ApproxEquals(map.Translation);
    public override MapBase PreTranslate(Vec3d value) => new TranslationMap(value + Translation);
    public override MapBase PostTranslate(Vec3d value) => new TranslationMap(value + Translation);
    public override MapBase PreScale(Vec3d value) => IsUniform(value) ? new UniformScaleTranslateMap(value.X, Translation) : new ScaleTranslateMap(value, Translation);
    public override MapBase PostScale(Vec3d value) => IsUniform(value) ? new UniformScaleTranslateMap(value.X, value.X * Translation) : new ScaleTranslateMap(value, Translation * value);
}
