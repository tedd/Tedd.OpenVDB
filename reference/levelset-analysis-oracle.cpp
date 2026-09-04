// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/LevelSetUtil.h>
#include <openvdb/tools/LevelSetMeasure.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <openvdb/tools/Composite.h>
#include <tbb/global_control.h>
#include <iostream>
#include <iomanip>
#include <cmath>
#include <limits>
#include <vector>
using namespace openvdb;
void number(double value) { if(std::isnan(value))std::cout<<"\"NaN\"";else if(std::isinf(value))std::cout<<(value>0?"\"Infinity\"":"\"-Infinity\"");else std::cout<<value; }
FloatGrid::Ptr shape(int id) {
 auto grid=FloatGrid::create(3.0f); auto& t=grid->tree();
 if(id==1) for(int x=-2;x<=9;++x)for(int y=-1;y<=2;++y)for(int z=-1;z<=2;++z) { Coord c(x,y,z);t.setValueOff(c,x*0.5f);if((x+y+z)%3!=0)t.setValueOn(c); }
 if(id==2) {t.setValueOff(Coord(0),-2);t.setValueOn(Coord(1,0,0),2);t.setValueOff(Coord(4096,0,0),-1);}
 if(id==3) {t.addTile(3,Coord(0),-3,false);t.addTile(3,Coord(8192,0,0),3,false);}
 if(id==4) {t.addTile(1,Coord(0),-3,true);t.setValueOn(Coord(8,0,0),2);t.addTile(1,Coord(24,0,0),1,true);}
 if(id==5) {t.addTile(2,Coord(128,0,0),-3,false);t.setValueOn(Coord(0),-1);}
 if(id==6) {for(Coord c: {Coord(-8,0,0),Coord(-7,0,0),Coord(-1,0,0),Coord(0),Coord(7,7,7),Coord(8,7,7),Coord(9,8,7),Coord(24,0,0)})t.setValueOn(c,float(c.x()+1));t.setValueOff(Coord(40),-2);}
 if(id==7) {t.setValueOn(Coord(0),1);t.setValueOn(Coord(1,0,0),2);}
 if(id==8) grid=tools::createLevelSetSphere<FloatGrid>(7.5f,Vec3f(-1.25f,2,3.5f),0.75f,3.0f);
 if(id==9 || id==10) {grid=tools::createLevelSetSphere<FloatGrid>(4.0f,Vec3f(-7,0,0),1.0f,3.0f);auto rhs=tools::createLevelSetSphere<FloatGrid>(4.0f,Vec3f(8,0,0),1.0f,3.0f);tools::csgUnion(*grid,*rhs);if(id==10)for(auto it=grid->tree().beginValueAll();it;++it)if(*it < -1.5f){it.setValue(-1.5f);it.setActiveState(false);}}
 if(id==11) {t.setValueOn(Coord(0),std::numeric_limits<float>::quiet_NaN());t.setValueOn(Coord(1,0,0),-1);t.setValueOff(Coord(2,0,0),std::numeric_limits<float>::quiet_NaN());}
 if(id==12)t.addTile(1,Coord(0),-3,true);
 if(id==13){t.addTile(3,Coord(0),-3,false);t.setValueOn(Coord(4096,0,0),-1);}
 grid->setName("analysis");grid->insertMeta("custom",StringMetadata("kept"));grid->setGridClass(GRID_LEVEL_SET);return grid;
}
std::vector<Coord> probes(){std::vector<Coord> out;for(int x=-16;x<=32;++x)for(int y=-2;y<=2;++y)for(int z=-2;z<=2;++z)out.emplace_back(x,y,z);for(Coord c:{Coord(7,7,7),Coord(8,7,7),Coord(9,8,7),Coord(40),Coord(128,0,0),Coord(255,127,127),Coord(4095),Coord(4096,0,0),Coord(8192,0,0)})out.push_back(c);return out;}
template<class Grid> void dump(const Grid& grid,const std::vector<Coord>& coords){auto& tree=grid.tree();std::cout<<"{\"background\":";number(double(tree.background()));std::cout<<",\"active\":"<<tree.activeVoxelCount()<<",\"leaves\":"<<tree.leafCount()<<",\"roots\":"<<tree.root().getTableSize()<<",\"tiles\":"<<tree.activeTileCount()<<",\"name\":\""<<grid.getName()<<"\",\"class\":"<<int(grid.getGridClass())<<",\"custom\":"<<(grid["custom"]?"true":"false")<<",\"samples\":[";bool first=true;for(const auto& c:coords){if(!first)std::cout<<',';first=false;std::cout<<'[';number(double(tree.getValue(c)));std::cout<<','<<(tree.isValueOn(c)?1:0)<<','<<tree.getValueDepth(c)<<']';}std::cout<<"]}";}
template<class Grid> void measurements(int id){using T=typename Grid::ValueType; double dx=id%3==0?0.5:id%3==1?1.0:2.0;auto grid=tools::createLevelSetSphere<Grid>(7.25f,Vec3f(-1.25f,2.5f,0.75f),float(dx),3.0f);tools::LevelSetMeasure<Grid> m(*grid);std::cout<<"{\"type\":\""<<(sizeof(T)==4?"float":"double")<<"\",\"voxelSize\":"<<dx<<",\"values\":[";number(m.area());std::cout<<',';number(m.volume());std::cout<<',';number(m.totMeanCurvature());std::cout<<',';number(m.totGaussianCurvature());std::cout<<',';number(m.avgMeanCurvature());std::cout<<',';number(m.avgGaussianCurvature());std::cout<<',';number(m.area(false));std::cout<<',';number(m.volume(false));std::cout<<',';number(m.totMeanCurvature(false));std::cout<<',';number(m.avgMeanCurvature(false));std::cout<<',';number(m.avgGaussianCurvature(false));std::cout<<','<<m.eulerCharacteristic()<<','<<m.genus()<<"]}";}
int main(){tbb::global_control threads(tbb::global_control::max_allowed_parallelism,1);std::cout<<std::setprecision(17);auto coords=probes();std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"coordinates\":[";for(size_t i=0;i<coords.size();++i){if(i)std::cout<<',';std::cout<<'['<<coords[i].x()<<','<<coords[i].y()<<','<<coords[i].z()<<']';}std::cout<<"],\"cases\":[";bool first=true;for(int id=0;id<14;++id)for(int operation=0;operation<11;++operation){if(!first)std::cout<<',';first=false;auto grid=shape(id);std::cout<<"{\"shape\":"<<id<<",\"operation\":"<<operation<<",\"outputs\":[";if(operation<3){auto mask=tools::sdfInteriorMask(*grid,operation==0?0.0f:operation==1?1.0f:99.0f);dump(*mask,coords);}else if(operation<5){auto mask=tools::extractIsosurfaceMask(*grid,operation==3?0.0f:0.5f);dump(*mask,coords);}else if(operation<8){if(operation==5)tools::sdfToFogVolume(*grid);else tools::sdfToFogVolume(*grid,operation==6?1.25f:0.0f);dump(*grid,coords);}else if(operation==8){std::vector<BoolGrid::Ptr> segments;tools::extractActiveVoxelSegmentMasks(*grid,segments);for(size_t i=0;i<segments.size();++i){if(i)std::cout<<',';dump(*segments[i],coords);}}else{std::vector<FloatGrid::Ptr> segments;if(operation==9)tools::segmentActiveVoxels(*grid,segments);else tools::segmentSDF(*grid,segments);for(size_t i=0;i<segments.size();++i){if(i)std::cout<<',';dump(*segments[i],coords);}}std::cout<<"]}";}std::cout<<"],\"measurements\":[";for(int i=0;i<6;++i){if(i)std::cout<<',';if(i<3)measurements<FloatGrid>(i);else measurements<DoubleGrid>(i);}std::cout<<"]}";}
