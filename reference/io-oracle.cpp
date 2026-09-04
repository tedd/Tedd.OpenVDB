// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Writes genuine native .vdb files and independently verifies managed output.
#include <openvdb/openvdb.h>
#include <openvdb/io/File.h>
#include <openvdb/io/Stream.h>
#include <fstream>
#include <iostream>
#include <filesystem>
#include <limits>
#include <stdexcept>
namespace fs = std::filesystem;
using openvdb::Coord;

openvdb::math::Mat4d matrix() {
    return openvdb::math::Mat4d(2.0,0.5,0.0,0.0, 0.0,3.0,0.25,0.0, 0.0,0.0,4.0,0.0, 10.0,-20.0,30.0,1.0);
}
template<typename GridT> typename GridT::Ptr makeGrid(const std::string& scenario) {
    using T = typename GridT::ValueType;
    auto grid = GridT::create(T(-7));
    grid->setName("oracle_" + scenario);
    grid->setGridClass(openvdb::GRID_FOG_VOLUME);
    grid->insertMeta("answer", openvdb::Int32Metadata(42));
    grid->insertMeta("enabled", openvdb::BoolMetadata(true));
    grid->insertMeta("epoch", openvdb::Int64Metadata(1234567890123LL));
    grid->insertMeta("float", openvdb::FloatMetadata(1.25f));
    grid->insertMeta("double", openvdb::DoubleMetadata(2.5));
    grid->insertMeta("caption", openvdb::StringMetadata("native UTF-8: \xc3\xa6\xc3\xb8\xc3\xa5"));
    grid->insertMeta("origin", openvdb::Vec3IMetadata(openvdb::Vec3i(-1,2,-3)));
    grid->insertMeta("direction", openvdb::Vec3DMetadata(openvdb::Vec3d(1.25,-2.5,3.75)));
    grid->insertMeta("matrix", openvdb::Mat4DMetadata(matrix()));
    grid->setTransform(openvdb::math::Transform::createLinearTransform(matrix()));
    if (scenario == "empty") return grid;
    auto& tree = grid->tree();
    tree.addTile(3, Coord(-4096,-4096,-4096), T(3), true);
    tree.addTile(3, Coord(4096,0,0), T(13), false);
    tree.addTile(2, Coord(0,0,0), T(5), false);
    tree.addTile(1, Coord(128,0,0), T(7), true);
    tree.setValueOn(Coord(-1,0,1), T(11));
    tree.setValueOff(Coord(7,8,9), T(17));
    tree.setValueOn(Coord(2147483647,2147483647,2147483647), T(23));
    tree.setValueOff(Coord(-2147483647-1,-2147483647-1,-2147483647-1), T(29));
    if constexpr (std::is_floating_point_v<T>) {
        tree.setValueOn(Coord(1,2,3), T(1.25));
        tree.setValueOff(Coord(2,3,4), -T(0));
        tree.setValueOn(Coord(3,4,5), std::numeric_limits<T>::infinity());
        tree.setValueOff(Coord(4,5,6), -std::numeric_limits<T>::infinity());
        tree.setValueOn(Coord(5,6,7), std::numeric_limits<T>::quiet_NaN());
    }
    return grid;
}
template<typename GridT> void generate(const fs::path& directory, const std::string& scalar, const std::string& scenario, bool streaming) {
    auto grid = makeGrid<GridT>(scenario);
    const auto file = directory / ("native-" + scalar + "-" + scenario + (streaming ? "-stream.vdb" : ".vdb"));
    if (streaming) {
        std::ofstream out(file, std::ios::binary);
        openvdb::io::Stream archive(out);
        archive.setCompression(openvdb::io::COMPRESS_NONE);
        archive.setInstancingEnabled(false);
        archive.setGridStatsMetadataEnabled(false);
        archive.write(openvdb::GridPtrVec{grid});
    } else {
        openvdb::io::File archive(file.string());
        archive.setCompression(openvdb::io::COMPRESS_NONE);
        archive.setInstancingEnabled(false);
        archive.setGridStatsMetadataEnabled(false);
        archive.write(openvdb::GridPtrVec{grid});
    }
    std::cout << "wrote " << file.filename().string() << '\n';
}
template<typename T> bool same(T a, T b) {
    if constexpr (std::is_floating_point_v<T>) {
        if (std::isnan(a) && std::isnan(b)) return true;
        if (a == 0 && b == 0) return std::signbit(a) == std::signbit(b);
    }
    return a == b;
}
template<typename GridT> void verify(const fs::path& file, const std::string& scenario) {
    std::ifstream in(file, std::ios::binary);
    if (!in) throw std::runtime_error("cannot open managed file");
    openvdb::io::Stream archive(in, false);
    auto grids = archive.getGrids();
    if (grids->size() != 1) throw std::runtime_error("expected exactly one grid");
    auto actual = openvdb::gridPtrCast<GridT>((*grids)[0]);
    if (!actual) throw std::runtime_error("grid type mismatch");
    auto expected = makeGrid<GridT>(scenario);
    if (actual->getName() != expected->getName()) throw std::runtime_error("name mismatch");
    if (actual->getGridClass() != expected->getGridClass()) throw std::runtime_error("class mismatch");
    for (auto meta = expected->beginMeta(); meta != expected->endMeta(); ++meta) {
        auto value = (*actual)[meta->first];
        if (!value || !(*value == *meta->second)) throw std::runtime_error("metadata mismatch: " + meta->first);
    }
    const auto& a = actual->tree(); const auto& e = expected->tree();
    if (!a.hasSameTopology(e)) throw std::runtime_error("topology mismatch");
    if (!same(a.background(), e.background()) || a.leafCount() != e.leafCount() || a.activeVoxelCount() != e.activeVoxelCount())
        throw std::runtime_error("background or counts mismatch");
    // Visit every stored tile and every active/inactive leaf value, not merely sampled points.
    for (auto iter = e.cbeginValueAll(); iter; ++iter) {
        const auto coordinate = iter.getCoord();
        if (!same(a.getValue(coordinate), *iter) || a.isValueOn(coordinate) != iter.isValueOn() || a.getValueDepth(coordinate) != e.getValueDepth(coordinate))
            throw std::runtime_error("stored tile/voxel mismatch");
    }
    for (auto iter = a.cbeginValueAll(); iter; ++iter)
        if (!same(e.getValue(iter.getCoord()), *iter)) throw std::runtime_error("unexpected stored value");
    for (openvdb::Vec3d p : {openvdb::Vec3d(0), openvdb::Vec3d(1.25,-2.5,3.75)})
        if (!actual->indexToWorld(p).eq(expected->indexToWorld(p), 1e-12)) throw std::runtime_error("transform mismatch");
    std::cout << "verified " << file.filename().string() << " native leafCount=" << a.leafCount() << " activeCount=" << a.activeVoxelCount() << '\n';
}
int main(int argc, char** argv) {
    try {
        openvdb::initialize();
        if (argc == 3 && std::string(argv[1]) == "generate") {
            const fs::path directory(argv[2]); fs::create_directories(directory);
            for (const std::string scenario : {"empty", "mixed"}) {
                generate<openvdb::Int32Grid>(directory, "int32", scenario, false);
                generate<openvdb::FloatGrid>(directory, "float", scenario, false);
                generate<openvdb::DoubleGrid>(directory, "double", scenario, false);
            }
            generate<openvdb::Int32Grid>(directory, "int32", "mixed", true);
        } else if (argc == 5 && std::string(argv[1]) == "verify") {
            const std::string scalar(argv[3]), scenario(argv[4]);
            if (scalar == "int32") verify<openvdb::Int32Grid>(argv[2], scenario);
            else if (scalar == "float") verify<openvdb::FloatGrid>(argv[2], scenario);
            else if (scalar == "double") verify<openvdb::DoubleGrid>(argv[2], scenario);
            else throw std::runtime_error("unsupported scalar");
        } else throw std::runtime_error("usage: vdb-io-reference generate directory | verify file int32|float|double empty|mixed");
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
