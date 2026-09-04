// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/VolumeToMesh.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <tbb/global_control.h>
#include <iostream>
#include <iomanip>
#include <type_traits>
using namespace openvdb;
using namespace openvdb::tools;
const Coord corners[8] = {Coord(0,0,0),Coord(1,0,0),Coord(1,0,1),Coord(0,0,1),Coord(0,1,0),Coord(1,1,0),Coord(1,1,1),Coord(0,1,1)};
bool first=true;
template<class G> typename G::Ptr shape(int id,int config) {
    using T=typename G::ValueType; auto grid=G::create(T(3)); if constexpr(std::is_same<T,bool>::value) grid=G::create(false);
    grid->setGridClass(GRID_LEVEL_SET);
    if(id==0) {
        for(int n=0;n<8;++n) { T value=T((config&(1<<n))?-1:1); if constexpr(std::is_same<T,bool>::value) value=(config&(1<<n))!=0; grid->tree().setValueOn(corners[n],value); }
    } else if(id==1 || id==2 || id==5 || id>=7) {
        if constexpr(std::is_floating_point<T>::value) grid=createLevelSetSphere<G>(3.25f,Vec3f(-.25f,.5f,.125f),.75f,3.f,false);
        if(id==2)grid->setGridClass(GRID_FOG_VOLUME);
        if(id==5)grid->setTransform(math::Transform::createFrustumTransform(BBoxd(Vec3d(-8,-8,-8),Vec3d(8,8,8)),.75,12,1));
    } else if(id==3) {
        grid->tree().addTile(1,Coord(0),T(-1),true); grid->tree().setValueOn(Coord(8,0,0),T(-.5));
    } else if(id==4) {
        for(int x=-2;x<=8;++x) for(int y=-2;y<=8;++y) for(int z=-2;z<=8;++z) { Coord p(x,y,z); T v=T(((x*x+y*3-z*2)%7-3)*.25); if((x+y+z)%5!=0)grid->tree().setValueOn(p,v);else grid->tree().setValueOff(p,v); }
    } else if(id==6) {
        grid->tree().setValueOff(Coord(0),T(-1)); grid->tree().setValueOff(Coord(1,0,0),T(1));
    }
    if(id>=7) for(auto leaf=grid->tree().beginLeaf();leaf;++leaf) for(auto it=leaf->beginValueAll();it;++it) { Coord p=it.getCoord(); double plane=id==7?p.x()*.75-.375:id==8?p.x()*.75+p.y()*.375-p.z()*.25-.125:-p.x()*.75+.375; T v=std::max(it.getValue(),T(plane)); v=std::min(v,grid->background()); leaf->setValueOnly(it.pos(),v); leaf->setActiveState(it.pos(),std::abs(double(v))<double(grid->background())); }
    return grid;
}
BoolGrid::Ptr mask(int mode) { auto m=BoolGrid::create(false); m->tree().fill(CoordBBox(Coord(0,-12,-12),Coord(12,12,12)),false,true); if(mode==3)m->setTransform(math::Transform::createLinearTransform(1.5));else m->setTransform(math::Transform::createLinearTransform(.75)); return m; }
template<class G> void run(const char*type,int sh,int config=0,double adapt=0,int maskMode=0,double iso=0,int referenceMode=0,double secondary=0,int spatial=0,int adaptMask=0,bool relax=true) {
    auto grid=shape<G>(sh,config); VolumeToMesh mesher(iso,adapt,relax); if(maskMode)mesher.setSurfaceMask(mask(maskMode),maskMode==2);
    if(referenceMode)mesher.setRefGrid(shape<G>(1,0),secondary);
    if(spatial) { auto g=FloatGrid::create(.5f);g->setTransform(math::Transform::createLinearTransform(.75));for(int x=-10;x<=10;++x)for(int y=-10;y<=10;++y)for(int z=-10;z<=10;++z)g->tree().setValueOn(Coord(x,y,z),float(x+4)*.125f);mesher.setSpatialAdaptivity(g); }
    if(adaptMask){auto g=BoolGrid::create(false);g->tree().fill(CoordBBox(Coord(-1),Coord(1)),false,true);mesher.setAdaptivityMask(g->treePtr());}
    if(referenceMode==2){auto previous=shape<G>(9,0);mesher(*previous);} mesher(*grid);
    if(!first)std::cout<<',';first=false;
    std::cout<<"{\"type\":\""<<type<<"\",\"shape\":"<<sh<<",\"config\":"<<config<<",\"adaptivity\":"<<adapt<<",\"mask\":"<<maskMode<<",\"iso\":"<<iso<<",\"reference\":"<<referenceMode<<",\"secondary\":"<<secondary<<",\"spatial\":"<<spatial<<",\"adaptivityMask\":"<<adaptMask<<",\"relax\":"<<relax<<",\"points\":[";
    for(size_t n=0;n<mesher.pointListSize();++n){if(n)std::cout<<',';auto p=mesher.pointList()[n];std::cout<<'['<<p.x()<<','<<p.y()<<','<<p.z()<<']';}
    std::cout<<"],\"pools\":[";
    for(size_t n=0;n<mesher.polygonPoolListSize();++n) {
        if(n)std::cout<<',';
        auto& p=mesher.polygonPoolList()[n];
        std::cout<<"{\"quads\":[";
        for(size_t i=0;i<p.numQuads();++i) { if(i)std::cout<<','; auto q=p.quad(i); std::cout<<'['<<q[0]<<','<<q[1]<<','<<q[2]<<','<<q[3]<<','<<int(p.quadFlags(i))<<']'; }
        std::cout<<"],\"triangles\":[";
        for(size_t i=0;i<p.numTriangles();++i) { if(i)std::cout<<','; auto q=p.triangle(i); std::cout<<'['<<q[0]<<','<<q[1]<<','<<q[2]<<','<<int(p.triangleFlags(i))<<']'; }
        std::cout<<"]}";
    }
    std::cout<<"],\"pointFlags\":[";for(size_t i=0;i<mesher.pointFlags().size();++i){if(i)std::cout<<',';std::cout<<int(mesher.pointFlags()[i]);}std::cout<<"]}";
}
int main(){initialize();tbb::global_control workers(tbb::global_control::max_allowed_parallelism,1);std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
for(int n=0;n<256;++n)run<FloatGrid>("float",0,n);
for(int n=0;n<256;++n)run<BoolGrid>("bool",0,n,0,0,.5);
for(int sh=1;sh<=6;++sh){run<FloatGrid>("float",sh);run<DoubleGrid>("double",sh);}
for(int mode=1;mode<=3;++mode){run<FloatGrid>("float",1,0,0,mode);run<DoubleGrid>("double",1,0,0,mode);}
for(double adapt:{.05,.25,.75,1.}){run<FloatGrid>("float",1,0,adapt);run<DoubleGrid>("double",1,0,adapt);run<FloatGrid>("float",3,0,adapt);}
for(int sh:{1,7,8})for(double adapt:{0.,.25,1.})for(int ref:{1,2}){run<FloatGrid>("float",sh,0,adapt,0,0,ref,.5);run<DoubleGrid>("double",sh,0,adapt,0,0,ref,.5);}
for(int mode:{0,1})for(double adapt:{.25,1.}){run<FloatGrid>("float",1,0,adapt,0,0,0,0,mode,1);run<DoubleGrid>("double",4,0,adapt,0,0,0,0,mode,1,false);}
std::cout<<"]}";}
