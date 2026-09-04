// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/math/Proximity.h>
#include <fstream>
#include <iomanip>
#include <random>
using namespace openvdb;
std::ofstream out;bool first=true;
void num(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
void vec(const Vec3d&v){out<<'[';num(v.x());out<<',';num(v.y());out<<',';num(v.z());out<<']';}
void run(const Vec3d&a,const Vec3d&b,const Vec3d&c,const Vec3d&p){
    if(!first)out<<',';first=false;Vec3d uvw;double t;
    Vec3d triangle=math::closestPointOnTriangleToPoint(a,b,c,p,uvw),segment=math::closestPointOnSegmentToPoint(a,b,p,t);
    out<<"{\"a\":";vec(a);out<<",\"b\":";vec(b);out<<",\"c\":";vec(c);out<<",\"p\":";vec(p);out<<",\"triangle\":";vec(triangle);out<<",\"barycentric\":";vec(uvw);out<<",\"segment\":";vec(segment);out<<",\"t\":";num(t);out<<'}';
}
int main(int argc,char**argv){
    if(argc!=2)return 2;out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    std::mt19937_64 rng(73019);std::uniform_real_distribution<double>d(-8,8);auto v=[&](){double x=d(rng),y=d(rng),z=d(rng);return Vec3d(x,y,z);};
    for(int i=0;i<500;i++){Vec3d a=v(),b=v(),c=v(),p=v();run(a,b,c,p);}
    for(int i=0;i<12;i++){Vec3d p=v();run(Vec3d(0),Vec3d(0),Vec3d(0),p);run(Vec3d(0),Vec3d(0),Vec3d(1,0,0),p);run(Vec3d(0),Vec3d(1,0,0),Vec3d(0),p);run(Vec3d(0),Vec3d(1,0,0),Vec3d(1,0,0),p);run(Vec3d(0),Vec3d(1,0,0),Vec3d(2,0,0),p);}
    for(double x:{-1.,0.,0.25,0.5,1.,2.})for(double y:{-1.,0.,0.25,0.5,1.,2.})for(double z:{-2.,0.,3.})run(Vec3d(0),Vec3d(1,0,0),Vec3d(0,1,0),Vec3d(x,y,z));
    for(double scale:{1e-12,1e-8,1e-7,1e-6,1.,1e8,1e12}){Vec3d a(scale),b=a+Vec3d(scale*1e-8,0,0),c=a+Vec3d(0,scale*0.25,0);run(a,b,c,a+Vec3d(0.125*scale,0.125*scale,0.125*scale));}
    const double nan=std::numeric_limits<double>::quiet_NaN(),inf=std::numeric_limits<double>::infinity();run(Vec3d(nan,0,0),Vec3d(1,0,0),Vec3d(0,1,0),Vec3d(0.5));run(Vec3d(0),Vec3d(1,0,0),Vec3d(0,1,0),Vec3d(nan));run(Vec3d(0),Vec3d(1,0,0),Vec3d(0,1,0),Vec3d(inf));
    out<<"]}\n";return out.good()?0:3;
}
