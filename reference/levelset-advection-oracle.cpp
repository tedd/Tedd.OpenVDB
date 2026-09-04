// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Independent reference for tools/LevelSetAdvect.h, using its unmodified solver and tracker.
#include <openvdb/openvdb.h>
#include <openvdb/tools/LevelSetAdvect.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <openvdb/io/io.h>
#include <tbb/global_control.h>
#include <iostream>
#include <iomanip>
#include <sstream>
#include <limits>
#include <vector>
using namespace openvdb;
void advNumber(double v){if(std::isnan(v))std::cout<<"\"NaN\"";else if(std::isinf(v))std::cout<<(v<0?"\"-Infinity\"":"\"Infinity\"");else std::cout<<v;}
math::Transform::Ptr readMap(const math::MapBase& map){std::stringstream stream;writeString(stream,map.type());map.write(stream);io::setCurrentVersion(stream);auto result=math::Transform::createLinearTransform();result->read(stream);return result;}
template<class Grid>typename Grid::Ptr advShape(int map,bool empty){float dx=map==2?1.0f:0.75f;auto result=empty?Grid::create(typename Grid::ValueType(3*dx)):tools::createLevelSetSphere<Grid>(3.25f,Vec3f(-0.25f,0.5f,0.25f),dx,3.0f);result->setGridClass(GRID_LEVEL_SET);if(empty)result->setTransform(math::Transform::createLinearTransform(dx));if(map==1)result->transform().postTranslate(Vec3d(0.25,0.5,-0.375));if(map==2){math::UnitaryMap unitary(math::Y_AXIS,0.21);result->setTransform(readMap(unitary));}return result;}
template<class S>struct AdvField{
    using ValueType=S;using VectorType=math::Vec3<S>;using Tree=typename tree::Tree4<VectorType,5,4,3>::Type;using Grid= openvdb::Grid<Tree>;
    typename Grid::Ptr grid;int kind;tools::DiscreteField<Grid> discrete;tools::EnrightField<S> enright;
    static typename Grid::Ptr make(int kind,const math::Transform& transform){VectorType background=kind==3?VectorType(0):kind==4?VectorType(S(1e-4)):kind==5?VectorType(std::numeric_limits<S>::quiet_NaN()):kind==6?VectorType(std::numeric_limits<S>::infinity()):VectorType(S(0.625),S(-0.375),S(0.25));auto result=Grid::create(background);result->setTransform(transform.copy());if(kind==1){auto t=math::Transform::createLinearTransform(0.625);t->postTranslate(Vec3d(-0.5,0.125,0.25));result->setTransform(readMap(*t->baseMap()->getAffineMap()));for(int x=-12;x<=12;++x)for(int y=-12;y<=12;++y)for(int z=-12;z<=12;++z)result->tree().setValue(Coord(x,y,z),VectorType(S(0.015625*x+0.0078125*y+0.125),S(-0.0234375*y+0.015625*z-0.0625),S(0.0078125*x*z+0.03125)));}return result;}
    AdvField(int k,const math::Transform& transform):grid(make(k,transform)),kind(k),discrete(*grid){}
    math::Transform transform()const{return kind==2?enright.transform():grid->transform();}
    VectorType operator()(const Coord& c,S time)const{return kind==2?enright(c,time):discrete(c,time);}
    VectorType operator()(const Vec3d& p,S time)const{return kind==2?enright(p,time):discrete(p,time);}
};
std::vector<Coord> advProbes(){std::vector<Coord> result;for(int i=0;i<256;++i)result.emplace_back(i*17%25-12,i*31%25-12,i*47%25-12);for(int i=-8;i<=8;++i){result.emplace_back(i,0,0);result.emplace_back(0,i,0);result.emplace_back(0,0,i);}return result;}
const math::BiasedGradientScheme advSpatial[]={math::FIRST_BIAS,math::SECOND_BIAS,math::THIRD_BIAS,math::WENO5_BIAS,math::HJWENO5_BIAS};
const math::TemporalIntegrationScheme advTemporal[]={math::TVD_RK1,math::TVD_RK2,math::TVD_RK3};
bool advFirst=true;
template<class Grid,class S>void advCase(int kind,int scheme,int rk,int map=0,int duration=0,bool empty=false){using T=typename Grid::ValueType;auto grid=advShape<Grid>(map,empty);AdvField<S> field(kind,grid->transform());tools::LevelSetAdvection<Grid,AdvField<S>> solver(*grid,field);solver.setGrainSize(0);solver.setSpatialScheme(advSpatial[scheme]);solver.setTemporalScheme(advTemporal[rk]);solver.setTrackerSpatialScheme(math::HJWENO5_BIAS);solver.setTrackerTemporalScheme(math::TVD_RK1);solver.setNormCount(1);T time0=T(0.125),time1=T(0.18);if(duration==1)time1=T(0.73);if(duration==2){time0=T(0.18);time1=T(-0.11);}auto steps=solver.advect(time0,time1);if(!advFirst)std::cout<<",\n";advFirst=false;
    std::cout<<"{\"precision\":\""<<(sizeof(T)==4?"float":"double")<<"\",\"fieldPrecision\":\""<<(sizeof(S)==4?"float":"double")<<"\",\"kind\":"<<kind<<",\"scheme\":"<<scheme<<",\"rk\":"<<rk<<",\"map\":"<<map<<",\"duration\":"<<duration<<",\"empty\":"<<(empty?"true":"false")<<",\"time0\":";advNumber(time0);std::cout<<",\"time1\":";advNumber(time1);std::cout<<",\"steps\":"<<steps<<",\"activeCount\":"<<grid->activeVoxelCount()<<",\"leafCount\":"<<grid->tree().leafCount()<<",\"rootCount\":"<<grid->tree().root().getTableSize()<<",\"background\":";advNumber(grid->background());
    std::cout<<",\"activeValues\":[";bool first=true;for(auto it=grid->tree().cbeginValueOn();it;++it){if(!first)std::cout<<',';first=false;Coord c=it.getCoord();std::cout<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<',';advNumber(*it);std::cout<<']';}
    std::cout<<"],\"probes\":[";first=true;for(const Coord& c:advProbes()){if(!first)std::cout<<',';first=false;std::cout<<'[';advNumber(grid->tree().getValue(c));std::cout<<','<<(grid->tree().isValueOn(c)?1:0)<<','<<grid->tree().getValueDepth(c)<<']';}std::cout<<"]}";
}
template<class Grid>void advectionCases(){for(int kind=0;kind<3;++kind)for(int scheme=0;scheme<5;++scheme)for(int rk=0;rk<3;++rk)advCase<Grid,float>(kind,scheme,rk);for(int scheme=0;scheme<5;++scheme)for(int rk=0;rk<3;++rk)advCase<Grid,double>(1,scheme,rk);for(int map=1;map<3;++map)for(int kind=0;kind<2;++kind)advCase<Grid,float>(kind,4,1,map);for(int rk=0;rk<3;++rk){advCase<Grid,float>(0,4,rk,0,1);advCase<Grid,float>(1,4,rk,0,2);}for(int kind=3;kind<7;++kind)advCase<Grid,float>(kind,4,1);advCase<Grid,float>(0,4,1,0,0,true);}
int main(){openvdb::initialize();tbb::global_control threads(tbb::global_control::max_allowed_parallelism,1);std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"coordinates\":[";bool first=true;for(auto c:advProbes()){if(!first)std::cout<<',';first=false;std::cout<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<']';}std::cout<<"],\"cases\":[\n";advectionCases<FloatGrid>();advectionCases<DoubleGrid>();std::cout<<"]}\n";return 0;}
