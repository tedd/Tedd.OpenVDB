// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main original_io_main
#include "io-oracle.cpp"
#undef main
#include <blosc.h>
#include <cstring>
#include <vector>
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

template<> bool same<openvdb::Vec3f>(openvdb::Vec3f a,openvdb::Vec3f b){return same(a.x(),b.x())&&same(a.y(),b.y())&&same(a.z(),b.z());}
template<> bool same<openvdb::Vec3d>(openvdb::Vec3d a,openvdb::Vec3d b){return same(a.x(),b.x())&&same(a.y(),b.y())&&same(a.z(),b.z());}
template<typename T> T sampleValue(int seed) {
    if constexpr(std::is_same_v<T,openvdb::Vec3f>||std::is_same_v<T,openvdb::Vec3d>)return T(typename T::ValueType(seed+0.123),typename T::ValueType(seed*-1.25),typename T::ValueType(seed*2.345));
    else if constexpr(std::is_same_v<T,openvdb::Vec3i>)return T(seed,seed*-2,seed*3);
    else if constexpr(std::is_same_v<T,bool>)return (seed&1)!=0;
    else return T(9007199254740993LL+seed);
}
template<typename GridT> typename GridT::Ptr typeGrid(bool half) {
    using T=typename GridT::ValueType;auto grid=GridT::create(sampleValue<T>(1));grid->setName("extended_values");grid->setSaveFloatAsHalf(half);
    grid->insertMeta("integer_vector",openvdb::Vec3IMetadata(openvdb::Vec3i(-1,2,-3)));
    grid->insertMeta("float_vector",openvdb::Vec3SMetadata(openvdb::Vec3f(1.25f,-2.5f,3.75f)));
    auto& tree=grid->tree();
    tree.addTile(3,Coord(-4096,0,0),sampleValue<T>(3),true);tree.addTile(2,Coord(0,0,0),sampleValue<T>(2),false);
    tree.addTile(1,Coord(128,0,0),sampleValue<T>(4),true);
    for(int x=0;x<24;++x)for(int y=0;y<8;++y)for(int z=0;z<8;++z) {
        int seed=x*64+y*8+z;Coord p(x,y,z);
        if(seed%5==0)tree.setValueOff(p,sampleValue<T>(seed%7));else tree.setValueOn(p,sampleValue<T>(seed%11));
    }
    tree.setValueOff(Coord(-1,-2,-3),sampleValue<T>(5));tree.setValueOn(Coord(2147483647,0,0),sampleValue<T>(6));
    if constexpr(std::is_same_v<T,int64_t>) {tree.setValueOn(Coord(1,2,3),std::numeric_limits<int64_t>::min());tree.setValueOff(Coord(3,2,1),std::numeric_limits<int64_t>::max());}
    if constexpr(std::is_same_v<T,openvdb::Vec3f>||std::is_same_v<T,openvdb::Vec3d>) {
        using S=typename T::ValueType;
        tree.setValueOn(Coord(1,2,3),T(S(-0.0),std::numeric_limits<S>::infinity(),std::numeric_limits<S>::quiet_NaN()));
        tree.setValueOff(Coord(3,2,1),T(S(1.000488281250001),S(0.000000059604644775390625),S(65520)));
    }
    return grid;
}

