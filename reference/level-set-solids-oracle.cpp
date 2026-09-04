// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/LevelSetPlatonic.h>
#include <openvdb/tools/TopologyToLevelSet.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <type_traits>
using namespace openvdb;
std::ofstream out;bool first=true;
void num(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class V>void vec(const V&v){out<<'[';num(v[0]);out<<',';num(v[1]);out<<',';num(v[2]);out<<']';}
template<class G>void result(const G&grid){using T=typename G::ValueType;out<<",\"background\":";num(grid.background());out<<",\"class\":"<<int(grid.getGridClass())<<",\"name\":\""<<grid.getName()<<"\",\"active\":"<<grid.activeVoxelCount()<<",\"tiles\":"<<grid.tree().activeTileCount()<<",\"roots\":"<<grid.tree().root().getTableSize()<<",\"voxelSize\":";vec(grid.voxelSize());out<<",\"worldZero\":";vec(grid.transform().indexToWorld(Vec3d(0)));out<<",\"leaves\":[";
 bool firstLeaf=true;for(auto leaf=grid.tree().cbeginLeaf();leaf;++leaf){if(!firstLeaf)out<<',';firstLeaf=false;out<<"{\"origin\":";vec(leaf->origin());out<<",\"runs\":[";int i=0;while(i<512){if(i)out<<',';T v=leaf->getValue(i);bool active=leaf->isValueOn(i);int end=i+1;while(end<512&&leaf->getValue(end)==v&&leaf->isValueOn(end)==active)++end;out<<'['<<(end-i)<<',';num(v);out<<','<<active<<']';i=end;}out<<"]}";}
 out<<"],\"probes\":[";bool firstProbe=true;for(int x:{-4096,-128,-16,-8,0,8,16,128,4096})for(int y:{-8,0,8})for(int z:{-8,0,8}){if(!firstProbe)out<<',';firstProbe=false;Coord c(x,y,z);out<<'['<<x<<','<<y<<','<<z<<',';num(grid.tree().getValue(c));out<<','<<grid.tree().isValueOn(c)<<','<<grid.tree().getValueDepth(c)<<']';}out<<"]}";
}
template<class G>void platonic(int faces,int profile){float scale=1,voxel=.1f,width=3;Vec3f center(0);
 if(profile==1){scale=2;voxel=.5f;width=1;center=Vec3f(-.25,.5,1.25);}
 if(profile==2){scale=-.75f;voxel=.25f;center=Vec3f(.125,-.375,.25);}
 if(profile==3){scale=0;voxel=.5f;center=Vec3f(.125,.25,.375);}
 if(profile==4){scale=1.5f;voxel=.25f;width=0;}
 if(profile==5){scale=3;voxel=.75f;width=5;center=Vec3f(8,-8,.125);}
 if(profile==6){scale=2;voxel=-.5f;width=2;}
 typename G::Ptr grid;std::string error;try{grid=tools::createLevelSetPlatonic<G>(faces,scale,center,voxel,width);}catch(const std::exception&e){error=e.what();}
 if(!first)out<<',';first=false;out<<"{\"operation\":0,\"type\":\""<<(std::is_same_v<typename G::ValueType,float>?"float":"double")<<"\",\"faces\":"<<faces<<",\"profile\":"<<profile<<",\"scale\":";num(scale);out<<",\"center\":";vec(center);out<<",\"voxel\":";num(voxel);out<<",\"width\":";num(width);if(!error.empty()){out<<",\"error\":"<<std::quoted(error)<<'}';return;}result(*grid);
}
template<class G>typename G::Ptr shape(int scene){using T=typename G::ValueType;auto grid=G::create(T(19));auto& t=grid->tree();
 if(scene==0)t.setValueOn(Coord(0),T(0));
 if(scene==1)t.fill(CoordBBox(Coord(-3,-2,-1),Coord(3,2,1)),T(0),true);
 if(scene==2){t.fill(CoordBBox(Coord(-4,-2,-2),Coord(-1,2,2)),T(1),true);t.fill(CoordBBox(Coord(1,-2,-2),Coord(4,2,2)),T(-1),true);}
 if(scene==3){t.addTile(1,Coord(0),T(0),true);t.addTile(1,Coord(-8,0,0),T(-7),false);t.fill(CoordBBox(Coord(8,2,2),Coord(10,4,4)),T(7),true);}
 if(scene==4){t.setValueOff(Coord(0),T(-1));t.addTile(1,Coord(8),T(123),false);}
 if(scene==5){auto sphere=tools::createLevelSetSphere<FloatGrid>(2.5f,Vec3f(.25,.5,-.25),1,1.5f,false);t.topologyUnion(sphere->tree());}
 if(scene==6)t.addTile(2,Coord(0),T(0),true);
 grid->setName("input");return grid;
}
template<class G>void topology(int scene,int profile){int width=3,closing=1,dilation=0,smooth=0;double dx=1.25;
 if(profile==1){width=1;closing=0;}
 if(profile==2){width=2;closing=1;dilation=1;dx=.75;}
 if(profile==3){width=2;closing=2;smooth=1;}
 if(profile==4){width=0;closing=-2;dilation=-1;smooth=-1;dx=.75;}
 if(profile==5){width=1;closing=0;smooth=2;}
 auto input=shape<G>(scene);auto transform=math::Transform::createLinearTransform(dx);transform->postRotate(.21,math::Z_AXIS);transform->postTranslate(Vec3d(.3,-.2,.1));input->setTransform(transform);
 auto grid=tools::topologyToLevelSet(*input,width,closing,dilation,smooth);
 if(!first)out<<',';first=false;out<<"{\"operation\":1,\"type\":\""<<(std::is_same_v<typename G::ValueType,bool>?"bool":"int")<<"\",\"scene\":"<<scene<<",\"profile\":"<<profile<<",\"width\":"<<width<<",\"closing\":"<<closing<<",\"dilation\":"<<dilation<<",\"smoothing\":"<<smooth<<",\"voxel\":";num(dx);result(*grid);
}
int main(int argc,char**argv){if(argc!=2 && argc!=5)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 if(argc==5){int face=std::atoi(argv[2]),profile=std::atoi(argv[3]);if(argv[4][0]=='f')platonic<FloatGrid>(face,profile);else platonic<DoubleGrid>(face,profile);out<<"]}\n";return out.good()?0:3;}
 for(int faces:{4,6,8,12,20})for(int profile=0;profile<=6;++profile){platonic<FloatGrid>(faces,profile);platonic<DoubleGrid>(faces,profile);}
 for(int scene=0;scene<=5;++scene)for(int profile=0;profile<=5;++profile){topology<BoolGrid>(scene,profile);topology<Int32Grid>(scene,profile);}
 topology<BoolGrid>(6,1);topology<Int32Grid>(6,1);out<<"]}\n";return out.good()?0:3;}
