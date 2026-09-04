// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Independent review fixtures for generic background reset and root predicates.
#include <openvdb/openvdb.h>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <limits>
#include <string>
void number(double v){if(std::isfinite(v))std::cout<<v;else std::cout<<'"'<<(std::isnan(v)?"NaN":v<0?"-Infinity":"Infinity")<<'"';}
template<class T> void value(const T& v){number(double(v));}
template<class T> void value(const openvdb::math::Vec2<T>& v){std::cout<<'[';number(v.x());std::cout<<',';number(v.y());std::cout<<']';}
template<class T> void value(const openvdb::math::Vec3<T>& v){std::cout<<'[';number(v.x());std::cout<<',';number(v.y());std::cout<<',';number(v.z());std::cout<<']';}
template<class T> void value(const openvdb::math::Vec4<T>& v){std::cout<<'[';for(int i=0;i<4;i++){if(i)std::cout<<',';number(v[i]);}std::cout<<']';}
template<class T>void emit(std::string name,T old,T replacement,T sample,bool& first){
 using Tree=typename openvdb::tree::Tree4<T,5,4,3>::Type;Tree tree(old);tree.addTile(3,openvdb::Coord(0),sample,false);
 typename Tree::LeafNodeType leaf(openvdb::Coord(0),sample,false);leaf.resetBackground(old,replacement);
 if(!first)std::cout<<',';first=false;std::cout<<"{\"name\":\""<<name<<"\",\"old\":";value(old);std::cout<<",\"replacement\":";value(replacement);std::cout<<",\"sample\":";value(sample);std::cout<<",\"rootEmpty\":"<<(tree.empty()?"true":"false")<<",\"afterReset\":";value(leaf.getValue(0));std::cout<<'}';
}
template<class V>void vectors(std::string type,bool& first){int n=0;for(double scale:{0.,1.,1e8})for(double delta:{0.,1e-9,5e-8,5e-7,1.,10.}){V old(scale),replacement(2),sample(old);sample[0]+=typename V::ValueType(delta);emit(type+"-"+std::to_string(n++),old,replacement,sample,first);}V old(1),replacement(2),sample(old);sample[0]=std::numeric_limits<typename V::ValueType>::quiet_NaN();emit(type+"-nan",old,replacement,sample,first);}
template<class T>void scalars(std::string type,bool& first){int n=0;for(double delta:{0.,1e-16,1e-15,1e-9,1e-8,1e-7,0.0009765625,0.001953125})emit(type+"-"+std::to_string(n++),T(0),T(2),T(delta),first);emit(type+"-nan",T(1),T(2),T(std::numeric_limits<float>::quiet_NaN()),first);}
int main(){std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
 vectors<openvdb::math::Vec2<double>>("Vec2d",first);vectors<openvdb::Vec3d>("Vec3d",first);vectors<openvdb::Vec3f>("Vec3f",first);vectors<openvdb::math::Vec4<double>>("Vec4d",first);
 scalars<float>("float",first);scalars<double>("double",first);scalars<openvdb::math::half>("Half",first);emit("Half-subtraction-boundary",openvdb::math::half(0.001953125f),openvdb::math::half(2),openvdb::math::half(0.000976085662841796875f),first);std::cout<<"]}\n";}
