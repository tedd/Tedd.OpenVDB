// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Explicit instance graphs written with native Archive methods; ordinary native write() emits backward references only.
#include <openvdb/openvdb.h>
#include <openvdb/io/Archive.h>
#include <openvdb/io/GridDescriptor.h>
#include <openvdb/io/File.h>
#include <openvdb/io/Stream.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <string>
#include <vector>
using namespace openvdb;
namespace fs=std::filesystem;
class ExplicitArchive:public io::Archive {
public:
 void emit(const fs::path& path,const GridPtrVec& grids,const std::vector<std::string>& names,const std::vector<std::string>& parents,bool seek) {
  setCompression(3);setGridStatsMetadataEnabled(false);
  std::ofstream out(path,std::ios::binary);
  io::StreamMetadata::Ptr state(new io::StreamMetadata);
  io::setStreamMetadataPtr(out,state,false);io::setDataCompression(out,compression());io::setWriteGridStatsMetadata(out,false);
  writeHeader(out,seek);MetaMap metadata;metadata.insertMeta("fixture",StringMetadata("native explicit instance graph"));metadata.writeMeta(out);
  int32_t count=int32_t(grids.size());out.write(reinterpret_cast<const char*>(&count),4);
  for(size_t i=0;i<grids.size();++i) {
   io::GridDescriptor descriptor(names[i],grids[i]->type(),grids[i]->saveFloatAsHalf());descriptor.setInstanceParentName(parents[i]);
   if(parents[i].empty())writeGrid(descriptor,grids[i],out,seek);else writeGridInstance(descriptor,grids[i],out,seek);
  }
 }
};
struct Loaded {GridPtrVecPtr grids;MetaMap::Ptr metadata;};
Loaded load(const fs::path& path) {std::ifstream input(path,std::ios::binary);io::Stream archive(input,false);return {archive.getGrids(),archive.getMetadata()};}
void save(const fs::path& path,const Loaded& data) {io::File file(path.string());file.setCompression(3);file.setGridStatsMetadataEnabled(false);file.write(*data.grids,*data.metadata);}
void describe(const fs::path& path,const Loaded& data) {
 std::cout<<path.filename().string()<<" native-stream";
 for(auto grid:*data.grids)std::cout<<" "<<grid->getName()<<":"<<grid->baseTree().leafCount()<<":"<<grid->activeVoxelCount();
 std::cout<<" sharing=";for(size_t i=0;i<data.grids->size();++i)for(size_t j=i+1;j<data.grids->size();++j)std::cout<<((*data.grids)[i]->baseTreePtr()==(*data.grids)[j]->baseTreePtr());std::cout<<'\n';
}
void resolve(Loaded& data,const std::vector<std::string>& names,const std::vector<std::string>& parents) {
 std::map<std::string,size_t> indices;for(size_t i=0;i<names.size();++i)if(!indices.emplace(names[i],i).second)throw std::runtime_error("duplicate descriptor");
 for(size_t i=0;i<names.size();++i) {
  size_t root=i,steps=0;while(!parents[root].empty()) {if(++steps>names.size())throw std::runtime_error("cycle");root=indices.at(parents[root]);}
  if(root!=i)(*data.grids)[i]->setTree((*data.grids)[root]->baseTreePtr());
 }
}
void initializeGrids(GridPtrVec& grids,const std::vector<std::string>& names) {
 for(size_t i=0;i<grids.size();++i) {
  grids[i]->setName(io::GridDescriptor::stripSuffix(names[i]));grids[i]->insertMeta("independent",Int32Metadata(int(i)));
  grids[i]->setTransform(math::Transform::Ptr(new math::Transform(math::MapBase::Ptr(new math::TranslationMap(Vec3d(double(i)*3,2,-1))))));
 }
}
void emitScenario(const fs::path& dir,const std::string& key,GridPtrVec grids,const std::vector<std::string>& names,const std::vector<std::string>& parents,bool valid=true) {
 initializeGrids(grids,names);
 for(bool seek:{false,true}) {
  std::string suffix=key+(seek?"-seek.vdb":"-stream.vdb");auto path=dir/("native-"+suffix);ExplicitArchive().emit(path,grids,names,parents,seek);
  try {
   auto data=load(path);describe(path,data);
   if(valid) {save(dir/("expected-native-"+suffix),data);resolve(data,names,parents);save(dir/("expected-resolved-"+suffix),data);}
  } catch(const std::exception& e) {if(valid)throw;std::cout<<path.filename().string()<<" native-stream ERROR "<<e.what()<<'\n';}
 }
}
void scenario(const fs::path& dir,const std::string& key,const std::vector<std::string>& names,const std::vector<std::string>& parents,int special=0,bool valid=true) {
 GridPtrVec grids;
 for(size_t i=0;i<names.size();++i) {
  GridBase::Ptr base;
  if(special==1&&i==1) {auto grid=Int32Grid::create(-7);grid->tree().setValue(Coord(1,2,3),42);base=grid;}
  else {auto grid=FloatGrid::create(-7);grid->tree().setValue(Coord(1,2,3),42);grid->tree().setValueOff(Coord(-1,2,3),17);if((special==2&&i==0)||(special==3&&i==1))grid->setSaveFloatAsHalf(true);base=grid;}
  grids.push_back(base);
 }
 emitScenario(dir,key,grids,names,parents,valid);
}
template<typename GridT>void typeScenario(const fs::path& dir,const std::string& type,typename GridT::ValueType background,typename GridT::ValueType value) {
 auto base=GridT::create(background);base->tree().setValue(Coord(1,2,3),value);base->tree().addTile(1,Coord(8,0,0),value,true);
 auto a=base->copyGrid(),b=base->copyGrid();emitScenario(dir,"forward-"+type,{a,b,base},{"a","b","z"},{"z","z",""});
}
void generate(const fs::path& dir) {
 fs::create_directories(dir);
 scenario(dir,"forward",{"a_child","z_base"},{"z_base",""});
 scenario(dir,"forward-chain-unresolved",{"a_child","b_middle","z_base"},{"b_middle","z_base",""});
 scenario(dir,"forward-chain-resolved",{"b_middle","c_child","z_base"},{"z_base","b_middle",""});
 scenario(dir,"chain-name-order",{"z_middle","a_child","m_base"},{"m_base","z_middle",""});
 scenario(dir,"cycle",{"a","b"},{"b","a"},0,false);
 scenario(dir,"self",{"a"},{"a"},0,false);
 scenario(dir,"missing",{"a","b"},{"missing",""},0,false);
 scenario(dir,"type-mismatch",{"a","b"},{"b",""},1,false);
 scenario(dir,"half-child",{"a","b"},{"b",""},2);
 scenario(dir,"half-parent",{"a","b"},{"b",""},3);
 scenario(dir,"duplicate",{"a","a"},{"a",""},0,false);
 scenario(dir,"unique-suffix",{"repeat\x1e" "0","repeat\x1e" "1"},{"repeat\x1e" "1",""});
 typeScenario<Int32Grid>(dir,"int32",-7,42);typeScenario<Int64Grid>(dir,"int64",-7,9223372036854775807LL);
 typeScenario<FloatGrid>(dir,"float",-7,1.25f);typeScenario<DoubleGrid>(dir,"double",-7,1.25);
 typeScenario<BoolGrid>(dir,"bool",false,true);typeScenario<MaskGrid>(dir,"mask",false,true);
 typeScenario<Vec3IGrid>(dir,"vec3i",Vec3i(-1,-2,-3),Vec3i(4,5,6));
 typeScenario<Vec3SGrid>(dir,"vec3s",Vec3f(-1,-2,-3),Vec3f(1.25f,2.5f,5));
 typeScenario<Vec3DGrid>(dir,"vec3d",Vec3d(-1,-2,-3),Vec3d(1.25,2.5,5));
}
template<typename T> bool equal(const T& a,const T& b) {return a==b;}
template<typename GridT>void compareTyped(GridBase::Ptr aBase,GridBase::Ptr bBase) {
 auto a=gridPtrCast<GridT>(aBase),b=gridPtrCast<GridT>(bBase);if(!a||!b)throw std::runtime_error("type mismatch");
 auto& x=a->tree();auto& y=b->tree();
 if(!x.hasSameTopology(y)||x.activeVoxelCount()!=y.activeVoxelCount()||x.leafCount()!=y.leafCount()||!equal(x.background(),y.background()))throw std::runtime_error("tree shape/background mismatch");
 for(auto it=y.cbeginValueAll();it;++it)if(!equal(x.getValue(it.getCoord()),*it)||x.isValueOn(it.getCoord())!=it.isValueOn())throw std::runtime_error("stored value/activity mismatch");
 for(auto it=x.cbeginValueAll();it;++it)if(!equal(y.getValue(it.getCoord()),*it))throw std::runtime_error("unexpected stored value");
}
void compareMetadata(const MetaMap& a,const MetaMap& b) {for(auto it=b.beginMeta();it!=b.endMeta();++it) {if(it->second->typeName()=="__delayedload")continue;auto m=a[it->first];if(!m||!(*m==*it->second))throw std::runtime_error("metadata mismatch "+it->first);}}
void verify(const fs::path& actual,const fs::path& expected) {
 auto a=load(actual),b=load(expected);if(a.grids->size()!=b.grids->size())throw std::runtime_error("grid count mismatch");compareMetadata(*a.metadata,*b.metadata);
 for(size_t i=0;i<a.grids->size();++i) {
  auto x=(*a.grids)[i],y=(*b.grids)[i];compareMetadata(*x,*y);
  if(y->isType<Int32Grid>())compareTyped<Int32Grid>(x,y);else if(y->isType<Int64Grid>())compareTyped<Int64Grid>(x,y);
  else if(y->isType<FloatGrid>())compareTyped<FloatGrid>(x,y);else if(y->isType<DoubleGrid>())compareTyped<DoubleGrid>(x,y);
  else if(y->isType<BoolGrid>())compareTyped<BoolGrid>(x,y);else if(y->isType<MaskGrid>())compareTyped<MaskGrid>(x,y);
  else if(y->isType<Vec3IGrid>())compareTyped<Vec3IGrid>(x,y);else if(y->isType<Vec3SGrid>())compareTyped<Vec3SGrid>(x,y);else if(y->isType<Vec3DGrid>())compareTyped<Vec3DGrid>(x,y);
  else throw std::runtime_error("unsupported comparison type");
  for(auto p:{Vec3d(0),Vec3d(1.25,-2,5)})if(!x->indexToWorld(p).eq(y->indexToWorld(p),1e-12))throw std::runtime_error("transform mismatch");
  for(size_t j=0;j<i;++j) {
   bool sharedA=x->baseTreePtr()==(*a.grids)[j]->baseTreePtr(),sharedB=y->baseTreePtr()==(*b.grids)[j]->baseTreePtr();
   // Native writer emits separate copies when the serialization half flags differ.
   if(x->saveFloatAsHalf()==(*a.grids)[j]->saveFloatAsHalf()&&sharedA!=sharedB)throw std::runtime_error("sharing mismatch");
  }
 }
 std::cout<<"verified "<<actual.filename().string()<<'\n';
}
int main(int argc,char**argv) {
 try {initialize();if(argc==3&&std::string(argv[1])=="generate")generate(argv[2]);else if(argc==4&&std::string(argv[1])=="verify")verify(argv[2],argv[3]);else throw std::runtime_error("usage: instance-oracle generate dir | verify actual expected");return 0;}
 catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
