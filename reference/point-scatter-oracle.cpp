// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/PointScatter.h>
#include <fstream>
#include <iomanip>
#include <random>
#include <vector>
#include <limits>
using namespace openvdb;
struct Points { std::vector<Vec3d> data; void add(const Vec3d& p) { data.push_back(p); } };
void vec(std::ostream& o,const Vec3d& v) { o<<'['<<v.x()<<','<<v.y()<<','<<v.z()<<']'; }
FloatGrid::Ptr grid(int scene,int tr) {
 auto g=FloatGrid::create(7.0f); g->setName("scatter source");
 if(scene==1) g->tree().setValue(Coord(0),1.0f);
 if(scene==2) {g->tree().setValue(Coord(-9,0,2),.25f);g->tree().setValue(Coord(-1,2,-1),2.0f);g->tree().setValue(Coord(0),0.0f);g->tree().setValue(Coord(7,7,7),-2.0f);g->tree().setValue(Coord(8,1,-2),3.0f);g->tree().setValueOff(Coord(20),90.0f);}
 if(scene==3) {g->tree().addTile(1,Coord(-8),.015625f,true);g->tree().setValue(Coord(0),2.0f);g->tree().addTile(1,Coord(8),.03125f,true);}
 if(scene==4) {g->tree().addTile(2,Coord(0),.000001f,true);g->tree().setValue(Coord(-1),1.0f);}
 if(scene==5) g->tree().addTile(3,Coord(0),.0000000001f,true);
 if(scene==6) {g->tree().setValueOff(Coord(0),5.0f);g->tree().addTile(1,Coord(-8),3.0f,false);}
 if(scene==7) {g->tree().fill(CoordBBox(Coord(-2,-1,0),Coord(2,1,1)),.5f,true);g->tree().setValue(Coord(0),4.0f);}
 auto t=math::Transform::createLinearTransform();
 if(tr==1) {t=math::Transform::createLinearTransform(.75);t->postTranslate(Vec3d(.25,-.375,.125));t->postRotate(.2,math::Z_AXIS);}
 if(tr==2) {Mat4d m=Mat4d::identity();m[0][0]=1.25;m[1][1]=.75;m[2][2]=1.5;m[0][1]=.25;m[1][2]=.125;t=math::Transform::createLinearTransform(m);}
 if(tr==3) t=math::Transform::createFrustumTransform(BBoxd(Vec3d(-10),Vec3d(10)),.7,20.0,.5);
 g->setTransform(t);return g;
}
void scatter(std::ostream& o,int scene,int tr,int mode,int profile) {
 auto g=grid(scene,tr);Points points;std::mt19937 engine(profile==0?42:profile==1?0:5489);double spread=profile==0?1.0:profile==1?.5:0.0;
 o<<"{\"scene\":"<<scene<<",\"transform\":"<<tr<<",\"mode\":"<<mode<<",\"profile\":"<<profile<<",\"calls\":[";
 auto run=[&](auto& s) {for(int i=0;i<2;++i){if(i)o<<',';bool result=s(*g);o<<"{\"result\":"<<(result?"true":"false")<<",\"pointCount\":"<<s.getPointCount()<<",\"voxelCount\":"<<s.getVoxelCount()<<'}';} };
 if(mode==0) {tools::UniformPointScatter<Points,std::mt19937> s(points,Index64(9),engine,spread);run(s);o<<"],\"target\":"<<s.getTargetPointCount()<<",\"density\":"<<s.getPointsPerVolume();}
 if(mode==1) {float d=scene>=4&&scene<=5?.0000000001f:.007f;tools::UniformPointScatter<Points,std::mt19937> s(points,d,engine,spread);run(s);o<<"],\"target\":"<<s.getTargetPointCount()<<",\"density\":"<<s.getPointsPerVolume();}
 if(mode==2) {float d=scene==5?.00000001f:scene==4?.000002f:scene==3?.005f:1.25f;tools::DenseUniformPointScatter<Points,std::mt19937> s(points,d,engine,spread);run(s);o<<']';}
 if(mode==3) {tools::NonUniformPointScatter<Points,std::mt19937> s(points,.6f,engine,spread);run(s);o<<']';}
 o<<",\"engineNext\":"<<engine()<<",\"points\":[";for(size_t i=0;i<points.data.size();++i){if(i)o<<',';vec(o,points.data[i]);}o<<"]}";
}
int main(int argc,char**argv) {initialize();std::ofstream o(argv[1]);o<<std::setprecision(17);o<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"engines\":[";
 unsigned seeds[]={0,42,5489,0xffffffffu};for(int j=0;j<4;++j){if(j)o<<',';std::mt19937 e(seeds[j]);o<<"{\"seed\":"<<seeds[j]<<",\"raw\":[";for(int i=0;i<1300;++i){if(i)o<<',';o<<e();}o<<"],\"real\":[";math::Rand01<double,std::mt19937> r(seeds[j]);for(int i=0;i<100;++i){if(i)o<<',';o<<r();}o<<"]}";}
 o<<"],\"ranges\":[";std::pair<uint64_t,uint64_t> ranges[]={{0,0},{9,9},{0,1},{0,8},{5,999},{999,5},{0,0x7fffffff},{0,0x80000000},{0,0xffffffff},{0,0x100000000},{0,0x100000001},{0,0xfffffffff},{0,0x7fffffffffffffffull},{0,0x8000000000000000ull},{0,0xfffffffffffffffeull},{0,0xffffffffffffffffull},{0xffffffffffffffffull-12,0xffffffffffffffffull}};bool first=true;for(unsigned seed:seeds)for(auto bounds:ranges){if(!first)o<<',';first=false;math::RandInt<uint64_t,std::mt19937> r(seed,bounds.first,bounds.second);o<<"{\"seed\":"<<seed<<",\"minimum\":"<<bounds.first<<",\"maximum\":"<<bounds.second<<",\"values\":[";for(int i=0;i<100;++i){if(i)o<<',';o<<r();}o<<"]}";}
 o<<"],\"cases\":[";first=true;for(int scene=0;scene<8;++scene)for(int tr=0;tr<4;++tr)for(int mode=0;mode<4;++mode)for(int p=0;p<3;++p){if(!first)o<<',';first=false;scatter(o,scene,tr,mode,p);}o<<"]}";return o?0:1;}
