// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/MeshToVolume.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <vector>
#include <cmath>
using namespace openvdb;
std::ofstream out;bool first=true;
void num(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class V>void vec(const V&v){out<<'[';num(v[0]);out<<',';num(v[1]);out<<',';num(v[2]);out<<']';}
struct Mesh{std::vector<Vec3d>points;std::vector<Vec4I>faces;};
void cube(Mesh&m,Vec3d min,Vec3d max){unsigned n=unsigned(m.points.size());for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)m.points.emplace_back(x?max.x():min.x(),y?max.y():min.y(),z?max.z():min.z());for(Vec4I f:std::vector<Vec4I>{{0,1,3,2},{4,6,7,5},{0,4,5,1},{2,3,7,6},{0,2,6,4},{1,5,7,3}}){f+=Vec4I(n);m.faces.push_back(f);}}
Mesh mesh(int scene){Mesh m;
    if(scene==0||scene==1){cube(m,Vec3d(-2.25,-1.75,-2.5),Vec3d(2.75,2.25,1.5));if(scene==1){auto faces=m.faces;m.faces.clear();for(auto q:faces){m.faces.emplace_back(q[0],q[1],q[2],util::INVALID_IDX);m.faces.emplace_back(q[0],q[3],q[2],util::INVALID_IDX);}}}
    else if(scene==2){m.points={{-2.25,-1.5,-1.25},{3.25,-1.5,-1.25},{0.25,3.5,-1.25},{0.25,0.5,3.25}};m.faces={{0,1,2,util::INVALID_IDX},{0,3,1,util::INVALID_IDX},{1,3,2,util::INVALID_IDX},{0,2,3,util::INVALID_IDX}};}
    else if(scene==3){m.points={{-2.25,-1.75,0.125},{3.75,-1.25,0.375},{-0.75,3.25,1.125}};m.faces={{0,1,2,util::INVALID_IDX}};}
    else if(scene==4){m.points={{0.25,0.375,0.125},{0.25,0.375,0.125},{0.25,0.375,0.125},{2.25,0.375,0.125}};m.faces={{0,1,2,util::INVALID_IDX},{0,1,3,util::INVALID_IDX}};}
    else if(scene==5){cube(m,Vec3d(-3.25),Vec3d(1.75));cube(m,Vec3d(-0.75),Vec3d(3.75));}
    else if(scene==6){m.points={{-18.25,-2.25,0.125},{18.75,-1.75,0.375},{-0.75,3.25,1.125}};m.faces={{0,1,2,util::INVALID_IDX}};}
    else if(scene==7){cube(m,Vec3d(-2.25),Vec3d(2.75));for(auto&f:m.faces)std::swap(f[0],f[2]);}
    if(scene==9)cube(m,Vec3d(-2),Vec3d(2));
    return m;
}
template<class Tree>void leaves(const Tree&t){std::vector<const typename Tree::LeafNodeType*>nodes;t.getNodes(nodes);out<<'[';bool f=true;for(auto*leaf:nodes){if(!f)out<<',';f=false;out<<"{\"origin\":";vec(leaf->origin());out<<",\"active\":\"";for(int i=0;i<512;i++)out<<(leaf->isValueOn(i)?'1':'0');out<<"\",\"values\":[";for(int i=0;i<512;i++){if(i)out<<',';num(double(leaf->getValue(i)));}out<<"]}";}out<<']';}
template<class Grid>void run(int scene,int flags,float ex,float in,int interiorMode,int transformMode){
    using T=typename Grid::ValueType;Mesh m=mesh(scene);tools::QuadAndTriangleDataAdapter<Vec3d,Vec4I>adapter(m.points,m.faces);
    auto transform=math::Transform::createLinearTransform(transformMode?0.75:1.0);if(transformMode){transform->postRotate(0.21,math::Z_AXIS);transform->postTranslate(Vec3d(2,-1,0.5));}
    Int32Grid indices(-1);typename Grid::Ptr grid;util::NullInterrupter interrupter;auto test=[](const Coord&c){return double(c.x())*c.x()+double(c.y())*c.y()+double(c.z())*c.z()<9.0;};
    if(interiorMode==0)grid=tools::meshToVolume<Grid>(adapter,*transform,ex,in,flags,&indices);
    else grid=tools::meshToVolume<Grid>(interrupter,adapter,*transform,ex,in,flags,&indices,test,interiorMode==1?tools::EVAL_EVERY_VOXEL:tools::EVAL_EVERY_TILE);
    if(!first)out<<',';first=false;out<<"{\"type\":\""<<(std::is_same_v<T,float>?"float":"double")<<"\",\"scene\":"<<scene<<",\"flags\":"<<flags<<",\"exterior\":";num(ex);out<<",\"interior\":";num(in);out<<",\"interiorMode\":"<<interiorMode<<",\"transformMode\":"<<transformMode<<",\"points\":[";for(size_t i=0;i<m.points.size();i++){if(i)out<<',';vec(m.points[i]);}out<<"],\"faces\":[";for(size_t i=0;i<m.faces.size();i++){if(i)out<<',';auto f=m.faces[i];out<<'['<<f[0]<<','<<f[1]<<','<<f[2]<<','<<int32_t(f[3])<<']';}out<<"],\"background\":";num(grid->background());out<<",\"class\":"<<int(grid->getGridClass())<<",\"active\":"<<grid->activeVoxelCount()<<",\"indexActive\":"<<indices.activeVoxelCount()<<",\"leaves\":";leaves(grid->tree());out<<",\"indexLeaves\":";leaves(indices.tree());
    out<<",\"probes\":[";bool pf=true;for(int x:{-128,-16,-8,0,8,16,128})for(int y:{-8,0,8})for(int z:{-8,0,8}){Coord c(x,y,z);if(!pf)out<<',';pf=false;out<<'['<<x<<','<<y<<','<<z<<',';num(grid->tree().getValue(c));out<<','<<grid->tree().isValueOn(c)<<','<<grid->tree().getValueDepth(c)<<']';}out<<"]}";
}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    for(int scene=0;scene<8;scene++)for(int flags:{0,1,2,4,8,15}){run<FloatGrid>(scene,flags,3,3,0,scene%2);run<DoubleGrid>(scene,flags,3,3,0,scene%2);}
    for(int scene:{0,2,5})for(float width:{0.5f,1.f,2.f,5.f}){run<FloatGrid>(scene,0,width,width+1,0,0);run<DoubleGrid>(scene,0,width,width+1,0,0);}
    for(int mode:{1,2})for(int scene:{0,2,5}){run<FloatGrid>(scene,0,3,4,mode,1);run<DoubleGrid>(scene,0,3,4,mode,1);}
    run<FloatGrid>(0,0,3,std::numeric_limits<float>::max(),0,0);run<DoubleGrid>(0,0,3,std::numeric_limits<float>::max(),0,0);
    run<FloatGrid>(8,0,3,3,0,1);run<DoubleGrid>(8,1,3,3,0,1);
    for(int flags:{0,1,2,4,8,15}){run<FloatGrid>(9,flags,3,3,0,0);run<DoubleGrid>(9,flags,3,3,0,0);}
    out<<"]}\n";return out.good()?0:3;
}