void typeSave(const fs::path& path,openvdb::GridBase::Ptr grid,unsigned flags) {
    openvdb::io::File file(path.string());file.setCompression(flags);file.setGridStatsMetadataEnabled(false);file.setInstancingEnabled(false);file.write(openvdb::GridPtrVec{grid});
}
openvdb::GridBase::Ptr typeLoad(const fs::path& path) {std::ifstream in(path,std::ios::binary);openvdb::io::Stream file(in,false);auto grids=file.getGrids();if(grids->size()!=1)throw std::runtime_error("expected one grid");return (*grids)[0];}
template<typename GridT> void bloscEmit(const fs::path& directory,const std::string& name,bool canHalf,bool scalar) {
    for(bool half:{false,true}) {
        if(half&&!canHalf)continue;
        for(unsigned flags:{4u,5u,6u,7u}) {
            typename GridT::Ptr grid;
            if constexpr(std::is_same_v<GridT,openvdb::Int32Grid>||std::is_same_v<GridT,openvdb::FloatGrid>||std::is_same_v<GridT,openvdb::DoubleGrid>)grid=compressionGrid<GridT>();
            else grid=typeGrid<GridT>(half);
            grid->setSaveFloatAsHalf(half);
            const auto path=directory/("native-"+name+"-"+std::to_string(flags)+(half?"-half.vdb":"-full.vdb"));
            typeSave(path,grid,flags);std::cout<<"generated "<<path.filename().string()<<'\n';
        }
    }
}
void frameFixture(const fs::path& directory,const std::string& name,const char* codec,int shuffle,int typeSize,int blockSize,int splitMode,int level,bool noise=false) {
    auto grid=openvdb::FloatGrid::create(0);grid->setName("frame_case");
    std::vector<float> values(512);uint32_t random=1234567;
    for(size_t i=0;i<values.size();++i){if(noise){random=random*1664525u+1013904223u;std::memcpy(&values[i],&random,4);}else values[i]=float(i%7)*0.123f;}
    auto leaf=grid->tree().touchLeaf(Coord(0));for(unsigned i=0;i<512;++i)leaf->setValueOn(i,values[i]);
    fs::path base=directory/("frame-"+name+".vdb");typeSave(base,grid,6);
    std::ifstream in(base,std::ios::binary);in.seekg(65);openvdb::readString(in);openvdb::readString(in);openvdb::readString(in);
    int64_t offsets=int64_t(in.tellg()),gridPos,blockPos,endPos;in.read((char*)&gridPos,8);in.read((char*)&blockPos,8);in.read((char*)&endPos,8);
    const int64_t chunkStart=blockPos+65;in.seekg(0);std::vector<char> prefix(static_cast<size_t>(chunkStart));in.read(prefix.data(),chunkStart);in.close();
    std::vector<char> frame(values.size()*sizeof(float)+BLOSC_MAX_OVERHEAD);
    blosc_set_splitmode(splitMode);
    int compressed=blosc_compress_ctx(level,shuffle,typeSize,values.size()*sizeof(float),values.data(),frame.data(),frame.size(),codec,blockSize,1);
    blosc_set_splitmode(BLOSC_FORWARD_COMPAT_SPLIT);
    if(compressed<=0)throw std::runtime_error("frame compression failed: "+name);
    frame.resize(compressed);int64_t frameBytes=compressed,newEnd=chunkStart+8+frameBytes;
    std::ofstream out(base,std::ios::binary|std::ios::trunc);out.write(prefix.data(),prefix.size());out.write((char*)&frameBytes,8);out.write(frame.data(),frame.size());out.seekp(offsets+16);out.write((char*)&newEnd,8);out.close();
    auto decoded=typeLoad(base);typeSave(directory/("expected-frame-"+name+".vdb"),decoded,0);
    auto actual=openvdb::gridPtrCast<openvdb::FloatGrid>(decoded);
    for(unsigned i=0;i<512;++i)if(!same(actual->tree().getValue(leaf->offsetToGlobalCoord(i)),values[i]))throw std::runtime_error("native custom frame mismatch");
    std::cout<<"generated native frame "<<name<<" flags="<<unsigned(uint8_t(frame[2]))<<'\n';
}
template<typename GridT> void typeVerify(const fs::path& actualPath,const fs::path& expectedPath) {
    auto actual=openvdb::gridPtrCast<GridT>(typeLoad(actualPath)),expected=openvdb::gridPtrCast<GridT>(typeLoad(expectedPath));
    if(!actual||!expected)throw std::runtime_error("grid type mismatch");auto& a=actual->tree();auto& e=expected->tree();
    if(!a.hasSameTopology(e)||a.activeVoxelCount()!=e.activeVoxelCount()||a.leafCount()!=e.leafCount()||!same(a.background(),e.background()))throw std::runtime_error("topology/counts/background mismatch");
    for(auto it=e.cbeginValueAll();it;++it)if(!same(a.getValue(it.getCoord()),*it)||a.isValueOn(it.getCoord())!=it.isValueOn())throw std::runtime_error("value/activity mismatch");
    for(auto it=a.cbeginValueAll();it;++it)if(!same(e.getValue(it.getCoord()),*it))throw std::runtime_error("unexpected value");
    for(auto it=expected->beginMeta();it!=expected->endMeta();++it) {if(it->second->typeName()=="__delayedload")continue;auto meta=(*actual)[it->first];if(!meta||!(*meta==*it->second))throw std::runtime_error("metadata mismatch");}
    std::cout<<"verified "<<actualPath.filename().string()<<" leafCount="<<a.leafCount()<<" active="<<a.activeVoxelCount()<<'\n';
}

