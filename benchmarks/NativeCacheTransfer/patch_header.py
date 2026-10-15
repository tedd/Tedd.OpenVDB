"""Generate experimental header overlays; never modify the upstream checkout."""
from pathlib import Path
import difflib


def patch(source: Path, destination: Path, evidence: Path) -> None:
    path = source / 'openvdb/openvdb/tree/ValueAccessor.h'
    original = path.read_text(encoding='utf-8')
    modified = original

    def replace(old: str, new: str) -> None:
        nonlocal modified
        if modified.count(old) != 1:
            raise ValueError('Upstream anchor missing or ambiguous: ' + old[:90])
        modified = modified.replace(old, new)

    replace('    mutable std::array<Coord, NumCacheLevels> mKeys;', '''#ifdef EXPERIMENT_LEAF_CACHE
    // Experimental 64-leaf direct-mapped cache. The normal tree callbacks clear
    // it with the existing cache, before detached leaves can be reused.
    mutable std::array<const LeafNodeT*, 64> mSecondaryLeaves{};
    static size_t secondarySlot(const Coord& xyz)
    {
        constexpr auto shift = LeafNodeT::LOG2DIM;
        return (((uint32_t(xyz[0]) >> shift) & 3) << 4)
             | (((uint32_t(xyz[1]) >> shift) & 3) << 2)
             | ((uint32_t(xyz[2]) >> shift) & 3);
    }
#endif
    mutable std::array<Coord, NumCacheLevels> mKeys;''')
    replace('''    void clear() override final
    {
        mKeys.fill''', '''    void clear() override final
    {
#ifdef EXPERIMENT_LEAF_CACHE
        mSecondaryLeaves.fill(nullptr);
#endif
        mKeys.fill''')
    replace('''    void eraseNode()
    {
        static constexpr''', '''    void eraseNode()
    {
#ifdef EXPERIMENT_LEAF_CACHE
        if constexpr (std::is_same<NodeT, LeafNodeT>::value) mSecondaryLeaves.fill(nullptr);
#endif
        static constexpr''')
    replace('''            mNodes.template get<Idx>() = const_cast<NodeT*>(node);
            if constexpr''', '''            mNodes.template get<Idx>() = const_cast<NodeT*>(node);
#ifdef EXPERIMENT_LEAF_CACHE
            if constexpr (std::is_same<NodeT, LeafNodeT>::value)
                mSecondaryLeaves[secondarySlot(xyz)] = node;
#endif
            if constexpr''')
    replace('''            return (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]
                && (xyz[1] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][1]
                && (xyz[2] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][2];''', '''            const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]
                && (xyz[1] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][1]
                && (xyz[2] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][2];
#if defined(EXPERIMENT_LEAF_CACHE) && !defined(EXPERIMENT_SPLIT_CACHE)
            if (!matches) {
                if constexpr (std::is_same<NodeT, LeafNodeT>::value) {
                    const auto leaf = mSecondaryLeaves[secondarySlot(xyz)];
                    // Full origin equality rejects collisions, including signed coordinates.
                    if (leaf && leaf->origin() == (xyz & ~Coord::ValueType(NodeT::DIM-1))) {
                        this->insert(xyz, leaf);
                        return true;
                    }
                }
            }
#endif
            return matches;''')
    replace('''                    const_cast<NodeType*>(node)->setValueOn(offset);''', '''#ifdef EXPERIMENT_DENSE_MASK
                    // This experiment targets ordinary leaves with at least 64 voxels.
                    // Smaller leaves retain their original mask API and behavior.
                    if constexpr (NodeType::LOG2DIM >= 2) {
                        if (node->getValueMask().template getWord<Index64>(offset >> 6) != ~Index64(0))
                            const_cast<NodeType*>(node)->setValueOn(offset);
                    } else {
                        const_cast<NodeType*>(node)->setValueOn(offset);
                    }
#else
                    const_cast<NodeType*>(node)->setValueOn(offset);
#endif''')
    # Follow-up experiment: the first overlay made the leaf-index lambda too
    # large for MSVC to inline. Keep the common leaf hit outside that lambda.
    replace('''    const ValueType& getValue(const Coord& xyz) const
    {''', '''#ifdef EXPERIMENT_SPLIT_CACHE
    OPENVDB_FORCE_INLINE const ValueType& getValue(const Coord& xyz) const
    {
        if constexpr (BypassLeafAPI && std::is_same<MutexT, void>::value) {
            if (this->template isHashed<LeafNodeT>(xyz))
                return LeafCacheT::buffer()[LeafNodeT::coordToOffset(xyz)];
            return this->getValueSecondary(xyz);
        } else return this->getValueNative(xyz);
    }
    __declspec(noinline) const ValueType& getValueSecondary(const Coord& xyz) const
    {
#ifdef EXPERIMENT_LEAF_CACHE
        const auto leaf = mSecondaryLeaves[secondarySlot(xyz)];
        if (leaf && leaf->origin() == (xyz & ~Coord::ValueType(LeafNodeT::DIM-1))) {
            this->insert(xyz, leaf);
            return LeafCacheT::buffer()[LeafNodeT::coordToOffset(xyz)];
        }
#endif
        return this->getValueNative(xyz);
    }
    const ValueType& getValueNative(const Coord& xyz) const
#else
    const ValueType& getValue(const Coord& xyz) const
#endif
    {''')
    replace('''    void setValue(const Coord& xyz, const ValueType& value)
    {''', '''#ifdef EXPERIMENT_SPLIT_CACHE
    OPENVDB_FORCE_INLINE void setValue(const Coord& xyz, const ValueType& value)
    {
        static_assert(!BaseT::IsConstTree, "can't modify a const tree's values");
        if constexpr (BypassLeafAPI && std::is_same<MutexT, void>::value) {
            if (this->template isHashed<LeafNodeT>(xyz)) {
                this->setCachedLeafValue(xyz, value);
                return;
            }
            this->setValueSecondary(xyz, value);
        } else this->setValueNative(xyz, value);
    }
    OPENVDB_FORCE_INLINE void setCachedLeafValue(const Coord& xyz, const ValueType& value)
    {
        const auto offset = LeafNodeT::coordToOffset(xyz);
        const_cast<ValueType&>(LeafCacheT::buffer()[offset]) = value;
        auto leaf = mNodes.template get<0>();
#ifdef EXPERIMENT_DENSE_MASK
        if constexpr (LeafNodeT::LOG2DIM >= 2) {
            if (leaf->getValueMask().template getWord<Index64>(offset >> 6) != ~Index64(0))
                leaf->setValueOn(offset);
        } else leaf->setValueOn(offset);
#else
        leaf->setValueOn(offset);
#endif
    }
    __declspec(noinline) void setValueSecondary(const Coord& xyz, const ValueType& value)
    {
#ifdef EXPERIMENT_LEAF_CACHE
        const auto leaf = mSecondaryLeaves[secondarySlot(xyz)];
        if (leaf && leaf->origin() == (xyz & ~Coord::ValueType(LeafNodeT::DIM-1))) {
            this->insert(xyz, leaf);
            this->setCachedLeafValue(xyz, value);
            return;
        }
#endif
        this->setValueNative(xyz, value);
    }
    void setValueNative(const Coord& xyz, const ValueType& value)
#else
    void setValue(const Coord& xyz, const ValueType& value)
#endif
    {''')
    destination.mkdir(parents=True, exist_ok=True)
    (destination / 'ValueAccessor.h').write_text(modified, encoding='utf-8')
    (evidence / 'native-cache-transfer.patch').write_text(''.join(difflib.unified_diff(
        original.splitlines(True), modified.splitlines(True),
        fromfile='a/openvdb/openvdb/tree/ValueAccessor.h', tofile='b/openvdb/openvdb/tree/ValueAccessor.h')), encoding='utf-8')
