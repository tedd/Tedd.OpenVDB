// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/GridTransformer.h resampleToMatch dispatch.
using System.Numerics;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

public static class Resampling
{
    /// <summary>Resamples to the output transform while retaining its metadata and grid classification.</summary>
    /// <remarks>Identical-transform level sets are copied. Differing-transform float/double level sets are rebuilt through a polygon surface, independently of sampling order. MaximumSamples bounds each rebuilding stage separately: meshing candidates and mesh-distance evaluations.</remarks>
    public static void ResampleToMatch<T>(Grid<T> input, Grid<T> output, SamplingOrder order = SamplingOrder.Box, bool staggered = false, CoordBBox? outputClip = null, ulong maximumSamples = ulong.MaxValue, CancellationToken cancellationToken = default) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output); cancellationToken.ThrowIfCancellationRequested();
        SamplingKernel<T, TreeSampleSource<T>>.Validate(order, staggered);
        if (input.GridClass == GridClass.LevelSet)
        {
            if (input.Transform.IsEquivalentTo(output.Transform)) { Copy(input, output, outputClip); return; }
            if (typeof(T) == typeof(float)) { Rebuild((Grid<float>)(object)input, (Grid<float>)(object)output, outputClip, maximumSamples, cancellationToken); return; }
            if (typeof(T) == typeof(double)) { Rebuild((Grid<double>)(object)input, (Grid<double>)(object)output, outputClip, maximumSamples, cancellationToken); return; }
        }
        DoResampleToMatch(input, output, order, staggered, outputClip, maximumSamples, cancellationToken);
    }

    /// <summary>Native generic resampling path, without mesh-based level-set rebuilding.</summary>
    /// <remarks>This path does not preserve signed-distance scaling under scale/shear. Use ResampleToMatch for the level-set rebuild contract.</remarks>
    public static void DoResampleToMatch<T>(Grid<T> input, Grid<T> output, SamplingOrder order = SamplingOrder.Box, bool staggered = false, CoordBBox? outputClip = null, ulong maximumSamples = ulong.MaxValue, CancellationToken cancellationToken = default) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output); cancellationToken.ThrowIfCancellationRequested();
        SamplingKernel<T, TreeSampleSource<T>>.Validate(order, staggered);
        var mapping = new ABTransform(input.Transform, output.Transform);
        if (!staggered && order != SamplingOrder.Quadratic && mapping.IsIdentity) { Copy(input, output, outputClip); return; }
        if (mapping.IsAffine)
        {
            var transformer = new GridTransformer(input.Transform.Matrix * output.Transform.Matrix.Inverse()) { OutputClip = outputClip, MaximumSamples = maximumSamples };
            transformer.TransformGrid(input, output, order, staggered, cancellationToken);
        }
        else new GridResampler { OutputClip = outputClip, MaximumSamples = maximumSamples }.TransformGrid(mapping, input, output, order, staggered, cancellationToken);
    }
    private static void Rebuild<T>(Grid<T> input, Grid<T> output, CoordBBox? clip, ulong maximumSamples, CancellationToken token)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        Grid<T> bandSource = output.GridClass == GridClass.LevelSet ? output : input;
        T halfWidth = T.CreateTruncating(double.CreateTruncating(bandSource.Background) * (1.0 / bandSource.Transform.VoxelSize.X));
        var options = new MeshToVolumeOptions
        {
            MaximumLeaves = int.MaxValue,
            MaximumVoxelEvaluations = (long)System.Math.Min(maximumSamples, (ulong)long.MaxValue),
            CancellationToken = token
        };
        Grid<T> rebuilt = LevelSetRebuild.RebuildTyped(input, T.Zero, halfWidth, halfWidth, output.Transform, options, maximumSamples);
        if (clip is CoordBBox box) rebuilt.Tree.Clip(box);
        token.ThrowIfCancellationRequested();
        output.SetTree(rebuilt.Tree);
    }
    private static void Copy<T>(Grid<T> input, Grid<T> output, CoordBBox? clip) where T : unmanaged
    {
        var tree = input.Tree.DeepCopy(); if (clip is CoordBBox box) tree.Clip(box); output.SetTree(tree);
    }
}
