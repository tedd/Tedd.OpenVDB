// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/Interpolation.h::AlphaMask.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Samples a float mask in a target grid's index space and derives smooth alpha/beta weights.</summary>
/// <remarks>Transforms are retained by reference; transform alignment is captured at construction. Instances cache a tree accessor and are not thread-safe.</remarks>
public sealed class AlphaMask<T> where T : unmanaged
{
    private readonly Transform _target, _source;
    private readonly ValueAccessor<float> _accessor;
    private readonly bool _aligned, _invert;
    private readonly float _minimum, _inverseRange;
    public AlphaMask(Grid<T> grid, Grid<float> mask, float minimum = 0, float maximum = 1, bool invert = false)
    {
        ArgumentNullException.ThrowIfNull(grid); ArgumentNullException.ThrowIfNull(mask);
        if (!(minimum < maximum)) throw new ArgumentException("Mask minimum must be less than its maximum.");
        _target = grid.Transform; _source = mask.Transform; _accessor = mask.GetAccessor();
        _aligned = ReferenceEquals(_target, _source) || _target.Equals(_source);
        _minimum = minimum; _inverseRange = 1 / (maximum - minimum); _invert = invert;
    }
    public bool Evaluate(Coord coordinate, out float alpha, out float beta)
    {
        float value = _aligned ? _accessor.GetValue(coordinate) : BoxSampler.Sample(_accessor, _source.WorldToIndex(_target.IndexToWorld(coordinate)));
        float x = (value - _minimum) * _inverseRange;
        alpha = x > 0 ? x < 1 ? (3 - 2 * x) * x * x : 1 : 0;
        beta = 1 - alpha;
        if (_invert) (alpha, beta) = (beta, alpha);
        return alpha > 0;
    }
}
