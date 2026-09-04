// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/Diagnostics.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Checks squared upwind-gradient norm against the squares of the supplied bounds.</summary>
public sealed class CheckNormGrad<T> : DiagnosticCheck<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly ValueAccessor<T> _accessor;
    private readonly T _inverseDxSquared;
    public CheckNormGrad(Grid<T> grid, T minimum, T maximum, BiasedGradientScheme scheme = BiasedGradientScheme.First, ValueIterationMode mode = ValueIterationMode.Active) : base(mode)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!DiagnosticNumeric<T>.FloatingScalar) throw new NotSupportedException("CheckNormGrad requires float or double scalar values.");
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("CheckNormGrad: The transform must have uniform scale.", nameof(grid));
        if (minimum > maximum) throw new ArgumentException("CheckNormGrad: Invalid range (min > max).");
        if ((uint)scheme > (uint)BiasedGradientScheme.HJWeno5) throw new ArgumentOutOfRangeException(nameof(scheme));
        _accessor = grid.GetAccessor(); double dx = grid.Transform.VoxelSize.X; _inverseDxSquared = T.CreateTruncating(1.0 / (dx * dx)); MinimumSquared = minimum * minimum; MaximumSquared = maximum * maximum; Scheme = scheme;
    }
    private CheckNormGrad(CheckNormGrad<T> source) : base(source.IterationMode) { _accessor = source._accessor.Tree.GetAccessor(); _inverseDxSquared = source._inverseDxSquared; MinimumSquared = source.MinimumSquared; MaximumSquared = source.MaximumSquared; Scheme = source.Scheme; }
    public T MinimumSquared { get; }
    public T MaximumSquared { get; }
    public BiasedGradientScheme Scheme { get; }
    public override string Description => $"outside the range of NormGrad [{DiagnosticNumeric<T>.Format(T.Sqrt(MinimumSquared))},{DiagnosticNumeric<T>.Format(T.Sqrt(MaximumSquared))}]";
    public override bool Fails(T value) => value < MinimumSquared || value > MaximumSquared;
    public override bool FailsTile(CoordBBox bounds, T value) => Fails(T.Zero);
    public override bool FailsVoxel(Coord coordinate, T value) => Fails(_inverseDxSquared * DifferentialOperators.IndexNormSquaredGradient(_accessor, coordinate, Scheme));
    public override DiagnosticCheck<T> Copy() => new CheckNormGrad<T>(this);
}

public enum EikonalStencilKind { Weno, Gradient }
/// <summary>Checks squared gradient norm only at stencil zero crossings.</summary>
/// <remarks>Native CheckEikonal compares squared norm directly with its supplied bounds; unlike
/// CheckNormGrad, the bounds are not squared. Its diagnostic text retains the native NormGrad name.</remarks>
public sealed class CheckEikonal<T> : DiagnosticCheck<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly Grid<T> _grid;
    private readonly WenoStencil<T>? _weno;
    private readonly GradStencil<T>? _gradient;
    public CheckEikonal(Grid<T> grid, T minimum, T maximum, EikonalStencilKind stencil = EikonalStencilKind.Weno, ValueIterationMode mode = ValueIterationMode.Active) : base(mode)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!DiagnosticNumeric<T>.FloatingScalar) throw new NotSupportedException("CheckEikonal requires float or double scalar values.");
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("CheckEikonal: The transform must have uniform scale.", nameof(grid));
        if (minimum > maximum) throw new ArgumentException("CheckEikonal: Invalid range (min > max).");
        if ((uint)stencil > (uint)EikonalStencilKind.Gradient) throw new ArgumentOutOfRangeException(nameof(stencil));
        _grid = grid; Minimum = minimum; Maximum = maximum; StencilKind = stencil;
        if (stencil == EikonalStencilKind.Weno) _weno = new(grid); else _gradient = new(grid);
    }
    public T Minimum { get; }
    public T Maximum { get; }
    public EikonalStencilKind StencilKind { get; }
    public override string Description => $"outside the range of NormGrad [{DiagnosticNumeric<T>.Format(Minimum)},{DiagnosticNumeric<T>.Format(Maximum)}]";
    public override bool Fails(T value) => value < Minimum || value > Maximum;
    public override bool FailsTile(CoordBBox bounds, T value) => Fails(T.Zero);
    public override bool FailsVoxel(Coord coordinate, T value)
    {
        if (_weno is not null) { _weno.MoveTo(coordinate, value); return _weno.ZeroCrossing() && Fails(_weno.NormSquaredGradient()); }
        _gradient!.MoveTo(coordinate, value); return _gradient.ZeroCrossing() && Fails(_gradient.NormSquaredGradient());
    }
    public override DiagnosticCheck<T> Copy() => new CheckEikonal<T>(_grid, Minimum, Maximum, StencilKind, IterationMode);
}

