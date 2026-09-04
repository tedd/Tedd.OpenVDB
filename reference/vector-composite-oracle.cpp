// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/Composite.h>
#include <iomanip>
#include <iostream>
#include <string>
template<typename V> void vector(const V& v){std::cout<<'[';for(int i=0;i<V::size;++i){if(i)std::cout<<',';std::cout<<v[i];}std::cout<<']';}
template<typename V> void emit(const std::string& type,bool& first){
 for(int seed=0;seed<40;++seed){
 V a,b;for(int i=0;i<V::size;++i){a[i]=(seed*(i+3)%13)-6;b[i]=(seed*(i+7)%11)+1;}
 if(seed<6){a=V(0);b=V(0);a[seed%V::size]=(seed%2)?-5:5;b[(seed+1)%V::size]=(seed%2)?-5:5;}
 if(!first)std::cout<<',';first=false;std::cout<<"{\"name\":\""<<type<<'-'<<seed<<"\",\"type\":\""<<type<<"\",\"a\":";vector(a);std::cout<<",\"b\":";vector(b);
 std::cout<<",\"sum\":";vector(a+b);std::cout<<",\"product\":";vector(a*b);std::cout<<",\"minimum\":";vector(openvdb::tools::composite::min(a,b));std::cout<<",\"maximum\":";vector(openvdb::tools::composite::max(a,b));
 // Nonzero component divisors avoid C++ integer division undefined behavior.
 V divisor=b;for(int i=0;i<V::size;++i)if(divisor[i]==0)divisor[i]=1;std::cout<<",\"divisor\":";vector(divisor);std::cout<<",\"quotient\":";vector(a/divisor);std::cout<<'}';
 }}
int main(){std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
 emit<openvdb::math::Vec2<double>>("Vec2d",first);emit<openvdb::Vec3d>("Vec3d",first);emit<openvdb::Vec3f>("Vec3f",first);emit<openvdb::Vec3i>("Vec3i",first);emit<openvdb::math::Vec4<double>>("Vec4d",first);std::cout<<"]}\n";}
