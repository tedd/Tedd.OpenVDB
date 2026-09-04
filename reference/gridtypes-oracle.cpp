// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main original_io_main
#include "io-oracle.cpp"
#undef main

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
template<typename GridT> void typeEmit(const fs::path& directory,const std::string& name,bool canHalf) {
    for(bool half:{false,true}) {
        if(half&&!canHalf)continue;
        for(unsigned flags:{0u,1u,2u,3u}) {
            auto key=name+"-"+std::to_string(flags)+(half?"-half":"-full");auto path=directory/("native-"+key+".vdb");typeSave(path,typeGrid<GridT>(half),flags);
            if(flags==0){auto decoded=typeLoad(path);decoded->setSaveFloatAsHalf(false);typeSave(directory/("expected-"+name+(half?"-half.vdb":"-full.vdb")),decoded,0);}
            std::cout<<"generated "<<key<<'\n';
        }
    }
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
void maskInheritance(const fs::path& path) {
    std::ofstream output(path,std::ios::binary);
    if(!output)throw std::runtime_error("cannot write mask inheritance fixture");
    output<<"{\"upstream\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"operation\":\"touchLeaf_then_setActiveState\",\"cases\":[";
    bool first=true;
    for(int level:{1,2,3})for(bool value:{false,true})for(bool active:{false,true})for(bool state:{false,true}) {
        openvdb::MaskTree tree(false);Coord position(-1,9,-9);tree.addTile(level,position,value,active);
        tree.touchLeaf(position)->setActiveState(position,state);
        if(!first)output<<',';first=false;
        output<<"{\"level\":"<<level<<",\"value\":"<<(value?"true":"false")<<",\"active\":"<<(active?"true":"false")<<",\"state\":"<<(state?"true":"false")
            <<",\"activeCount\":"<<tree.activeVoxelCount()<<",\"leafCount\":"<<tree.leafCount()<<",\"samples\":[";
        bool firstSample=true;
        for(Coord p:{position,Coord(-2,9,-9),Coord(-8,8,-16),Coord(-1,15,-9),Coord(-9,9,-9),Coord(0,9,-9)}) {
            if(!firstSample)output<<',';firstSample=false;
            output<<"{\"coord\":["<<p.x()<<','<<p.y()<<','<<p.z()<<"],\"value\":"<<(tree.getValue(p)?"true":"false")<<",\"active\":"<<(tree.isValueOn(p)?"true":"false")<<"}";
        }
        output<<"]}";
    }
    output<<"]}";output.close();if(!output)throw std::runtime_error("mask fixture write failed");
    std::cout<<"generated 24 native mask inheritance cases\n";
}int main(int argc,char** argv) {
    try {
        openvdb::initialize();
        if(argc==3&&std::string(argv[1])=="mask-inheritance") maskInheritance(argv[2]);
        else if(argc==3&&std::string(argv[1])=="generate") {
            fs::path directory(argv[2]);fs::create_directories(directory);
            typeEmit<openvdb::Int64Grid>(directory,"int64",false);typeEmit<openvdb::BoolGrid>(directory,"bool",false);typeEmit<openvdb::MaskGrid>(directory,"mask",false);
            typeEmit<openvdb::Vec3IGrid>(directory,"vec3i",false);typeEmit<openvdb::Vec3SGrid>(directory,"vec3s",true);typeEmit<openvdb::Vec3DGrid>(directory,"vec3d",true);
        } else if(argc==5&&std::string(argv[1])=="verify") {
            std::string type(argv[4]);
            if(type=="int64")typeVerify<openvdb::Int64Grid>(argv[2],argv[3]);else if(type=="bool")typeVerify<openvdb::BoolGrid>(argv[2],argv[3]);
            else if(type=="mask")typeVerify<openvdb::MaskGrid>(argv[2],argv[3]);else if(type=="vec3i")typeVerify<openvdb::Vec3IGrid>(argv[2],argv[3]);
            else if(type=="vec3s")typeVerify<openvdb::Vec3SGrid>(argv[2],argv[3]);else if(type=="vec3d")typeVerify<openvdb::Vec3DGrid>(argv[2],argv[3]);else throw std::runtime_error("type not supported");
        }else throw std::runtime_error("usage: gridtypes-oracle generate directory | verify actual expected type");
        return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
