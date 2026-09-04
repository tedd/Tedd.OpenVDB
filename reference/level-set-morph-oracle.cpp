// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/LevelSetMorph.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <type_traits>
#include <sstream>
#include <openvdb/io/io.h>
using namespace openvdb;
std::ofstream out;bool first=true;
void num(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class V>void vec(const V&v){out<<'[';num(v[0]);out<<',';num(v[1]);out<<',';num(v[2]);out<<']';}
template<class G>void result(const G&grid){using T=typename G::ValueType;out<<",\"background\":";num(grid.background());out<<",\"class\":"<<int(grid.getGridClass())<<",\"name\":\""<<grid.getName()<<"\",\"active\":"<<grid.activeVoxelCount()<<",\"tiles\":"<<grid.tree().activeTileCount()<<",\"roots\":"<<grid.tree().root().getTableSize()<<",\"voxelSize\":";vec(grid.voxelSize());out<<",\"worldZero\":";vec(grid.transform().indexToWorld(Vec3d(0)));out<<",\"leaves\":[";
 bool firstLeaf=true;for(auto leaf=grid.tree().cbeginLeaf();leaf;++leaf){if(!firstLeaf)out<<',';firstLeaf=false;out<<"{\"origin\":";vec(leaf->origin());out<<",\"runs\":[";int i=0;while(i<512){if(i)out<<',';T v=leaf->getValue(i);bool active=leaf->isValueOn(i);int end=i+1;while(end<512&&leaf->getValue(end)==v&&leaf->isValueOn(end)==active)++end;out<<'['<<(end-i)<<',';num(v);out<<','<<active<<']';i=end;}out<<"]}";}
 out<<"],\"probes\":[";bool firstProbe=true;for(int x:{-4096,-128,-16,-8,0,8,16,128,4096})for(int y:{-8,0,8})for(int z:{-8,0,8}){if(!firstProbe)out<<',';firstProbe=false;Coord c(x,y,z);out<<'['<<x<<','<<y<<','<<z<<',';num(grid.tree().getValue(c));out<<','<<grid.tree().isValueOn(c)<<','<<grid.tree().getValueDepth(c)<<']';}out<<"]}";
}
math::Transform::Ptr exactMap(math::MapBase::Ptr map){std::stringstream bytes;writeString(bytes,map->type());map->write(bytes);io::setCurrentVersion(bytes);auto t=math::Transform::Ptr(new math::Transform);t->read(bytes);if(t->mapType()!=map->type())throw std::logic_error("Map reader lost exact type");return t;}
template<class G> typename G::Ptr source(int map){float dx=map>=2?1.f:.75f;auto grid=tools::createLevelSetSphere<G>(2.5f,Vec3f(.25,.5,-.25),dx,3,false);grid->setName("source");
 if(map==1)grid->transform().postTranslate(Vec3d(.375,-.25,.125));
 if(map==2)grid->transform().postRotate(.25,math::Z_AXIS);
 if(map==3)grid->setTransform(math::Transform::Ptr(new math::Transform(math::MapBase::Ptr(new math::TranslationMap(Vec3d(.25,-.5,.125))))));
 if(map==4)grid->setTransform(math::Transform::Ptr(new math::Transform()));
 if(map==5){grid->setTransform(math::Transform::createLinearTransform(.75));grid->transform().postRotate(.2,math::Z_AXIS);}
 if(map==6){grid->transform().postRotate(.2,math::Z_AXIS);grid->transform().postTranslate(Vec3d(.25,.5,.125));}
 if(map==7){grid->tree().clear();grid->tree().addTile(1,Coord(64),typename G::ValueType(-1),true);}
 if(map==8)grid->tree().addTile(1,Coord(64),typename G::ValueType(-1),true);
 if(map==9){grid->tree().clear();grid->tree().setValueOff(Coord(0),typename G::ValueType(-1));}
 if(map==10)grid->tree().clear();
 if(map==11)grid->setTransform(exactMap(math::MapBase::Ptr(new math::UnitaryMap(math::Z_AXIS,.25))));
 if(map==12)grid->setTransform(exactMap(math::MapBase::Ptr(new math::TranslationMap(Vec3d(.25,-.5,.125)))));
 return grid;}
template<class G>typename G::Ptr target(const G&src,int shape,int map){using T=typename G::ValueType;auto grid=tools::createLevelSetSphere<G>(shape==2?1.75f:3.25f,Vec3f(.25,.5,-.25),float(src.voxelSize().x()),3,false);
 if(shape==1||shape==4)grid=src.deepCopy();
 if(shape==3)grid=G::create(T(.2));
 if(shape==5)grid=G::create(std::numeric_limits<T>::quiet_NaN());
 if(shape==6)grid=G::create(std::numeric_limits<T>::infinity());
 grid->setTransform(src.transform().copy());
 if(map==1)grid->transform().postTranslate(Vec3d(.375,-.25,.125));
 if(map==2)grid->setTransform(math::Transform::createFrustumTransform(BBoxd(Vec3d(-4),Vec3d(4)),.75,8,1));
 if(shape==4)for(auto it=grid->tree().beginValueAll();it;++it)it.setValue(it.getValue()+T(5e-7));return grid;}
template<class G>typename G::Ptr mask(const G&src,int mode){using T=typename G::ValueType;auto grid=G::create(mode==5?std::numeric_limits<T>::quiet_NaN():T(.5));grid->setTransform(src.transform().copy());
 if(mode==4)grid->setTransform(math::Transform::createLinearTransform(.5));
 if(mode==2||mode==3||mode==4)for(int x=-8;x<=8;++x)for(int y=-8;y<=8;++y)for(int z=-8;z<=8;++z){Coord c(x,y,z);T value=T((x+5)*.1);if((x+y+z)%2==0)grid->tree().setValueOn(c,value);else grid->tree().setValueOff(c,value);}return grid;}
template<class G>void run(int spatial,int temporal,int targetShape=0,int sourceMap=0,int targetMap=0,int maskMode=0,int trackerMode=0,double begin=0,double end=.5){using T=typename G::ValueType;auto grid=source<G>(sourceMap);auto goal=target<G>(*grid,targetShape,targetMap);if(targetShape==7)goal=grid;
 auto alpha=mask<G>(*grid,maskMode);if(maskMode==6)alpha=grid;
 tools::LevelSetMorphing<G> morph(*grid,*goal);morph.setSpatialScheme(math::BiasedGradientScheme(spatial));morph.setTemporalScheme(math::TemporalIntegrationScheme(temporal));morph.setGrainSize(0);
 if(maskMode){morph.setAlphaMask(*alpha);if(maskMode>=2&&maskMode<=4)morph.setMaskRange(T(.2),T(.8));if(maskMode==6)morph.setMaskRange(T(-1),T(1));morph.invertMask(maskMode==3);}
 if(trackerMode){morph.setTrackerSpatialScheme(math::FIRST_BIAS);morph.setTrackerTemporalScheme(math::TVD_RK3);morph.setNormCount(2);}
 size_t steps=0;std::string error;try{steps=morph.advect(T(begin),T(end));}catch(const std::exception&e){error=e.what();}
 if(!first)out<<',';first=false;out<<"{\"type\":\""<<(std::is_same_v<T,float>?"float":"double")<<"\",\"spatial\":"<<spatial<<",\"temporal\":"<<temporal<<",\"targetShape\":"<<targetShape<<",\"sourceMap\":"<<sourceMap<<",\"sourceMapType\":\""<<grid->transform().mapType()<<"\",\"targetMap\":"<<targetMap<<",\"mask\":"<<maskMode<<",\"tracker\":"<<trackerMode<<",\"time0\":";num(begin);out<<",\"time1\":";num(end);out<<",\"steps\":"<<steps;
 if(!error.empty()){out<<",\"error\":"<<std::quoted(error)<<'}';return;}result(*grid);}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 for(int spatial:{0,4})for(int temporal:{0,1,2})for(int shape:{0,1,2,3}){run<FloatGrid>(spatial,temporal,shape);run<DoubleGrid>(spatial,temporal,shape);}
 for(int spatial:{0,4})for(int temporal:{0,1,2})for(int maskMode:{1,2,3,4,5,6}){run<FloatGrid>(spatial,temporal,0,0,0,maskMode,1);run<DoubleGrid>(spatial,temporal,0,0,0,maskMode,1);}
 for(int sourceMap:{1,2,3,11,12})for(int targetMap:{0,1,2})for(int maskMode:{0,4}){run<FloatGrid>(4,1,0,sourceMap,targetMap,maskMode);run<DoubleGrid>(4,1,0,sourceMap,targetMap,maskMode);}
 for(int shape:{4,5,6,7}){run<FloatGrid>(4,1,shape);run<DoubleGrid>(4,1,shape);}
 for(int map:{4,5,6,7,8,9,10}){run<FloatGrid>(4,1,0,map);run<DoubleGrid>(4,1,0,map);}
 for(int spatial:{1,2,3}){run<FloatGrid>(spatial,1);run<DoubleGrid>(spatial,1);}
 run<FloatGrid>(4,1,0,0,0,0,0,1,0);run<DoubleGrid>(4,1,0,0,0,0,0,1,0);run<FloatGrid>(4,1,0,0,0,0,0,.25,.25);run<DoubleGrid>(4,1,0,0,0,0,0,.25,.25);
 out<<"]}\n";return out.good()?0:3;}