// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main original_io_main
#include "io-oracle.cpp"
#undef main

openvdb::MetaMap fileMetadata() {
    openvdb::MetaMap meta; meta.insertMeta("creator",openvdb::StringMetadata("managed port reference"));
    meta.insertMeta("revision",openvdb::Int64Metadata(225));
    openvdb::UnknownMetadata opaque("vendor_test_v1"); opaque.setValue({0,1,127,128,255,42});
    meta.insertMeta("opaque",opaque); return meta;
}
openvdb::GridPtrVec archiveGrids() {
    using namespace openvdb; using namespace openvdb::math;
    auto blocks=Int32Grid::create(-7); blocks->setName("duplicate");
    blocks->tree().setValue(Coord(-1,2,3),42); blocks->tree().setValueOff(Coord(8,0,0),17);
    blocks->tree().addTile(2,Coord(128,0,0),5,true);
    blocks->setTransform(Transform::Ptr(new Transform(MapBase::Ptr(new UniformScaleMap(2)))));
    auto fog=FloatGrid::create(2.5f); fog->setName("duplicate"); fog->setSaveFloatAsHalf(true);
    fog->tree().setValue(Coord(-9,0,0),1.25f); fog->tree().setValueOff(Coord(2,3,4),-5.5f);
    fog->tree().addTile(1,Coord(8,8,8),3.75f,true);
    fog->setTransform(Transform::Ptr(new Transform(MapBase::Ptr(new NonlinearFrustumMap(
        BBoxd(Vec3d(-10,-20,0),Vec3d(110,60,100)),0.5,40,MapBase::Ptr(new AffineMap(matrix())))))));
    openvdb::UnknownMetadata opaque("vendor_grid_v2"); opaque.setValue({255,0,254,1}); fog->insertMeta("opaque",opaque);
    auto empty=DoubleGrid::create(9.25); empty->setName("");
    empty->setTransform(Transform::Ptr(new Transform(MapBase::Ptr(new ScaleMap(Vec3d(2,3,4))))));
    auto fogInstance=gridPtrCast<FloatGrid>(fog->copyGrid()); fogInstance->setName("fog_instance");
    fogInstance->setTransform(Transform::Ptr(new Transform(MapBase::Ptr(new TranslationMap(Vec3d(3,4,5))))));
    fogInstance->insertMeta("instance",BoolMetadata(true));
    auto blockInstance=gridPtrCast<Int32Grid>(blocks->copyGrid()); blockInstance->setName("duplicate");
    blockInstance->setTransform(Transform::Ptr(new Transform(MapBase::Ptr(new ScaleTranslateMap(Vec3d(2,3,4),Vec3d(5,6,7))))));
    auto uniform=DoubleGrid::create(1);uniform->setName("");
    uniform->setTransform(Transform::Ptr(new Transform(MapBase::Ptr(new UniformScaleTranslateMap(3,Vec3d(1,2,3))))));
    auto unitary=Int32Grid::create(0);unitary->setName("unitary");
    unitary->setTransform(Transform::Ptr(new Transform(MapBase::Ptr(new UnitaryMap()))));
    return {blocks,fog,empty,fogInstance,blockInstance,uniform,unitary};
}
void archiveGenerate(const fs::path& directory) {
    fs::create_directories(directory);
    for(bool streaming:{false,true}) for(bool instances:{false,true}) for(unsigned flags:{0u,3u}) {
        auto name="native-archive-"+std::to_string(flags)+(instances?"-instances":"-copies")+(streaming?"-stream.vdb":"-seek.vdb");
        auto grids=archiveGrids(); auto metadata=fileMetadata(); auto path=directory/name;
        if(streaming) {
            std::ofstream out(path,std::ios::binary);openvdb::io::Stream file(out);
            file.setCompression(flags);file.setInstancingEnabled(instances);file.setGridStatsMetadataEnabled(false);file.write(grids,metadata);
        } else {
            openvdb::io::File file(path.string());file.setCompression(flags);file.setInstancingEnabled(instances);file.setGridStatsMetadataEnabled(false);file.write(grids,metadata);
        }
        std::cout<<"generated "<<name<<'\n';
    }
}
void compareMetadata(const openvdb::MetaMap& actual,const openvdb::MetaMap& expected) {
    for(auto it=expected.beginMeta();it!=expected.endMeta();++it) {
        if(it->second->typeName()=="__delayedload")continue;
        auto meta=actual[it->first];if(!meta||!(*meta==*it->second))throw std::runtime_error("metadata mismatch: "+it->first);
    }
}
template<typename GridT> void archiveCompareGrid(openvdb::GridBase::Ptr actualBase,openvdb::GridBase::Ptr expectedBase) {
    auto actual=openvdb::gridPtrCast<GridT>(actualBase),expected=openvdb::gridPtrCast<GridT>(expectedBase);
    if(!actual||!expected)throw std::runtime_error("grid type mismatch");
    auto& a=actual->tree();auto& e=expected->tree();
    if(!a.hasSameTopology(e)||a.activeVoxelCount()!=e.activeVoxelCount()||a.leafCount()!=e.leafCount()||!same(a.background(),e.background()))throw std::runtime_error("tree counts/topology/background");
    for(auto it=e.cbeginValueAll();it;++it)if(!same(a.getValue(it.getCoord()),*it)||a.isValueOn(it.getCoord())!=it.isValueOn())throw std::runtime_error("value mismatch");
    for(auto it=a.cbeginValueAll();it;++it)if(!same(e.getValue(it.getCoord()),*it))throw std::runtime_error("unexpected value");
}
void archiveVerify(const fs::path& actualPath,const fs::path& expectedPath,bool instances) {
    std::ifstream ain(actualPath,std::ios::binary),ein(expectedPath,std::ios::binary);
    openvdb::io::Stream af(ain,false),ef(ein,false);auto a=af.getGrids(),e=ef.getGrids();
    compareMetadata(*af.getMetadata(),*ef.getMetadata());
    if(a->size()!=e->size())throw std::runtime_error("grid count mismatch");
    for(size_t i=0;i<a->size();++i) {
        auto ag=(*a)[i],eg=(*e)[i];compareMetadata(*ag,*eg);
        if(ag->getName()!=eg->getName()||ag->type()!=eg->type())throw std::runtime_error("grid name/type mismatch");
        if(eg->isType<openvdb::Int32Grid>())archiveCompareGrid<openvdb::Int32Grid>(ag,eg);
        else if(eg->isType<openvdb::FloatGrid>())archiveCompareGrid<openvdb::FloatGrid>(ag,eg);
        else archiveCompareGrid<openvdb::DoubleGrid>(ag,eg);
        for(auto point:{openvdb::Vec3d(0),openvdb::Vec3d(1.25,-2.5,3.75),openvdb::Vec3d(100,30,90)})
            if(!ag->indexToWorld(point).eq(eg->indexToWorld(point),1e-10))throw std::runtime_error("transform mismatch");
    }
    if(((*a)[0]->baseTreePtr()==(*a)[4]->baseTreePtr())!=instances||((*a)[1]->baseTreePtr()==(*a)[3]->baseTreePtr())!=instances)throw std::runtime_error("instance sharing mismatch");
    std::cout<<"verified "<<actualPath.filename().string()<<" with 7 grids, metadata, frustum and instances="<<instances<<'\n';
}
int main(int argc,char** argv) {
    try {
        openvdb::initialize();
        if(argc==3&&std::string(argv[1])=="generate")archiveGenerate(argv[2]);
        else if(argc==5&&std::string(argv[1])=="verify")archiveVerify(argv[2],argv[3],std::string(argv[4])=="instances");
        else throw std::runtime_error("usage: archive-oracle generate directory | verify actual expected instances|copies");
        return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
