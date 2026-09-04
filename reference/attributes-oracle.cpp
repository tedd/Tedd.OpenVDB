// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/points/AttributeArray.h>
#include <openvdb/points/AttributeGroup.h>
#include <fstream>
#include <iomanip>
#include <cmath>
#include <type_traits>
using namespace openvdb; using namespace openvdb::points;
std::ofstream out; bool first=true;
void number(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class T>void value(const T&v){
 if constexpr(VecTraits<T>::IsVec){out<<'[';for(int i=0;i<T::size;i++){if(i)out<<',';value(v[i]);}out<<']';}
 else if constexpr(std::is_integral_v<T>)out<<+v;else number(double(v));
}
template<class T>T make(double x,int i=0){
 if constexpr(VecTraits<T>::IsVec){T v;for(int j=0;j<T::size;j++)v[j]=typename T::ValueType(x+double(j)*.25+double(i)*.03125);return v;}
 else if constexpr(std::is_same_v<T,math::half>)return T(float(x+double(i)*.03125));
 else return T(x+double(i)*.03125);
}
void hex(const char*bytes,size_t size){const char*digits="0123456789abcdef";out<<'"';for(size_t i=0;i<size;i++){unsigned b=(unsigned char)bytes[i];out<<digits[b>>4]<<digits[b&15];}out<<'"';}
template<class A>void snapshot(const A&a,const char*op){
 out<<"{\"op\":\""<<op<<"\",\"size\":"<<a.size()<<",\"stride\":"<<a.stride()<<",\"dataSize\":"<<a.dataSize()<<",\"flags\":"<<int(a.flags())<<",\"uniform\":"<<a.isUniform()<<",\"storage\":";
 hex(a.constDataAsByteArray(),(a.isUniform()?1:a.dataSize())*a.storageTypeSize());
 out<<",\"values\":[";for(Index i=0;i<a.dataSize();i++){if(i)out<<',';value(a.get(i));}out<<"]}";
}
template<class T,class C>void arrays(){
 using A=TypedAttributeArray<T,C>;
 for(int shape=0;shape<5;shape++)for(int seed=0;seed<4;seed++){
    Index n[]={0,1,9,5,5};Index s[]={1,1,1,3,23};bool constant=shape!=4;
    T init=make<T>(double(seed)*.375-.625);A a(n[shape],s[shape],constant,init);
    if(!first)out<<',';first=false;
    out<<"{\"kind\":\"array\",\"type\":\""<<a.valueType()<<"\",\"codec\":\""<<C::name()<<"\",\"n\":"<<n[shape]<<",\"s\":"<<s[shape]<<",\"constant\":"<<constant<<",\"seed\":"<<seed<<",\"initial\":";value(init);
    out<<",\"valueSize\":"<<a.valueTypeSize()<<",\"storageSize\":"<<a.storageTypeSize()<<",\"traits\":["<<a.valueTypeIsFloatingPoint()<<','<<a.valueTypeIsClass()<<','<<a.valueTypeIsVector()<<','<<a.valueTypeIsQuaternion()<<','<<a.valueTypeIsMatrix()<<"],\"steps\":[";
    snapshot(a,"initial");a.set(0,a.get(0));out<<',';snapshot(a,"same");
    a.collapse(init);out<<',';snapshot(a,"collapse");
    a.expand();out<<',';snapshot(a,"expand");
    out<<"],\"writes\":[";
    for(Index i=0;i<a.dataSize();i++){if(i)out<<',';T v=make<T>((double(int(i)%7)-3)*.271+double(seed)*.03125,int(i)%3);value(v);a.set(i,v);}
    out<<"],\"written\":";snapshot(a,"written");
    A copy(a);out<<",\"copyEqual\":"<<(a==copy);
    copy.set(copy.dataSize()-1,make<T>(9.75));
    out<<",\"tailEqual\":"<<(a==copy);
    T fill=make<T>(double(seed)*.173-.31);out<<",\"fill\":";value(fill);a.fill(fill);out<<",\"filled\":";snapshot(a,"filled");
    bool compact=a.compact();out<<",\"compacted\":"<<compact<<",\"compact\":";snapshot(a,"compact");
    a.setHidden(true);a.setTransient(true);a.setStreaming(true);
    {AttributeWriteHandle<T> handle(a);handle.set(0,init);}out<<",\"writeHandle\":";snapshot(a,"writeHandle");
    {AttributeHandle<T> handle(a);out<<",\"handle\":{\"size\":"<<handle.size()<<",\"stride\":"<<handle.stride()<<",\"last\":";value(handle.get(handle.size()-1,handle.stride()-1));out<<'}';}
    out<<",\"disposed\":";snapshot(a,"disposed");
    out<<'}';
 }
}
template<class T>void codecs(){
 arrays<T,NullCodec>();arrays<T,FixedPointCodec<true>>();arrays<T,FixedPointCodec<false>>();
 arrays<T,FixedPointCodec<true,UnitRange>>();arrays<T,FixedPointCodec<false,UnitRange>>();
}
template<class T,class C>void special(){
 using A=TypedAttributeArray<T,C>;
 for(int scene=0;scene<5;scene++){
    T v=scene==0?T(-0.0):scene==1?T(std::numeric_limits<float>::infinity()):scene==2?T(-std::numeric_limits<float>::infinity()):scene==3?T(std::numeric_limits<float>::quiet_NaN()):T(1.00048828125);
    A a(3,1,true,v);a.expand();A b(a);bool equal=a==b;bool compact=a.compact();
    if(!first)out<<',';first=false;out<<"{\"kind\":\"special\",\"type\":\""<<a.valueType()<<"\",\"codec\":\""<<C::name()<<"\",\"scene\":"<<scene<<",\"input\":";value(v);
    out<<",\"equal\":"<<equal<<",\"compacted\":"<<compact<<",\"state\":";snapshot(a,"special");out<<'}';
 }
}
struct CopyIter {Index i=0;operator bool()const{return i<3;}CopyIter&operator++(){++i;return *this;}Index sourceIndex()const{return 2-i;}Index targetIndex()const{return i+1;}};
void copies(){
 for(int shape=0;shape<3;shape++)for(int uniform=0;uniform<2;uniform++)for(int compact=0;compact<2;compact++){
    if(shape==1&&uniform)continue; // Native memcpy reads beyond a single uniform storage value for stride > 1.
    using A=TypedAttributeArray<float>;bool constant=shape!=2;A source(4,shape==1?3:shape==2?7:1,constant,2.f),target(5,shape==1?3:shape==2?9:1,constant,8.f);
    if(!uniform){source.expand();for(Index i=0;i<source.dataSize();i++)source.set(i,float(i)+.25f);}
    target.copyValues(source,CopyIter{},compact!=0);
    if(!first)out<<',';first=false;out<<"{\"kind\":\"copy\",\"shape\":"<<shape<<",\"uniform\":"<<uniform<<",\"compact\":"<<compact<<",\"state\":";snapshot(target,"copy");out<<'}';
 }
}
void groups(){
 for(int offset=0;offset<8;offset++)for(int scene=0;scene<6;scene++){
    bool constant=scene!=5;Index stride=scene==4?3:scene==5?15:1;
    GroupAttributeArray a(7,stride,constant,scene==1?255:0);
    if(scene>=2){a.expand();for(Index i=0;i<a.dataSize();i++)a.set(i,scene==2?GroupType((i%2)<<offset):GroupType(i*11));}
    if(!first)out<<',';first=false;out<<"{\"kind\":\"group\",\"offset\":"<<offset<<",\"scene\":"<<scene<<",\"initial\":";snapshot(a,"initial");
    GroupWriteHandle writer(a,GroupType(offset));out<<",\"constructed\":";snapshot(a,"constructed");
    writer.set(2,true);out<<",\"set\":";snapshot(a,"set");
    bool on=writer.collapse(true);out<<",\"onResult\":"<<on<<",\"on\":";snapshot(a,"on");
    bool off=writer.collapse(false);out<<",\"offResult\":"<<off<<",\"off\":";snapshot(a,"off");
    bool compact=writer.compact();out<<",\"compactResult\":"<<compact<<",\"compact\":";snapshot(a,"compact");out<<'}';
 }
 GroupAttributeArray a(256);a.expand();for(Index i=0;i<256;i++)a.set(i,GroupType(i));
 for(int mask=0;mask<256;mask++){
    GroupHandle reader(a,GroupType(mask),GroupHandle::BitMask{});if(!first)out<<',';first=false;out<<"{\"kind\":\"group-mask\",\"mask\":"<<mask<<",\"values\":[";
    for(Index i=0;i<256;i++){if(i)out<<',';out<<reader.get(i);}out<<"]}";
 }
}
int main(int argc,char**argv){
 if(argc!=2)return 2;initialize();out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 codecs<float>();codecs<double>();codecs<Vec3s>();codecs<Vec3d>();
 arrays<float,TruncateCodec>();arrays<int,TruncateCodec>();arrays<Vec3s,TruncateCodec>();arrays<Vec3i,TruncateCodec>();
 arrays<Vec3s,UnitVecCodec>();arrays<Vec3d,UnitVecCodec>();
 arrays<bool,NullCodec>();arrays<int,NullCodec>();arrays<int64_t,NullCodec>();arrays<math::half,NullCodec>();
 arrays<Vec2i,NullCodec>();arrays<Vec2s,NullCodec>();arrays<Vec2d,NullCodec>();arrays<Vec3i,NullCodec>();
 arrays<Vec4i,NullCodec>();arrays<Vec4s,NullCodec>();arrays<Vec4d,NullCodec>();
 special<float,NullCodec>();special<double,NullCodec>();special<math::half,NullCodec>();special<float,TruncateCodec>();copies();groups();
 out<<"]}\n";return out.good()?0:3;
}
