// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main unusedPcgOracleMain
#include "pcg-oracle.cpp"
#undef main
#include <openvdb/math/QuantizedUnitVec.h>
#include <cstring>
#include <random>
uint32_t bits(float value) { uint32_t result;std::memcpy(&result,&value,4);return result; }
void decodeBlock(int begin) {
    start("decode","float");field("start",begin);std::cout<<",\"values\":[";
    for(int i=begin;i<begin+256;++i) { if(i!=begin)std::cout<<',';uint16_t code=uint16_t(i);auto v=QuantizedUnitVec::unpack(code);QuantizedUnitVec::flipSignBits(code);std::cout<<'['<<bits(v.x())<<','<<bits(v.y())<<','<<bits(v.z())<<','<<code<<']'; }
    std::cout<<"]}";
}
template<class T> void packBlocks() {
    std::mt19937 engine(123991);
    for(int block=0;block<64;++block) {
        start("pack",precision<T>());field("start",block*64);std::cout<<",\"values\":[";
        for(int j=0;j<64;++j) {
            int i=block*64+j;T x,y,z;
            if(i<128) {x=T(i);y=T(0);z=T(126-i);}
            else if(i<256) {x=std::nextafter(T(i-128),i%2?std::numeric_limits<T>::infinity():-std::numeric_limits<T>::infinity());y=T(1);z=T(125-(i-128));}
            else { x=T(int(engine()%2049)-1024)/T(17);y=T(int(engine()%2049)-1024)/T(23);z=T(int(engine()%2049)-1024)/T(31);T scale=i%3==0?T(1e-30):i%3==1?T(1e30):T(1);x*=scale;y*=scale;z*=scale; }
            if(i==256)x=y=z=T(0);if(i==257)x=y=z=T(-0.0);
            if(j)std::cout<<',';std::cout<<'[';num(x);std::cout<<',';num(y);std::cout<<',';num(z);std::cout<<','<<QuantizedUnitVec::pack(Vec3<T>(x,y,z))<<']';
        }
        std::cout<<"]}";
    }
}
int main() { std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[\n";for(int i=0;i<65536;i+=256)decodeBlock(i);packBlocks<float>();packBlocks<double>();std::cout<<"]}\n";return 0; }