/// <summary>Checks scalar divergence of a Vec3f or Vec3d field against scalar bounds.</summary>
/// <remarks>This corrects an ill-formed native CheckDivergence template whose bounds and inverse voxel
/// size use vector ValueType where ElementType is required. Derivatives use native ISDivergence.
/// Constant background and tiles have zero divergence. Scale and accessor tree are captured at construction.</remarks>
public sealed class CheckDivergence<TVector> : DiagnosticCheck<TVector> where TVector : unmanaged
{
    private readonly ValueAccessor<TVector> _accessor;
    private readonly double _inverseDx;
    public CheckDivergence(Grid<TVector> grid, double minimum, double maximum, DScheme scheme = DScheme.CenteredSecond, ValueIterationMode mode = ValueIterationMode.Active) : base(mode)
    {
        ArgumentNullException.ThrowIfNull(grid); VelocityValue<TVector>.Validate();
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("CheckDivergence: The transform must have uniform scale.", nameof(grid));
        Minimum = typeof(TVector) == typeof(Vec3f) ? (float)minimum : minimum; Maximum = typeof(TVector) == typeof(Vec3f) ? (float)maximum : maximum;
        if (Minimum > Maximum) throw new ArgumentException("CheckDivergence: Invalid range (min > max).");
        _ = FiniteDifference.OffsetRange(scheme); Scheme = scheme; _accessor = grid.GetAccessor(); double inverse = 1 / grid.Transform.VoxelSize.X; _inverseDx = typeof(TVector) == typeof(Vec3f) ? (float)inverse : inverse;
    }
    private CheckDivergence(CheckDivergence<TVector> source) : base(source.IterationMode) { _accessor = source._accessor.Tree.GetAccessor(); _inverseDx = source._inverseDx; Minimum = source.Minimum; Maximum = source.Maximum; Scheme = source.Scheme; }
    public double Minimum { get; }
    public double Maximum { get; }
    public DScheme Scheme { get; }
    public override string Description => $"outside the range of divergence [{DiagnosticNumeric<double>.FormatDouble(Minimum)},{DiagnosticNumeric<double>.FormatDouble(Maximum)}]";
    public bool FailsDivergence(double value) { if (typeof(TVector) == typeof(Vec3f)) value = (float)value; return value < Minimum || value > Maximum; }
    public override bool Fails(TVector value) => FailsDivergence(0);
    public override bool FailsTile(CoordBBox bounds, TVector value) => FailsDivergence(0);
    public override bool FailsVoxel(Coord coordinate, TVector value)
    {
        double divergence = typeof(TVector) == typeof(Vec3f) ? (float)_inverseDx * DifferentialOperators.IndexDivergence((ValueAccessor<Vec3f>)(object)_accessor, coordinate, Scheme) : _inverseDx * DifferentialOperators.IndexDivergence((ValueAccessor<Vec3d>)(object)_accessor, coordinate, Scheme);
        return FailsDivergence(divergence);
    }
    public override DiagnosticCheck<TVector> Copy() => new CheckDivergence<TVector>(this);
}
