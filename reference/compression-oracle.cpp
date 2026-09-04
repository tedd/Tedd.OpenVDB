// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Uses the pinned OpenVDB implementation for compression/half files and their canonical expected values.
#define main uncompressed_reference_main
#include "io-oracle.cpp"
#undef main

std::string key(const std::string& scalar, unsigned compression, bool half) {
    return scalar + "-" + std::to_string(compression) + (half ? "-half" : "-full");
}
template<typename GridT> typename GridT::Ptr compressionGrid() {
    using T = typename GridT::ValueType;
    const T background = std::is_integral_v<T> ? T(10) : T(10.123);
    auto grid = GridT::create(background);
    grid->setName("compression_profiles");
    grid->insertMeta("purpose", openvdb::StringMetadata("native active-mask, zlib and half reference"));
    grid->setTransform(openvdb::math::Transform::createLinearTransform(matrix()));
    auto& tree = grid->tree();
    for (int profile = 0; profile <= 9; ++profile) {
        auto* leaf = tree.touchLeaf(Coord(profile * 8,0,0));
        T initial = background;
        if (profile == 1) initial = -background;
        else if (profile == 2 || profile == 5 || profile == 6) initial = T(17.123);
        leaf->fill(initial, false);
        if (profile == 3) leaf->setValueOff(0, -background);
        if (profile == 4) leaf->setValueOff(0, T(17.123));
        if (profile == 5 || profile == 6) leaf->setValueOff(0, T(23.456));
        if (profile == 6) leaf->setValueOff(1, T(29.789));
        if (profile < 8) leaf->setValueOn(7, T(31.234));
        if (profile == 7 || profile == 9) {
            leaf->setValuesOn();
            uint32_t rng = 0x12345678;
            for (unsigned offset = 0; offset < 512; ++offset) {
                rng = rng * 1664525u + 1013904223u;
                T value = std::is_integral_v<T> ? T(static_cast<int32_t>(rng)) : T(static_cast<int32_t>(rng)) / T(100000);
                leaf->setValueOnly(offset, value);
            }
            if constexpr (std::is_floating_point_v<T>) {
                const T values[] = {T(0), -T(0), T(1.00048828125), T(1.00146484375), T(0.000000059604644775390625),
                    T(0.0000000298023223876953125), T(65504), T(65520), T(-65520), std::numeric_limits<T>::infinity(),
                    -std::numeric_limits<T>::infinity(), std::numeric_limits<T>::quiet_NaN(), T(1.000488281250001)};
                for (unsigned offset = 0; offset < sizeof(values)/sizeof(values[0]); ++offset) leaf->setValueOnly(offset, values[offset]);
            }
        }
    }
    tree.addTile(1, Coord(128,0,0), T(1.0001), true);
    tree.addTile(2, Coord(256,0,0), T(17.123), false);
    tree.addTile(3, Coord(-4096,-4096,-4096), T(13.123), true);
    tree.addTile(3, Coord(4096,4096,4096), T(19.123), false);
    return grid;
}
void save(const fs::path& path, openvdb::GridBase::Ptr grid, unsigned flags) {
    openvdb::io::File archive(path.string()); archive.setCompression(flags);
    archive.setInstancingEnabled(false); archive.setGridStatsMetadataEnabled(false);
    archive.write(openvdb::GridPtrVec{grid});
}
openvdb::GridBase::Ptr load(const fs::path& path) {
    std::ifstream input(path, std::ios::binary); if (!input) throw std::runtime_error("cannot open " + path.string());
    openvdb::io::Stream archive(input, false); auto grids = archive.getGrids();
    if (grids->size() != 1) throw std::runtime_error("expected one grid"); return (*grids)[0];
}
template<typename GridT> void emit(const fs::path& directory, const std::string& scalar) {
    for (bool half : {false,true}) {
        if (half && std::is_integral_v<typename GridT::ValueType>) continue;
        for (unsigned flags : {0u,1u,2u,3u}) {
            auto grid = compressionGrid<GridT>(); grid->setSaveFloatAsHalf(half);
            const auto path = directory / ("native-" + key(scalar,flags,half) + ".vdb"); save(path,grid,flags);
            if (flags == 0) {
                auto expected = load(path); expected->setSaveFloatAsHalf(false);
                save(directory / ("expected-" + scalar + (half ? "-half.vdb" : "-full.vdb")), expected, 0);
            }
            std::cout << "generated " << path.filename().string() << '\n';
        }
    }
}
template<typename GridT> void compare(const fs::path& actualPath, const fs::path& expectedPath) {
    auto actual = openvdb::gridPtrCast<GridT>(load(actualPath));
    auto expected = openvdb::gridPtrCast<GridT>(load(expectedPath));
    if (!actual || !expected) throw std::runtime_error("scalar type mismatch");
    const auto& a=actual->tree(); const auto& e=expected->tree();
    if (!a.hasSameTopology(e) || a.activeVoxelCount()!=e.activeVoxelCount() || a.leafCount()!=e.leafCount() || !same(a.background(),e.background()))
        throw std::runtime_error("topology/count/background mismatch");
    for (auto it=e.cbeginValueAll();it;++it)
        if (!same(a.getValue(it.getCoord()),*it) || a.isValueOn(it.getCoord())!=it.isValueOn()) throw std::runtime_error("stored value mismatch");
    for (auto it=a.cbeginValueAll();it;++it)
        if (!same(e.getValue(it.getCoord()),*it)) throw std::runtime_error("unexpected stored value");
    if (actual->getName()!=expected->getName()) throw std::runtime_error("name mismatch");
    for (auto it=expected->beginMeta();it!=expected->endMeta();++it) {
        auto meta=(*actual)[it->first];
        if (!meta || !(*meta==*it->second)) throw std::runtime_error("metadata mismatch: " + it->first);
    }
    if (!actual->indexToWorld(openvdb::Vec3d(1.25,2.5,-3.75)).eq(expected->indexToWorld(openvdb::Vec3d(1.25,2.5,-3.75)),1e-12))
        throw std::runtime_error("transform mismatch");
    std::cout << "verified " << actualPath.filename().string() << " against " << expectedPath.filename().string() << '\n';
}
int main(int argc,char** argv) {
    try {
        openvdb::initialize();
        if (argc==3 && std::string(argv[1])=="generate") {
            fs::path directory(argv[2]); fs::create_directories(directory);
            emit<openvdb::Int32Grid>(directory,"int32"); emit<openvdb::FloatGrid>(directory,"float"); emit<openvdb::DoubleGrid>(directory,"double");
        } else if (argc==5 && std::string(argv[1])=="verify") {
            const std::string scalar(argv[4]);
            if (scalar=="int32") compare<openvdb::Int32Grid>(argv[2],argv[3]);
            else if (scalar=="float") compare<openvdb::FloatGrid>(argv[2],argv[3]);
            else if (scalar=="double") compare<openvdb::DoubleGrid>(argv[2],argv[3]);
            else throw std::runtime_error("unsupported scalar");
        } else throw std::runtime_error("usage: compression-oracle generate dir | verify actual expected scalar");
        return 0;
    } catch(const std::exception& error) { std::cerr<<error.what()<<'\n'; return 1; }
}
