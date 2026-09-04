// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/Count.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <cmath>
#include <type_traits>
using namespace openvdb;
std::ofstream out;bool first=true;
void number(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class T>void value(const T&v){if constexpr(std::is_arithmetic_v<T>)number(double(v));else{out<<'[';for(int i=0;i<T::size;i++){if(i)out<<',';number(v[i]);}out<<']';}}
template<class T>T make(double x,double y,double z,double w){
    if constexpr(std::is_arithmetic_v<T>)return T(x);
    else {T v;double input[]={x,y,z,w};for(int i=0;i<T::size;i++)v[i]=typename T::ValueType(input[i]);return v;}
}
template<class T>void run(const char*name,bool floating){
    using Tree=typename tree::Tree4<T,5,4,3>::Type;
    for(int scene=0;scene<7;scene++){
        T background=make<T>(5,5,5,5);Tree tree(background);
        struct Operation {int kind;Coord coordinate;T value;int level;bool active;};
        std::vector<Operation> ops;
        if(scene==1)ops.push_back({0,Coord(0),make<T>(-2,-2,-2,-2),0,true});
        if(scene==2){ops.push_back({0,Coord(0),make<T>(-2,-2,-2,-2),0,true});ops.push_back({0,Coord(8),make<T>(2,2,2,2),0,true});ops.push_back({1,Coord(16),make<T>(99,99,99,99),0,false});}
        if(scene==3){ops.push_back({2,Coord(4096),make<T>(10,10,10,10),3,true});ops.push_back({2,Coord(0),make<T>(-10,-10,-10,-10),2,true});ops.push_back({0,Coord(-2),make<T>(5,5,5,5),0,true});ops.push_back({2,Coord(8192),make<T>(20,20,20,20),3,false});}
        if(scene==4){ops.push_back({0,Coord(0),make<T>(1,9,-2,8),0,true});ops.push_back({0,Coord(1),make<T>(1,-3,20,1),0,true});ops.push_back({0,Coord(8),make<T>(2,-99,-99,-99),0,true});ops.push_back({0,Coord(9),make<T>(1,9,-2,9),0,true});}
        if(scene==5){ops.push_back({0,Coord(0),make<T>(floating?NAN:0,1,2,3),0,true});ops.push_back({0,Coord(1),make<T>(2,-9,4,5),0,true});}
        if(scene==6){ops.push_back({0,Coord(0),make<T>(1,3,4,5),0,true});ops.push_back({0,Coord(1),make<T>(1,floating?NAN:0,0,0),0,true});ops.push_back({0,Coord(2),make<T>(1,3,4,6),0,true});}
        for(auto op:ops){if(op.kind==0)tree.setValueOn(op.coordinate,op.value);else if(op.kind==1)tree.setValueOnly(op.coordinate,op.value);else tree.addTile(op.level,op.coordinate,op.value,op.active);}
        auto range=tools::minMax(tree,false);
        if(!first)out<<',';first=false;out<<"{\"type\":\""<<name<<"\",\"scene\":"<<scene<<",\"background\":";value(background);out<<",\"operations\":[";
        bool f=true;for(auto op:ops){if(!f)out<<',';f=false;out<<'['<<op.kind<<','<<op.coordinate.x()<<','<<op.coordinate.y()<<','<<op.coordinate.z()<<',';value(op.value);out<<','<<op.level<<','<<op.active<<']';}
        out<<"],\"minimum\":";value(range.min());out<<",\"maximum\":";value(range.max());out<<'}';
    }
}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    run<bool>("bool",false);run<int32_t>("int",false);run<float>("float",true);run<double>("double",true);
    run<Vec2i>("vec2i",false);run<Vec2s>("vec2f",true);run<Vec2d>("vec2d",true);run<Vec3i>("vec3i",false);run<Vec3s>("vec3f",true);run<Vec3d>("vec3d",true);run<Vec4i>("vec4i",false);run<Vec4s>("vec4f",true);run<Vec4d>("vec4d",true);
    out<<"]}\n";return out.good()?0:3;}
