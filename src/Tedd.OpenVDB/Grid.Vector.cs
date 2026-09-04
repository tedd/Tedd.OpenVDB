// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB;

public enum VectorType { Invariant, Covariant, CovariantNormalize, ContravariantRelative, ContravariantAbsolute }
public sealed partial class Grid<T> where T : unmanaged
{
    public const string VectorTypeMetadataKey = "vector_type";
    public const string LocalSpaceMetadataKey = "is_local_space";
    public VectorType VectorType
    {
        get => TryGetMetadata<string>(VectorTypeMetadataKey, out var text) ? ParseVectorType(text) : VectorType.Invariant;
        set => InsertMeta(VectorTypeMetadataKey, VectorTypeToString(value));
    }
    public void ClearVectorType() => RemoveMetadata(VectorTypeMetadataKey);
    public bool IsInWorldSpace
    {
        get => !(_metadata.TryGetValue(LocalSpaceMetadataKey, out var value) && value.AsBool());
        set { RemoveMetadata(LocalSpaceMetadataKey); InsertMeta(LocalSpaceMetadataKey, !value); }
    }
    public static VectorType ParseVectorType(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Trim().ToLowerInvariant() switch
        {
            "covariant" => VectorType.Covariant,
            "covariant normalize" => VectorType.CovariantNormalize,
            "contravariant relative" => VectorType.ContravariantRelative,
            "contravariant absolute" => VectorType.ContravariantAbsolute,
            _ => VectorType.Invariant
        };
    }
    public static string VectorTypeToString(VectorType type) => type switch
    {
        VectorType.Invariant => "invariant",
        VectorType.Covariant => "covariant",
        VectorType.CovariantNormalize => "covariant normalize",
        VectorType.ContravariantRelative => "contravariant relative",
        VectorType.ContravariantAbsolute => "contravariant absolute",
        _ => string.Empty
    };
    public static string VectorTypeExamples(VectorType type) => type switch
    {
        VectorType.Invariant => "Tuple/Color/UVW",
        VectorType.Covariant => "Gradient/Normal",
        VectorType.CovariantNormalize => "Unit Normal",
        VectorType.ContravariantRelative => "Displacement/Velocity/Acceleration",
        VectorType.ContravariantAbsolute => "Position",
        _ => string.Empty
    };
    public static string VectorTypeDescription(VectorType type) => type switch
    {
        VectorType.Invariant => "Does not transform",
        VectorType.Covariant => "Apply the inverse-transpose transform matrix but ignore translation",
        VectorType.CovariantNormalize => "Apply the inverse-transpose transform matrix but ignore translation and renormalize vectors",
        VectorType.ContravariantRelative => "Apply the forward transform matrix but ignore translation",
        VectorType.ContravariantAbsolute => "Apply the forward transform matrix, including translation",
        _ => string.Empty
    };
}