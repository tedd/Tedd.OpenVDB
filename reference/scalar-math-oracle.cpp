// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/math/Math.h>
#include <openvdb/math/Vec3.h>
#include <iostream>
#include <iomanip>
#include <limits>
#include <vector>
#include <cmath>
using namespace openvdb;
bool firstCase=true;
void start(const char* kind,const char* type){if(!firstCase)std::cout<<",\n";firstCase=false;std::cout<<"{\"kind\":\""<<kind<<"\",\"type\":\""<<type<<'"';}
template<class T>void number(T value){double x=double(value);if(std::isnan(x))std::cout<<"\"NaN\"";else if(std::isinf(x))std::cout<<(x<0?"\"-Infinity\"":"\"Infinity\"");else std::cout<<x;}
template<class T>void value(T x){std::cout<<',';number(x);}
template<class T>void core(const char* type,T x,T y){start("core",type);std::cout<<",\"x\":";number(x);std::cout<<",\"y\":";number(y);T clamped=x;bool changed=math::ClampTest01(clamped);std::cout<<",\"flags\":["<<changed<<','<<math::Sign(x)<<','<<math::SignChange(x,y)<<','<<math::ZeroCrossing(x,y)<<','<<math::isZero(x)<<','<<math::isNegative(x)<<','<<math::isApproxLarger(x,y,T(0.125))<<"],\"values\":[";number(math::Clamp(x,T(-0.5),T(1.25)));value(math::Clamp01(x));value(clamped);value(math::SmoothUnitStep(x));value(math::SmoothUnitStep(x,T(-1),T(2)));value(math::Pow2(x));value(math::Pow3(x));value(math::Pow4(x));value(math::Pow(x,5));value(math::Pow(x,-3));value(math::Inv(x));value(math::Chop(x,T(0.125)));value(math::Min(x,y));value(math::Max(x,y));value(math::Pow(T(math::Abs(x)),T(0.75)));value(math::Tolerance<T>::value());value(math::Delta<T>::value());value(math::negative(x));value(T(math::Abs(x)));std::cout<<"]}";}
template<class T>void floating(const char* type,T x,T y){start("floating",type);std::cout<<",\"x\":";number(x);std::cout<<",\"y\":";number(y);std::cout<<",\"values\":[";number(math::RoundUp(x));value(math::RoundDown(x));value(math::Round(x));value(math::RoundUp(x,y));value(math::RoundDown(x,y));value(math::Mod(x,y));value(math::EuclideanRemainder(x));value(math::IntegerPart(x));value(math::FractionalPart(x));for(unsigned digits:{0,1,2,5,19,20,63,64,65})value(math::Truncate(x,digits));value(math::Sqrt(x));value(math::Cbrt(x));value(math::Exp(x));value(math::Sin(x));value(math::Cos(x));std::cout<<"],\"ulps\":[";T nearby=std::nextafter(x,std::numeric_limits<T>::infinity());number(nearby);for(int n:{-1,0,1,2,8})std::cout<<','<<math::isUlpsEqual(x,nearby,std::conditional_t<std::is_same<T,float>::value,int32_t,int64_t>(n));std::cout<<"]}";}
template<class T>void sample(const char* type){std::vector<double> xs={-std::numeric_limits<double>::infinity(),-16384.125,-10.5,-2.5,-1.5,-1,-0.5,-0.125,-0.0,0,0.0001,0.125,0.5,0.75,1,1.5,2.5,10.5,16384.125,std::numeric_limits<double>::infinity(),std::numeric_limits<double>::quiet_NaN(),std::numeric_limits<float>::denorm_min(),-double(std::numeric_limits<float>::denorm_min()),std::numeric_limits<double>::denorm_min()};uint32_t state=0x749365ab;for(int i=0;i<256;++i){state=state*1664525u+1013904223u;xs.push_back((int32_t(state>>8)-8388608)/1048576.0);}for(size_t i=0;i<xs.size();++i){T x=T(xs[i]),y=T(i%9==0?0:i%7==0?-0.5:xs[(i+13)%xs.size()]);core(type,x,y);if constexpr(!std::is_same<T,math::half>::value)floating(type,x,y);}}
template<class T>void minmax(const char* type){for(int count=2;count<=8;++count)for(int shape=0;shape<12;++shape){T a[8];for(int i=0;i<8;++i)a[i]=T(((i*7+shape*3)%11)-5);if(shape<8)a[shape]=T(std::numeric_limits<double>::quiet_NaN());if(shape==8)for(int i=0;i<8;++i)a[i]=T(i%2?-0.0:0.0);if(shape==9)for(int i=0;i<8;++i)a[i]=T(1);if(shape==10){a[0]=T(-0.0);a[1]=T(0.0);}if(shape==11){a[0]=T(0.0);a[1]=T(-0.0);}T minimum,maximum;
#define MM(N,...) case N:minimum=math::Min(__VA_ARGS__);maximum=math::Max(__VA_ARGS__);break
switch(count){MM(2,a[0],a[1]);MM(3,a[0],a[1],a[2]);MM(4,a[0],a[1],a[2],a[3]);MM(5,a[0],a[1],a[2],a[3],a[4]);MM(6,a[0],a[1],a[2],a[3],a[4],a[5]);MM(7,a[0],a[1],a[2],a[3],a[4],a[5],a[6]);MM(8,a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7]);}
#undef MM
start("minmax",type);std::cout<<",\"inputs\":[";number(a[0]);for(int i=1;i<count;++i)value(a[i]);std::cout<<"],\"values\":[";number(minimum);value(maximum);std::cout<<"],\"indices\":["<<math::MinIndex(math::Vec3<T>(a[0],a[1],a[2]))<<','<<math::MaxIndex(math::Vec3<T>(a[0],a[1],a[2]))<<"],\"third\":";number(a[2]);std::cout<<'}';}}
int main(){std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[\n";sample<float>("float");sample<double>("double");sample<math::half>("half");minmax<float>("float");minmax<double>("double");minmax<math::half>("half");std::cout<<"]}\n";}
