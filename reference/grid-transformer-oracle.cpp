// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Native fixture for pinned OpenVDB tools/GridTransformer.h.
#include <openvdb/openvdb.h>
#include <openvdb/tools/GridTransformer.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <tbb/global_control.h>
#include <iostream>
#include <iomanip>
#include <vector>
#include <type_traits>
using namespace openvdb;
using namespace openvdb::tools;
std::vector<Coord> coordinates;
bool firstCase = true;
template<class T> T value(double v) { return T(v); }
template<> Vec3f value<Vec3f>(double v) { return Vec3f(float(v),float(v*.5-1),float(-v)); }
template<class T> void number(T v) { if(std::isnan(double(v))) std::cout << "\"NaN\""; else if(std::isinf(double(v))) std::cout << (v<0?"\"-Infinity\"":"\"Infinity\""); else std::cout << v; }
void number(const Vec3f& v) { std::cout << '['; number(v.x()); std::cout << ','; number(v.y()); std::cout << ','; number(v.z()); std::cout << ']'; }
template<class G> typename G::Ptr shape(int id) {
    using T=typename G::ValueType;
    auto grid=G::create(value<T>(2.5));
    if(id==0) {
        for(int x=-2;x<=3;++x) for(int y=-1;y<=2;++y) for(int z=0;z<=4;++z) {
            Coord p(x,y,z); T v=value<T>((x*x+2*y-3*z)*.125+.75);
            if((x+2*y+z)%4!=0) grid->tree().setValueOn(p,v); else grid->tree().setValueOff(p,v);
        }
        grid->tree().setValueOff(Coord(-4,0,1),value<T>(-2.5));
        grid->tree().setValueOff(Coord(7,7,7),value<T>(9));
        grid->tree().setValueOn(Coord(8,0,0),value<T>(1.5));
    } else if(id==1) {
        grid->tree().addTile(1,Coord(0,0,0),value<T>(5),true);
        grid->tree().addTile(1,Coord(8,0,0),value<T>(-3),false);
        grid->tree().addTile(1,Coord(0,8,0),value<T>(2.5),true);
        grid->tree().addTile(1,Coord(-8,0,0),value<T>(6),true);
        grid->tree().setValueOn(Coord(1,1,1),value<T>(.125));
    } else if(id==2) {
        grid->tree().setValueOff(Coord(1,2,3),value<T>(-4));
        grid->tree().addTile(1,Coord(8,0,0),value<T>(2.5),false);
    } else if(id==3) {
        if constexpr(std::is_floating_point<T>::value) grid=createLevelSetSphere<G>(2.5f,Vec3f(.25f,.5f,-.25f),1.f,2.f,false);
    }
    if(id==4) grid->tree().addTile(2,Coord(0),value<T>(5),true);
    if(id==5) { grid->tree().addTile(3,Coord(0),value<T>(5),true); grid->tree().addTile(3,Coord(4096,0,0),value<T>(2.5),false); }
    return grid;
}
Mat4d matrix(int id) {
    Mat4d m=Mat4d::identity();
    if(id==1) m.setTranslation(Vec3d(1,-2,3));
    if(id==2) m.setTranslation(Vec3d(.375,-.25,.625));
    if(id==3) m.preScale(Vec3d(.5,.75,1.25));
    if(id==4) m.preScale(Vec3d(.25,.125,.375));
    if(id==5) m.preScale(Vec3d(-.25,.5,1));
    if(id==6) { m.preRotate(math::Z_AXIS,.4); m.preRotate(math::Y_AXIS,-.3); m.preRotate(math::X_AXIS,.2); }
    if(id==7) { m(0,1)=.25; m(1,2)=-.125; m(2,0)=.0625; m.setTranslation(Vec3d(.25,-.5,.75)); }
    if(id==10) m.preRotate(math::Y_AXIS,math::pi<double>()/2);
    if(id==11) { m(2,3)=.002; m.setTranslation(Vec3d(.5,.25,-.25)); }
    if(id==16) m.preScale(Vec3d(1.0/512));
    if(id==17) m.preScale(Vec3d(1.0/16));
    return m;
}
math::Transform::Ptr frustum(double taper=.5) { return math::Transform::createFrustumTransform(BBoxd(Vec3d(-4,-4,0),Vec3d(12,12,16)),taper,12,1); }
struct MatrixMap { Mat4d m,i; explicit MatrixMap(const Mat4d& a):m(a),i(a.inverse()){} bool isAffine()const{return math::isAffine(m);} Vec3d transform(const Vec3d&p)const{return m.transformH(p);} Vec3d invTransform(const Vec3d&p)const{return i.transformH(p);} };
template<class G,class S> void run(const char* type,int kernel,bool staggered,int id,int sh,int op,bool tiles) {
    auto in=shape<G>(sh),out=G::create(value<typename G::ValueType>(7));
    out->tree().setValueOn(Coord(0,0,0),value<typename G::ValueType>(80));
    out->tree().setValueOn(Coord(30,2,-1),value<typename G::ValueType>(81));
    out->tree().setValueOff(Coord(-20,1,0),value<typename G::ValueType>(-7));
    out->setName("retained"); out->setGridClass(GRID_FOG_VOLUME);
    in->setTransform(math::Transform::createLinearTransform(1));
    out->setTransform(math::Transform::createLinearTransform(1));
    Mat4d m=matrix(id); std::unique_ptr<GridTransformer> transformer;
    if(id==8 || id==9) transformer.reset(new GridTransformer(Vec3d(1,-.5,.25),Vec3d(.2,.4,.75),Vec3d(.1,.2,-.15),Vec3d(.375,-.25,.625),id==8?"tsr":"rst",id==8?"zyx":"xyz"));
    else transformer.reset(new GridTransformer(m));
    transformer->setThreaded(false); transformer->setTransformTiles(tiles);
    if(op==0) transformer->transformGrid<S>(*in,*out);
    else if(op==1) { GridResampler resampler; resampler.setThreaded(false); resampler.setTransformTiles(tiles); resampler.transformGrid<S>(MatrixMap(m),*in,*out); }
    else {
        if(id==12 || id==14 || id==15) in->setTransform(frustum()); else in->setTransform(math::Transform::createLinearTransform(m));
        if(id==13 || id==14) out->setTransform(frustum());
        if(id==15) out->setTransform(frustum(.75));
        util::NullInterrupter interrupt;
        if(op==2) doResampleToMatch<S>(*in,*out,interrupt); else resampleToMatch<S>(*in,*out,interrupt);
    }
    if(!firstCase) std::cout << ','; firstCase=false;
    std::cout << "{\"type\":\""<<type<<"\",\"kernel\":"<<kernel<<",\"staggered\":"<<staggered<<",\"profile\":"<<id<<",\"shape\":"<<sh<<",\"operation\":"<<op<<",\"transformTiles\":"<<tiles<<",\"matrix\":[";
    for(int r=0;r<4;++r) for(int c=0;c<4;++c) { if(r||c)std::cout<<','; std::cout<<transformer->getTransform()(r,c); }
    std::cout << "],\"active\":"<<out->activeVoxelCount()<<",\"leaves\":"<<out->tree().leafCount()<<",\"roots\":"<<out->tree().root().getTableSize()<<",\"tiles\":"<<out->tree().activeTileCount()<<",\"background\":"; number(out->background());
    std::cout << ",\"samples\":[";
    bool first=true; for(const Coord& p:coordinates) { if(!first) std::cout<<','; first=false; std::cout<<'['; number(out->tree().getValue(p)); std::cout<<','<<out->tree().isValueOn(p)<<','<<out->tree().getValueDepth(p)<<']'; }
    std::cout << "]}";
}
template<class G,class S> void scalarCases(const char* name,int kernel) {
    for(int p=0;p<12;++p) run<G,S>(name,kernel,false,p,0,0,true);
    for(int p: {0,2,4,7}) run<G,S>(name,kernel,false,p,1,0,true);
    run<G,S>(name,kernel,false,2,1,0,false);
    run<G,S>(name,kernel,false,2,2,0,true);
    for(int p: {0,2,4,11}) run<G,S>(name,kernel,false,p,0,1,true);
    for(int p: {0,2,4,12,13,14,15}) run<G,S>(name,kernel,false,p,0,2,true);
    run<G,S>(name,kernel,false,16,5,1,true);
    run<G,S>(name,kernel,false,17,4,1,true);
    run<G,S>(name,kernel,false,0,3,3,true);
    run<G,S>(name,kernel,false,2,3,2,true);
}
int main() {
    initialize(); tbb::global_control limit(tbb::global_control::max_allowed_parallelism,1); std::cout<<std::setprecision(17);
    for(int x=-4;x<=9;++x) for(int y=-3;y<=5;++y) for(int z=-2;z<=8;++z) coordinates.emplace_back(x,y,z);
    for(int x: {-20,-9,-8,-7,15,16,17,30}) for(int y: {0,2,7,8,15}) for(int z: {-1,0,7,8}) coordinates.emplace_back(x,y,z);
    std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"coordinates\":[";
    for(size_t n=0;n<coordinates.size();++n){if(n)std::cout<<',';auto p=coordinates[n];std::cout<<'['<<p.x()<<','<<p.y()<<','<<p.z()<<']';} std::cout<<"],\"cases\":[";
    scalarCases<FloatGrid,PointSampler>("float",0); scalarCases<FloatGrid,BoxSampler>("float",1); scalarCases<FloatGrid,QuadraticSampler>("float",2);
    scalarCases<DoubleGrid,PointSampler>("double",0); scalarCases<DoubleGrid,BoxSampler>("double",1); scalarCases<DoubleGrid,QuadraticSampler>("double",2);
    for(int p: {0,2,4,7,12,13}) {
        run<Vec3SGrid,PointSampler>("vec3f",0,false,p,0,p>=12?2:0,true); run<Vec3SGrid,BoxSampler>("vec3f",1,false,p,1,p>=12?2:0,true); run<Vec3SGrid,QuadraticSampler>("vec3f",2,false,p,0,p>=12?2:0,true);
        run<Vec3SGrid,StaggeredPointSampler>("vec3f",0,true,p,0,p>=12?2:0,true); run<Vec3SGrid,StaggeredBoxSampler>("vec3f",1,true,p,1,p>=12?2:0,true); run<Vec3SGrid,StaggeredQuadraticSampler>("vec3f",2,true,p,0,p>=12?2:0,true);
    }
    std::cout<<"]}";
}
