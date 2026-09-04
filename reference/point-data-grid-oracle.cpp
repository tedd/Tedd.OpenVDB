// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/points/PointConversion.h>
#include <openvdb/points/PointCount.h>
#include <openvdb/tools/PointIndexGrid.h>
#include <tbb/global_control.h>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <vector>

using namespace openvdb;
namespace pts = openvdb::points;
namespace tools = openvdb::tools;
static std::ofstream out;

static void number(double value)
{
    if (std::isfinite(value)) out << value;
    else out << '"' << (std::isnan(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity") << '"';
}
template<typename V> static void vector3(const V& value)
{
    out << '[';
    for (int axis = 0; axis < 3; ++axis) { if (axis) out << ','; number(value[axis]); }
    out << ']';
}
static math::Transform::Ptr makeTransform(int profile)
{
    auto transform = math::Transform::createLinearTransform(profile == 0 ? 1.0 : 0.75);
    if (profile == 1) {
        transform->postTranslate(Vec3d(0.25, -0.375, 0.125));
        transform->postRotate(0.2, math::Z_AXIS);
    }
    return transform;
}
static std::vector<Vec3d> indexPositions(int scene)
{
    if (scene == 0) return {};
    if (scene == 1) return {
        Vec3d(-0.5), Vec3d(0.5), Vec3d(0.0), Vec3d(0.0),
        Vec3d(7.499, -7.5, 1.25), Vec3d(7.5, -8.5, 1.25),
        Vec3d(-8.5, 7.5, -0.5), Vec3d(-8.499, 8.5, 0.5),
        Vec3d(15.5, 15.5, 15.5), Vec3d(-16.5, -16.5, -16.5),
        Vec3d(0.499999, -0.499999, 0.25), Vec3d(0.500001, -0.500001, -0.25)
    };
    if (scene == 2) return {
        Vec3d(7.499, 0.0, 0.0), Vec3d(7.5, 0.0, 0.0),
        Vec3d(8.499, 0.0, 0.0), Vec3d(8.5, 0.0, 0.0),
        Vec3d(-7.5, 1.0, -1.0), Vec3d(-8.5, 1.0, -1.0),
        Vec3d(127.5, -127.5, 3.0), Vec3d(128.5, -128.5, 3.0),
        Vec3d(4095.5, 4.0, -4.0), Vec3d(4096.5, 4.0, -4.0),
        Vec3d(-4095.5, -4.0, 4.0), Vec3d(-4096.5, -4.0, 4.0),
        Vec3d(32767.25, -32768.25, 255.75), Vec3d(-32768.25, 32767.25, -256.75)
    };
    std::vector<Vec3d> values;
    values.reserve(46);
    for (int i = 0; i < 40; ++i) {
        values.emplace_back(((i * 47) % 97 - 48) * 3.125 + (i % 3) * 0.249,
                            ((i * 29) % 83 - 41) * 2.375 - (i % 5) * 0.125,
                            ((i * 19) % 71 - 35) * 1.625 + (i % 7) * 0.0625);
    }
    values.insert(values.end(), { Vec3d(-0.5), Vec3d(0.5), Vec3d(0.0), Vec3d(0.0),
                                  Vec3d(4095.5, -128.5, 7.5), Vec3d(-4096.5, 128.5, -8.5) });
    return values;
}
static std::vector<Vec3d> worldPositions(int scene, const math::Transform& transform)
{
    std::vector<Vec3d> values = indexPositions(scene);
    for (Vec3d& value : values) value = transform.indexToWorld(value);
    return values;
}
static void mutateMask(pts::PointDataGrid& grid)
{
    for (auto leaf = grid.tree().beginLeaf(); leaf; ++leaf) {
        int occupied = -1, empty = -1;
        int32_t start = 0;
        for (int offset = 0; offset < 512; ++offset) {
            const int32_t end = static_cast<int32_t>(leaf->getValue(offset));
            if (end > start && occupied < 0) occupied = offset;
            if (end == start && empty < 0) empty = offset;
            start = end;
        }
        if (occupied >= 0) leaf->setActiveState(occupied, false);
        if (empty >= 0) leaf->setActiveState(empty, true);
    }
}
static void integerArray(const std::vector<uint32_t>& values)
{
    out << '[';
    for (size_t i = 0; i < values.size(); ++i) { if (i) out << ','; out << values[i]; }
    out << ']';
}
static void writeLeaf(const pts::PointDataGrid& grid, const pts::PointDataTree::LeafNodeType& leaf,
                      const tools::PointIndexTree& indexTree)
{
    const auto* indexLeaf = indexTree.probeConstLeaf(leaf.origin());
    if (!indexLeaf) throw std::runtime_error("missing PointIndex leaf");
    pts::AttributeHandle<Vec3f> positions(leaf.constAttributeArray("P"));
    out << "{\"origin\":"; vector3(leaf.origin());
    out << ",\"points\":" << leaf.pointCount()
        << ",\"onVoxels\":" << leaf.onVoxelCount()
        << ",\"onPoints\":" << leaf.onPointCount()
        << ",\"offPoints\":" << leaf.offPointCount()
        << ",\"runs\":[";
    for (int offset = 0; offset < 512;) {
        if (offset) out << ',';
        const int32_t value = static_cast<int32_t>(leaf.getValue(offset));
        const bool active = leaf.isValueOn(offset);
        int end = offset + 1;
        while (end < 512 && static_cast<int32_t>(leaf.getValue(end)) == value && leaf.isValueOn(end) == active) ++end;
        out << '[' << (end - offset) << ',' << value << ',' << active << ']';
        offset = end;
    }
    std::vector<uint32_t> source, activeSource, inactiveSource;
    source.reserve(indexLeaf->indices().size());
    activeSource.reserve(indexLeaf->indices().size());
    inactiveSource.reserve(indexLeaf->indices().size());
    for (auto value : indexLeaf->indices()) source.push_back(static_cast<uint32_t>(value));
    out << "],\"source\":"; integerArray(source);
    out << ",\"local\":[";
    bool first = true;
    int32_t start = 0;
    for (int offset = 0; offset < 512; ++offset) {
        const int32_t end = static_cast<int32_t>(leaf.getValue(offset));
        const Coord voxel = leaf.offsetToGlobalCoord(offset);
        for (int32_t point = start; point < end; ++point) {
            if (!first) out << ','; first = false;
            vector3(positions.get(static_cast<Index>(point)));
            (leaf.isValueOn(offset) ? activeSource : inactiveSource).push_back(source.at(static_cast<size_t>(point)));
        }
        start = end;
    }
    out << "],\"world\":[";
    first = true; start = 0;
    for (int offset = 0; offset < 512; ++offset) {
        const int32_t end = static_cast<int32_t>(leaf.getValue(offset));
        const Coord voxel = leaf.offsetToGlobalCoord(offset);
        for (int32_t point = start; point < end; ++point) {
            if (!first) out << ','; first = false;
            const Vec3f local = positions.get(static_cast<Index>(point));
            vector3(grid.indexToWorld(voxel.asVec3d() + Vec3d(local)));
        }
        start = end;
    }
    out << "],\"activeSource\":"; integerArray(activeSource);
    out << ",\"inactiveSource\":"; integerArray(inactiveSource);
    out << '}';
}
template<typename Codec>
static void writeCase(int scene, int transformProfile, const char* codec)
{
    auto transform = makeTransform(transformProfile);
    std::vector<Vec3d> world = worldPositions(scene, *transform);
    pts::PointAttributeVector<Vec3d> source(world);
    auto indexGrid = tools::createPointIndexGrid<tools::PointIndexGrid>(source, *transform);
    auto grid = pts::createPointDataGrid<Codec, pts::PointDataGrid>(*indexGrid, source, *transform);
    const bool mutated = transformProfile == 1;
    if (mutated) mutateMask(*grid);
    out << "{\"scene\":" << scene << ",\"transform\":" << transformProfile
        << ",\"codec\":\"" << codec << "\",\"mutated\":" << mutated
        << ",\"treeType\":\"" << grid->tree().type() << "\""
        << ",\"points\":" << pts::pointCount(grid->tree(), pts::NullFilter(), false, false)
        << ",\"activeVoxels\":" << grid->activeVoxelCount()
        << ",\"roots\":" << grid->tree().root().getTableSize() << ",\"nodes\":[";
    const auto nodes = grid->tree().nodeCount();
    for (size_t i = 0; i < nodes.size(); ++i) { if (i) out << ','; out << nodes[i]; }
    out << "],\"voxelSize\":"; vector3(grid->voxelSize());
    out << ",\"zero\":"; vector3(grid->indexToWorld(Vec3d(0)));
    out << ",\"leaves\":[";
    bool first = true;
    for (auto leaf = grid->tree().cbeginLeaf(); leaf; ++leaf) {
        if (!first) out << ','; first = false;
        writeLeaf(*grid, *leaf, indexGrid->tree());
    }
    out << "]}";
}
int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    openvdb::initialize();
    tbb::global_control serial(tbb::global_control::max_allowed_parallelism, 1);
    out.open(argv[1], std::ios::binary | std::ios::trunc);
    out << std::setprecision(17)
        << "{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    bool first = true;
    for (int scene = 0; scene < 4; ++scene) for (int transform = 0; transform < 2; ++transform) {
        if (!first) out << ','; first = false;
        writeCase<pts::NullCodec>(scene, transform, "null");
    }
    for (int scene = 1; scene <= 2; ++scene) for (int transform = 0; transform < 2; ++transform) {
        out << ','; writeCase<pts::FixedPointCodec<false>>(scene, transform, "fxpt16");
    }
    out << "]}";
    return out ? 0 : 1;
}