int main(int argc,char** argv) {
    try {
        openvdb::initialize();blosc_init();
        if(argc==3&&std::string(argv[1])=="generate") {
            fs::path directory(argv[2]);fs::create_directories(directory);
            bloscEmit<openvdb::Int32Grid>(directory,"int32",false,true);bloscEmit<openvdb::FloatGrid>(directory,"float",true,true);bloscEmit<openvdb::DoubleGrid>(directory,"double",true,true);
            bloscEmit<openvdb::Int64Grid>(directory,"int64",false,false);bloscEmit<openvdb::BoolGrid>(directory,"bool",false,false);bloscEmit<openvdb::MaskGrid>(directory,"mask",false,false);
            bloscEmit<openvdb::Vec3IGrid>(directory,"vec3i",false,false);bloscEmit<openvdb::Vec3SGrid>(directory,"vec3s",true,false);bloscEmit<openvdb::Vec3DGrid>(directory,"vec3d",true,false);
            frameFixture(directory,"lz4-split","lz4",1,4,2048,BLOSC_FORWARD_COMPAT_SPLIT,9);
            frameFixture(directory,"lz4-unsplit","lz4",1,4,2048,BLOSC_NEVER_SPLIT,9);
            frameFixture(directory,"lz4-multiblock","lz4",1,4,256,BLOSC_NEVER_SPLIT,9);
            frameFixture(directory,"lz4-leftover","lz4",1,3,511,BLOSC_NEVER_SPLIT,9);
            frameFixture(directory,"lz4-plain","lz4",0,1,128,BLOSC_NEVER_SPLIT,9);
            frameFixture(directory,"memcpy","lz4",1,4,2048,BLOSC_FORWARD_COMPAT_SPLIT,0);
            frameFixture(directory,"noise","lz4",1,4,2048,BLOSC_FORWARD_COMPAT_SPLIT,9,true);
            frameFixture(directory,"zlib","zlib",1,4,2048,BLOSC_FORWARD_COMPAT_SPLIT,9);
            frameFixture(directory,"bitshuffle","lz4",2,4,2048,BLOSC_FORWARD_COMPAT_SPLIT,9);
            frameFixture(directory,"blosclz","blosclz",1,4,2048,BLOSC_FORWARD_COMPAT_SPLIT,9);
            frameFixture(directory,"zstd","zstd",1,4,2048,BLOSC_FORWARD_COMPAT_SPLIT,9);
        } else if(argc==5&&std::string(argv[1])=="verify") {
            std::string type(argv[4]);
            if(type=="int32")typeVerify<openvdb::Int32Grid>(argv[2],argv[3]);else if(type=="float")typeVerify<openvdb::FloatGrid>(argv[2],argv[3]);else if(type=="double")typeVerify<openvdb::DoubleGrid>(argv[2],argv[3]);
            else if(type=="int64")typeVerify<openvdb::Int64Grid>(argv[2],argv[3]);else if(type=="bool")typeVerify<openvdb::BoolGrid>(argv[2],argv[3]);
            else if(type=="mask")typeVerify<openvdb::MaskGrid>(argv[2],argv[3]);else if(type=="vec3i")typeVerify<openvdb::Vec3IGrid>(argv[2],argv[3]);
            else if(type=="vec3s")typeVerify<openvdb::Vec3SGrid>(argv[2],argv[3]);else if(type=="vec3d")typeVerify<openvdb::Vec3DGrid>(argv[2],argv[3]);else throw std::runtime_error("type not supported");
        }else throw std::runtime_error("usage: blosc-oracle generate directory | verify actual expected type");
        blosc_destroy();return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
