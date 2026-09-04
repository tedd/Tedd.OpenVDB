// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/Interpolation.h>
#include <fstream>
#include <iomanip>
#include <iostream>
using C=openvdb::Coord;using V=openvdb::Vec3d;using Half=openvdb::math::half;
template<class T> struct Components {static constexpr int size=1;static double get(T v,int){return double(v);}static T make(double a,double,double,double){return T(a);}};
template<class T> struct Components<openvdb::math::Vec2<T>> {static constexpr int size=2;using Vec=openvdb::math::Vec2<T>;static double get(Vec v,int i){return double(v[i]);}static Vec make(double a,double b,double,double){return Vec(T(a),T(b));}};
template<class T> struct Components<openvdb::math::Vec3<T>> {static constexpr int size=3;using Vec=openvdb::math::Vec3<T>;static double get(Vec v,int i){return double(v[i]);}static Vec make(double a,double b,double c,double){return Vec(T(a),T(b),T(c));}};
template<class T> struct Components<openvdb::math::Vec4<T>> {static constexpr int size=4;using Vec=openvdb::math::Vec4<T>;static double get(Vec v,int i){return double(v[i]);}static Vec make(double a,double b,double c,double d){return Vec(T(a),T(b),T(c),T(d));}};
template<class T> void output(std::ostream&o,T value){o<<'[';for(int i=0;i<Components<T>::size;i++){if(i)o<<',';o<<Components<T>::get(value,i);}o<<']';}
template<class T,class Sampler>void run(std::ostream&o,const char*name,int order,bool staggered){using Tree=typename openvdb::tree::Tree4<T,5,4,3>::Type;using Grid=openvdb::Grid<Tree>;Tree tree(Components<T>::make(0.125,0.25,-0.5,0.75));
for(int x=-3;x<=4;x++)for(int y=-3;y<=4;y++)for(int z=-3;z<=4;z++){double a=((x*17+y*11-z*13)%31)*0.071;if constexpr(std::is_same<T,int8_t>::value)a*=35;else if constexpr(std::is_same<T,uint8_t>::value)a=128+a*50;else if constexpr(std::is_same<T,int16_t>::value)a=30000+a*500;else if constexpr(std::is_same<T,uint16_t>::value)a=60000+a*1500;T value=Components<T>::make(a,a*1.337+0.25,-a*0.793-0.5,a*3.125+0.75);if((x+y-z)%3==0)tree.setValueOn(C(x,y,z),value);else tree.setValueOff(C(x,y,z),value);}
openvdb::math::Mat4d matrix(1.25,0.125,0.0,0.0, 0.0,0.75,0.25,0.0, 0.0,0.0,1.5,0.0, 2.0,-1.0,0.5,1.0);openvdb::math::Transform source(openvdb::math::MapBase::Ptr(new openvdb::math::AffineMap(matrix)));auto target=source.copy();target->postTranslate(V(0.375,-0.25,0.125));
openvdb::tools::GridSampler<Tree,Sampler> sampler(tree,source);openvdb::tools::DualGridSampler<Tree,Sampler> dual(tree,source,*target),aligned(tree,source,source);
o<<"{\"type\":\""<<name<<"\",\"order\":"<<order<<",\"staggered\":"<<staggered<<",\"samples\":[";
for(int i=0;i<40;i++){V p((i%7)*0.625-2.5,((i*3)%9)*0.375-1.75,((i*5)%11)*0.25-1.25);if(i==0)p=V(-0.5);if(i==1)p=V(0);T value;bool active=Sampler::sample(tree,p,value);if(i)o<<',';o<<"{\"position\":";output(o,p);o<<",\"value\":";output(o,value);o<<",\"active\":"<<active<<",\"world\":";output(o,sampler.wsSample(source.indexToWorld(p)));C c(i%4-2,i%3-1,i%5-2);o<<",\"coord\":["<<c.x()<<','<<c.y()<<','<<c.z()<<"],\"dual\":";output(o,dual(c));o<<",\"aligned\":";output(o,aligned(c));o<<'}';}
o<<"]}";}
template<class T>void all(std::ostream&o,const char*name,bool&first){using namespace openvdb::tools;
#define EMIT(S,O,F) if(!first)o<<',';first=false;run<T,S>(o,name,O,F)
EMIT(PointSampler,0,false);EMIT(BoxSampler,1,false);EMIT(QuadraticSampler,2,false);
if constexpr(Components<T>::size==3){EMIT(StaggeredPointSampler,0,true);EMIT(StaggeredBoxSampler,1,true);EMIT(StaggeredQuadraticSampler,2,true);}
#undef EMIT
}
int main(int argc,char**argv){try{if(argc!=2)return 2;openvdb::initialize();std::ofstream o(argv[1]);o<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
all<int8_t>(o,"sbyte",first);all<uint8_t>(o,"byte",first);all<int16_t>(o,"short",first);all<uint16_t>(o,"ushort",first);all<int>(o,"int",first);all<float>(o,"float",first);all<double>(o,"double",first);all<Half>(o,"half",first);all<bool>(o,"bool",first);all<openvdb::Vec2d>(o,"vec2d",first);all<openvdb::Vec3d>(o,"vec3d",first);all<openvdb::Vec3s>(o,"vec3f",first);all<openvdb::Vec3i>(o,"vec3i",first);all<openvdb::Vec4d>(o,"vec4d",first);o<<"]}";return o?0:3;}catch(std::exception&e){std::cerr<<e.what()<<'\n';return 1;}}
