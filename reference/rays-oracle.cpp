// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/RayIntersector.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <fstream>
#include <iomanip>
#include <vector>
#include <cmath>
using namespace openvdb;
using Ray = math::Ray<double>;
std::ofstream out;
void number(double v) { if (std::isfinite(v)) out << v; else out << '"' << (std::isnan(v) ? "NaN" : v > 0 ? "Infinity" : "-Infinity") << '"'; }
template<class V> void vec(const V& v) { out << '['; number(v[0]); out << ','; number(v[1]); out << ','; number(v[2]); out << ']'; }
void rayJson(const Ray& r) { out << "\"eye\":"; vec(r.eye()); out << ",\"direction\":"; vec(r.dir()); out << ",\"range\":["; number(r.t0()); out << ','; number(r.t1()); out << ']'; }
void spans(const std::vector<Ray::TimeSpan>& values) { out << '['; bool first=true; for(const auto& v:values) { if(!first)out<<',';first=false;out<<'[';number(v.t0);out<<',';number(v.t1);out<<']'; } out << ']'; }
FloatGrid::Ptr volume(int scene)
{
    auto g=FloatGrid::create(0);
    if(scene==0) {
        for(const Coord& c:std::vector<Coord>{{-17,-1,0},{0,0,0},{7,7,7},{24,0,0},{127,1,1},{128,1,1},{4095,0,0},{4096,0,0},{-4097,0,0}})g->tree().setValueOn(c,1);
        g->tree().setValueOff(Coord(16,0,0),3);
        g->tree().addTile(1,Coord(48,0,0),2,true);
        g->tree().addTile(2,Coord(256,0,0),2,true);
        g->tree().addTile(3,Coord(8192,0,0),2,true);
    } else if(scene==1) {
        g->tree().addTile(3,Coord(-4096,0,0),0,true);
        g->tree().addTile(2,Coord(0,0,0),0,true);
        g->tree().addTile(1,Coord(128,0,0),0,true);
        g->tree().setValueOff(Coord(130,0,0),7);
        g->tree().addTile(1,Coord(160,0,0),3,false);
        g->tree().touchLeaf(Coord(184,0,0));
        g->tree().setValueOn(Coord(200,0,0),0);
    } else {
        for(int x=-18;x<=19;x+=3)for(int y=-12;y<=12;y+=4)for(int z=-9;z<=9;z+=3)
            if((x+2*y-z)%7!=0)g->tree().setValueOn(Coord(x,y,z),1);
    }
    auto t=math::Transform::createLinearTransform(0.75);t->postRotate(0.31,math::Z_AXIS);t->postTranslate(Vec3d(2.25,-1.5,0.75));g->setTransform(t);return g;
}
std::vector<Ray> rays(int scene)
{
    std::vector<Ray> r;
    double left=scene==2?-60:-5000,right=scene==2?60:13000;
    for(double y: {0.25,1.25,7.25,8.25,-0.25,130.25}) {
        r.emplace_back(Vec3d(left,y,0.375),Vec3d(1,0,0),0.0,right-left);
        r.emplace_back(Vec3d(right,y,0.375),Vec3d(-1,0,0),0.0,right-left);
    }
    r.emplace_back(Vec3d(0.5,-50,0.5),Vec3d(0,1,0),0,150);
    r.emplace_back(Vec3d(0.5,0.5,-50),Vec3d(0,0,1),0,150);
    for(int i=0;i<14;i++) { Vec3d eye(-42, double(i*3-18)+0.125,double(i%5-2)+0.375); Vec3d d(1,0.037*i-0.2,0.019*i-0.1);d.normalize();r.emplace_back(eye,d,0.01,155); }
    r.emplace_back(Vec3d(0.5,0.5,0.5),Vec3d(1,0,0),0,200);
    r.emplace_back(Vec3d(-0.5,0.5,0.5),Vec3d(2,0,0),0,40);
    r.emplace_back(Vec3d(-8.5,0.5,0.5),Vec3d(1,0,0),0,8.5001);
    r.emplace_back(Vec3d(7.5,0.5,0.5),Vec3d(1,0,0),0,0.5001);
    r.emplace_back(Vec3d(0.5,0.5,0.5),Vec3d(1,0,0),0.2,0.200001);
    return r;
}
template<int L> void volumeCase(int scene,int dilation,bool world,bool& first)
{
    auto grid=volume(scene); tools::VolumeRayIntersector<FloatGrid,L> master(*grid,dilation);
    if(!first)out<<',';first=false;out<<"{\"kind\":\"volume\",\"scene\":"<<scene<<",\"level\":"<<L<<",\"dilation\":"<<dilation<<",\"world\":"<<world<<",\"bbox\":[";vec(master.bbox().min());out<<',';vec(master.bbox().max());out<<"],\"rays\":[";
    bool rf=true;for(auto ray:rays(scene)) {
        if(world) { if(std::abs(ray.dir().length()-1)>1e-8)continue; ray=ray.indexToWorld(*grid); }
        tools::VolumeRayIntersector<FloatGrid,L> a(master),b(master);
        bool hit=world?a.setWorldRay(ray):a.setIndexRay(ray);
        if(world)b.setWorldRay(ray);else b.setIndexRay(ray);
        if(!rf)out<<',';rf=false;out<<'{';rayJson(ray);out<<",\"clip\":"<<hit;
        std::vector<Ray::TimeSpan> all,march;
        if(hit) { a.hits(all);for(int n=0;n<1000;n++) {auto s=b.march();if(!s.valid())break;march.push_back(s);} }
        out<<",\"hits\":";spans(all);out<<",\"march\":";spans(march);
        if(!all.empty()) {double t=all.front().mid();out<<",\"indexPosition\":";vec(a.getIndexPos(t));out<<",\"worldPosition\":";vec(a.getWorldPos(t));out<<",\"worldTime\":";number(a.getWorldTime(t));}
        out<<'}';
    }out<<"]}";
}
template<class Grid> typename Grid::Ptr levelGrid(int scene)
{
    using T=typename Grid::ValueType;typename Grid::Ptr grid;
    if(scene<2)grid=tools::createLevelSetSphere<Grid>(T(4.25),Vec3f(0.25f,-0.375f,0.5f),T(0.5),T(3));
    else {grid=Grid::create(T(3));grid->setGridClass(GRID_LEVEL_SET);for(int x=-9;x<=9;x++)for(int y=-9;y<=9;y++)for(int z=-9;z<=9;z++){T v=T(x)*T(0.5)+T(y)*T(0.125)-T(z)*T(0.0625)-T(0.375);if(std::abs(v)<T(3))grid->tree().setValueOn(Coord(x,y,z),v);else grid->tree().setValueOff(Coord(x,y,z),v<0?T(-3):T(3));}grid->setTransform(math::Transform::createLinearTransform(0.5));}
    if(scene==1) {grid->transform().postRotate(0.31,math::Z_AXIS);grid->transform().postTranslate(Vec3d(2.25,-1.5,0.75));}return grid;
}
template<class Grid,int L,int I> void levelCase(int scene,double iso,bool world,bool& first)
{
    using T=typename Grid::ValueType;auto grid=levelGrid<Grid>(scene);tools::LevelSetRayIntersector<Grid,tools::LinearSearchImpl<Grid,I>,L> inter(*grid,T(iso));
    if(!first)out<<',';first=false;out<<"{\"kind\":\"levelset\",\"type\":\""<<(std::is_same_v<T,float>?"float":"double")<<"\",\"scene\":"<<scene<<",\"level\":"<<L<<",\"iterations\":"<<I<<",\"iso\":"<<iso<<",\"world\":"<<world<<",\"rays\":[";
    std::vector<Ray> rr;for(int i=-10;i<=10;i++){Vec3d e(-20,double(i)*0.875+0.125,double(i%4)*0.375+0.25);Vec3d d(1,0.013*i,0.021*i);d.normalize();rr.emplace_back(e,d,0.01,50);}
    rr.emplace_back(Vec3d(0.125,-0.25,0.375),Vec3d(1,0,0),0.01,50);
    rr.emplace_back(Vec3d(20,0.25,0.375),Vec3d(-1,0,0),0,50);
    rr.emplace_back(Vec3d(0.25,-20,0.375),Vec3d(0,1,0),0,50);
    rr.emplace_back(Vec3d(0.25,0.375,-20),Vec3d(0,0,1),0,50);
    rr.emplace_back(Vec3d(-20,12.25,12.375),Vec3d(1,0,0),0,50);
    bool rf=true;for(auto ray:rr) {if(world)ray=ray.indexToWorld(*grid);double time=-999;Vec3d pos(999),normal(999);bool hit=world?inter.intersectsWS(ray,pos,normal,time):inter.intersectsIS(ray,pos,time);if(!rf)out<<',';rf=false;out<<'{';rayJson(ray);out<<",\"hit\":"<<hit;if(hit){out<<",\"time\":";number(time);out<<",\"position\":";vec(pos);if(world){out<<",\"normal\":";vec(normal);}}out<<'}';}out<<"]}";
}
template<class Grid,int L> void levelIterations(int scene,bool world,bool& first) {levelCase<Grid,L,0>(scene,0,world,first);levelCase<Grid,L,2>(scene,0.25,world,first);levelCase<Grid,L,5>(scene,-0.375,world,first);}
int main(int argc,char**argv)
{
    if(argc!=2)return 2;openvdb::initialize();out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
    for(int s=0;s<3;s++)for(int d=0;d<3;d++)for(bool w:{false,true}){volumeCase<0>(s,d,w,first);volumeCase<1>(s,d,w,first);volumeCase<2>(s,d,w,first);}
    for(int s=0;s<3;s++)for(bool w:{false,true}){levelIterations<FloatGrid,-1>(s,w,first);levelIterations<FloatGrid,0>(s,w,first);levelIterations<FloatGrid,1>(s,w,first);levelIterations<FloatGrid,2>(s,w,first);levelIterations<DoubleGrid,-1>(s,w,first);levelIterations<DoubleGrid,0>(s,w,first);levelIterations<DoubleGrid,1>(s,w,first);levelIterations<DoubleGrid,2>(s,w,first);}out<<"]}\n";return out.good()?0:3;
}
