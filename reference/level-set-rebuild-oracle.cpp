// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/LevelSetRebuild.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <openvdb/tools/GridTransformer.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <type_traits>
using namespace openvdb;
std::ofstream out; bool first=true;
void number(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class V>void vec(const V&v){out<<'[';number(v[0]);out<<',';number(v[1]);out<<',';number(v[2]);out<<']';}
math::Transform::Ptr transform(int mode){
 if(mode==0)return math::Transform::createLinearTransform(1);
 if(mode==1)return math::Transform::createLinearTransform(1.25);
 if(mode==3)return math::Transform::createFrustumTransform(BBoxd(Vec3d(-8),Vec3d(8)),.75,12,1);
 auto t=math::Transform::createLinearTransform(1);t->preScale(Vec3d(.75,1.25,1));t->postRotate(.2,math::Z_AXIS);t->postTranslate(Vec3d(.375,-.25,.125));return t;
}
template<class G> typename G::Ptr shape(int scene){using T=typename G::ValueType;auto g=G::create(T(2.5));g->setGridClass(GRID_LEVEL_SET);
 if(scene==0||scene==1||scene==5)g=tools::createLevelSetSphere<G>(2.5f,Vec3f(.25,.5,-.25),1,2,false);
 if(scene==1)g->setGridClass(GRID_FOG_VOLUME);
 if(scene==2)g->tree().addTile(1,Coord(0),T(-1),true);
 if(scene==3)g->tree().setValueOff(Coord(0),T(-1));
 if(scene==4)g->tree().setValueOn(Coord(0),T(-1));
 if(scene==5)g->setTransform(transform(3));
 g->setName("source");g->insertMeta("custom",StringMetadata("source metadata"));return g;
}
template<class G> void run(int scene,int mode,float iso,float ex,float in,int operation=0,int outputClass=0,int kernel=0){
 using T=typename G::ValueType;auto input=shape<G>(scene);typename G::Ptr grid;
 if(operation==0)grid=tools::levelSetRebuild(*input,iso,ex,in,mode<0?nullptr:transform(mode).get());
 else{grid=G::create(T(mode==3?.1875:3.125));grid->setTransform(transform(mode));grid->setGridClass(outputClass?GRID_LEVEL_SET:GRID_FOG_VOLUME);grid->setName("retained");grid->insertMeta("custom",StringMetadata("output metadata"));grid->tree().setValueOn(Coord(30,2,-1),T(81));
  if(kernel==0)tools::resampleToMatch<tools::PointSampler>(*input,*grid);else if(kernel==1)tools::resampleToMatch<tools::BoxSampler>(*input,*grid);else tools::resampleToMatch<tools::QuadraticSampler>(*input,*grid);
 }
 if(!first)out<<',';first=false;
 out<<"{\"type\":\""<<(std::is_same_v<T,float>?"float":"double")<<"\",\"scene\":"<<scene<<",\"transform\":"<<mode<<",\"iso\":";number(iso);out<<",\"exterior\":";number(ex);out<<",\"interior\":";number(in);out<<",\"operation\":"<<operation<<",\"outputClass\":"<<outputClass<<",\"kernel\":"<<kernel<<",\"background\":";number(grid->background());out<<",\"class\":"<<int(grid->getGridClass())<<",\"name\":\""<<grid->getName()<<"\",\"active\":"<<grid->activeVoxelCount()<<",\"tiles\":"<<grid->tree().activeTileCount()<<",\"roots\":"<<grid->tree().root().getTableSize()<<",\"leaves\":[";
 bool firstLeaf=true;for(auto leaf=grid->tree().cbeginLeaf();leaf;++leaf){if(!firstLeaf)out<<',';firstLeaf=false;out<<"{\"origin\":";vec(leaf->origin());out<<",\"runs\":[";int i=0;while(i<512){if(i)out<<',';T value=leaf->getValue(i);bool active=leaf->isValueOn(i);int end=i+1;while(end<512&&leaf->getValue(end)==value&&leaf->isValueOn(end)==active)++end;out<<'['<<(end-i)<<',';number(value);out<<','<<active<<']';i=end;}out<<"]}";}
 out<<"],\"probes\":[";bool pfirst=true;for(int x:{-128,-16,-8,0,8,16,128})for(int y:{-8,0,8})for(int z:{-8,0,8}){if(!pfirst)out<<',';pfirst=false;Coord c(x,y,z);out<<'['<<x<<','<<y<<','<<z<<',';number(grid->tree().getValue(c));out<<','<<grid->tree().isValueOn(c)<<','<<grid->tree().getValueDepth(c)<<']';}out<<"]}";
}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 for(int scene=0;scene<=5;++scene)for(int mode:{-1,1,2,3}){run<FloatGrid>(scene,mode,0,3,3);run<DoubleGrid>(scene,mode,0,3,3);}
 for(float in:{.5f,1.f,3.f,5.f,std::numeric_limits<float>::max()}){run<FloatGrid>(0,1,0,1.5f,in);run<DoubleGrid>(0,1,0,1.5f,in);}
 for(float iso:{-.25f,.5f,.50000006f}){run<FloatGrid>(0,2,iso,2.25f,1.75f);run<DoubleGrid>(0,2,iso,2.25f,1.75f);}
 for(int mode:{1,2,3})for(int cls:{0,1})for(int kernel:{0,1,2}){run<FloatGrid>(0,mode,0,3,3,1,cls,kernel);run<DoubleGrid>(0,mode,0,3,3,1,cls,kernel);}
 for(int scene:{0,3}){run<FloatGrid>(scene,0,0,3,3,1,1,2);run<DoubleGrid>(scene,0,0,3,3,1,1,2);}
 out<<"]}\n";return out.good()?0:3;}
