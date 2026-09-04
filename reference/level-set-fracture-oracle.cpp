// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/LevelSetFracture.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <type_traits>
using namespace openvdb;
std::ofstream out;bool first=true;
void num(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class V>void vec(const V&v){out<<'[';num(v[0]);out<<',';num(v[1]);out<<',';num(v[2]);out<<']';}
template<class G>void result(const G&grid){using T=typename G::ValueType;out<<"{\"background\":";num(grid.background());out<<",\"class\":"<<int(grid.getGridClass())<<",\"name\":\""<<grid.getName()<<"\",\"active\":"<<grid.activeVoxelCount()<<",\"tiles\":"<<grid.tree().activeTileCount()<<",\"roots\":"<<grid.tree().root().getTableSize()<<",\"voxelSize\":";vec(grid.voxelSize());out<<",\"worldZero\":";vec(grid.transform().indexToWorld(Vec3d(0)));out<<",\"leaves\":[";
 bool firstLeaf=true;for(auto leaf=grid.tree().cbeginLeaf();leaf;++leaf){if(!firstLeaf)out<<',';firstLeaf=false;out<<"{\"origin\":";vec(leaf->origin());out<<",\"runs\":[";int i=0;while(i<512){if(i)out<<',';T v=leaf->getValue(i);bool active=leaf->isValueOn(i);int end=i+1;while(end<512&&leaf->getValue(end)==v&&leaf->isValueOn(end)==active)++end;out<<'['<<(end-i)<<',';num(v);out<<','<<active<<']';i=end;}out<<"]}";}
 out<<"],\"probes\":[";bool firstProbe=true;for(int x:{-4096,-128,-16,-8,0,8,16,128,4096})for(int y:{-8,0,8})for(int z:{-8,0,8}){if(!firstProbe)out<<',';firstProbe=false;Coord c(x,y,z);out<<'['<<x<<','<<y<<','<<z<<',';num(grid.tree().getValue(c));out<<','<<grid.tree().isValueOn(c)<<','<<grid.tree().getValueDepth(c)<<']';}out<<"]}";
}
template<class G>typename G::Ptr sphere(float radius,Vec3f center=Vec3f(0)){return tools::createLevelSetSphere<G>(radius,center,1.f,3.f,false);}
template<class G>typename G::Ptr scene(int mode){using T=typename G::ValueType;auto grid=sphere<G>(mode==2?.8f:mode==3?1.5f:3.f);grid->setName("object");
 if(mode==1){auto b=sphere<G>(2.f,Vec3f(0,5,0));tools::csgUnion(*grid,*b);}
 if(mode==4)grid->tree().clear();
 if(mode==5){grid->tree().clear();grid->tree().addTile(1,Coord(0),T(-1),true);}
 if(mode==6){grid->tree().clear();for(int i=0;i<9;++i)grid->tree().setValueOn(Coord(i*8,0,0),T(.5));}
 if(mode==7){grid->tree().clear();for(int x=0;x<3;++x)for(int y=0;y<3;++y)for(int z=0;z<3;++z)grid->tree().setValueOn(Coord(x,y,z),T(x-1));}
 return grid;}
template<class G>void run(int mode,int profile){using F=tools::LevelSetFracture<G>;auto input=scene<G>(mode);auto cutter=sphere<G>(2.5f,Vec3f(2,.25,0));typename F::GridPtrList grids;grids.push_back(input);F fracture;
 typename F::Vec3sList points;typename F::QuatsList rotations;bool overlap=profile!=7,segment=profile==1;
 if(profile==2||profile==3||profile==4||profile==5){points={Vec3f(-.75,0,0),Vec3f(.75,1.25,0)};if(profile==3)rotations={math::Quats::identity(),math::Quats::identity()};if(profile==4)rotations={math::Quats(math::Z_AXIS,.37f),math::Quats(math::X_AXIS,-.21f)*math::Quats(math::Y_AXIS,.44f)};if(profile==5)rotations={math::Quats(math::Z_AXIS,.37f)};}
 if(profile==8)cutter=input;if(profile==9)grids.clear();
 fracture.fracture(grids,*cutter,segment,points.empty()?nullptr:&points,rotations.empty()?nullptr:&rotations,overlap);
 if(profile==6||profile==7){auto next=sphere<G>(2.25f,Vec3f(0,2,0));fracture.fracture(grids,*next,true,nullptr,nullptr,overlap);}
 if(!first)out<<',';first=false;out<<"{\"type\":\""<<(std::is_same_v<typename G::ValueType,float>?"float":"double")<<"\",\"scene\":"<<mode<<",\"profile\":"<<profile<<",\"residuals\":[";bool f=true;for(auto g:grids){if(!f)out<<',';f=false;result(*g);}out<<"],\"fragments\":[";f=true;for(auto g:fracture.fragments()){if(!f)out<<',';f=false;result(*g);}out<<"]}";
}
void quaternion(int index,math::Quats q){if(!first)out<<',';first=false;out<<"{\"index\":"<<index<<",\"q\":[";for(int i=0;i<4;++i){if(i)out<<',';num(q[i]);}out<<"],\"angles\":";vec(q.eulerAngles(math::XYZ_ROTATION));out<<",\"matrix\":[";auto m=math::rotation<math::Mat4s>(q);for(int i=0;i<16;++i){if(i)out<<',';num(m[i/4][i%4]);}out<<"]}";}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 for(int mode:{0,1,2,3})for(int profile=0;profile<10;++profile){run<FloatGrid>(mode,profile);run<DoubleGrid>(mode,profile);}
 for(int mode:{4,5,6,7})for(int profile:{0,1,8,9}){run<FloatGrid>(mode,profile);run<DoubleGrid>(mode,profile);}
 out<<"],\"quaternions\":[";first=true;int index=0;
 for(int axis=0;axis<3;++axis)for(float angle:{0.f,.3f,-.7f,1.5707963267948966f,-1.5707963267948966f,3.141592653589793f,6.5f}){math::Quats q(math::Axis(axis),angle);quaternion(index++,q);quaternion(index++,q*3.f);}
 for(auto q:{math::Quats(0,0,0,0),math::Quats(1e-5f,0,0,0),math::Quats(.1f,.2f,.3f,.4f),math::Quats(-.2f,.3f,-.1f,.7f),math::Quats(math::X_AXIS,.2f)*math::Quats(math::Y_AXIS,1.5707963267948966f)*math::Quats(math::Z_AXIS,.3f),math::Quats(std::numeric_limits<float>::quiet_NaN(),0,0,1)})quaternion(index++,q);
 quaternion(index++,math::Quats(math::Z_AXIS,.37f));quaternion(index++,math::Quats(math::X_AXIS,-.21f));quaternion(index++,math::Quats(math::Y_AXIS,.44f));quaternion(index++,math::Quats(math::X_AXIS,-.21f)*math::Quats(math::Y_AXIS,.44f));out<<"]}\n";return out.good()?0:3;}