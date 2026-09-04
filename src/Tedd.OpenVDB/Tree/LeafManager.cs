// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Tree;

/// <summary>A snapshot of leaves with reusable auxiliary value buffers for iterative algorithms.</summary>
/// <remarks>Topology changes require Rebuild. Buffer swaps preserve leaf identity and active masks.</remarks>
public sealed class LeafManager<T> where T : unmanaged
{
    private readonly VdbTree<T> _tree;
    private LeafNode<T>[] _leaves = [];
    private T[][][] _buffers = [];
    private long _version;
    public LeafManager(VdbTree<T> tree, int auxiliaryBufferCount = 0)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        Rebuild(auxiliaryBufferCount);
    }
    public VdbTree<T> Tree => _tree;
    public int LeafCount => _leaves.Length;
    public int AuxiliaryBufferCount { get; private set; }
    public bool IsValid => _version == _tree.TopologyVersion;
    public LeafNode<T> this[int index] { get { Validate(); return _leaves[index]; } }
    public void Rebuild(int auxiliaryBufferCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(auxiliaryBufferCount);
        var leaves = _tree.EnumerateLeaves().ToArray();
        var buffers = new T[leaves.Length][][];
        for (int leaf = 0; leaf < leaves.Length; leaf++)
        {
            buffers[leaf] = new T[auxiliaryBufferCount][];
            for (int buffer = 0; buffer < auxiliaryBufferCount; buffer++) buffers[leaf][buffer] = leaves[leaf].Values.ToArray();
        }
        _leaves = leaves; _buffers = buffers; AuxiliaryBufferCount = auxiliaryBufferCount; _version = _tree.TopologyVersion;
    }
    /// <summary>Returns auxiliary buffer 1..AuxiliaryBufferCount. Leaf buffer 0 is exposed through the leaf.</summary>
    public Span<T> GetBuffer(int leafIndex, int bufferIndex)
    {
        Validate(); ValidateBuffer(bufferIndex);
        return _buffers[leafIndex][bufferIndex - 1];
    }
    public void SyncAuxiliaryBuffer(int bufferIndex)
    {
        Validate(); ValidateBuffer(bufferIndex);
        for (int leaf = 0; leaf < _leaves.Length; leaf++) _leaves[leaf].Values.CopyTo(_buffers[leaf][bufferIndex - 1]);
    }
    public void SyncAllBuffers()
    {
        Validate();
        for (int buffer = 1; buffer <= AuxiliaryBufferCount; buffer++) SyncAuxiliaryBuffer(buffer);
    }
    public void SwapBuffer(int bufferIndex)
    {
        Validate(); ValidateBuffer(bufferIndex);
        for (int leaf = 0; leaf < _leaves.Length; leaf++) _buffers[leaf][bufferIndex - 1] = _leaves[leaf].SwapBuffer(_buffers[leaf][bufferIndex - 1]);
    }
    /// <summary>Runs an operation on distinct leaves. The callback must not change tree topology.</summary>
    public void ForEach(Action<LeafNode<T>, int> operation, bool parallel = false)
    {
        ArgumentNullException.ThrowIfNull(operation); Validate();
        if (parallel) Parallel.For(0, _leaves.Length, index => operation(_leaves[index], index));
        else for (int index = 0; index < _leaves.Length; index++) operation(_leaves[index], index);
        Validate();
    }
    private void Validate()
    {
        if (!IsValid) throw new InvalidOperationException("The tree topology changed. Rebuild the leaf manager.");
    }
    private void ValidateBuffer(int index)
    {
        if (index < 1 || index > AuxiliaryBufferCount) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
