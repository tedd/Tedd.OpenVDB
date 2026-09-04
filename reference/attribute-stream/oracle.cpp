// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/points/AttributeArray.h>
#include <openvdb/points/StreamCompression.h>
#include <openvdb/io/io.h>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <cstring>
#include <type_traits>
using namespace openvdb;
using namespace openvdb::points;
namespace fs = std::filesystem;
static std::ofstream output;
static fs::path managedDirectory;
static int caseIndex = 0, verified = 0;
static bool generating = true;

template<class T> void jsonValue(const T& value) {
    if constexpr (VecTraits<T>::IsVec) {
        output << '['; for (int i=0;i<T::size;i++) { if(i)output<<',';jsonValue(value[i]); } output<<']';
    } else if constexpr (std::is_integral_v<T>) output << +value;
    else output << double(value);
}
static void hex(const char* bytes, size_t size) {
    const char* digits="0123456789abcdef";output<<'"';
    for(size_t i=0;i<size;i++) {const unsigned v=(unsigned char)bytes[i];output<<digits[v>>4]<<digits[v&15];}output<<'"';
}
template<class T> T inputValue(int i,int profile) {
    // Values are serialized into the fixture too: managed construction does not duplicate this generator.
    const uint32_t bits=uint32_t(i)*2654435761u+uint32_t(profile)*2246822519u;
    const double x=(profile>=6&&profile<=9)?0.0:profile==11||profile==18?
        double(bits&0xffffffu)/16777216.0-.5:double(i%7-3)*.125+.03125;
    if constexpr(VecTraits<T>::IsVec) return T(typename T::ValueType(x),typename T::ValueType(x+.125),typename T::ValueType(.375-x));
    else if constexpr(std::is_integral_v<T>) return T(profile==11||profile==18?int32_t(bits):i%7-3);
    else return T(x);
}
template<class A> void checkNativeRead(const fs::path& path,const A& expected,bool omitted) {
    std::ifstream stream(path,std::ios::binary);
    if(!stream)throw std::runtime_error("Missing managed stream: "+path.string());
    if(omitted) {if(stream.peek()!=EOF)throw std::runtime_error("Transient stream was emitted");++verified;return;}
    A actual;actual.read(stream);
    if(!stream || stream.peek()!=EOF) throw std::runtime_error("Native reader did not consume exactly one stream: "+path.string());
    if(actual.size()!=expected.size()||actual.stride()!=expected.stride()||actual.dataSize()!=expected.dataSize()||actual.flags()!=expected.flags()||actual.isUniform()!=expected.isUniform())
        throw std::runtime_error("Managed/native attribute metadata mismatch: "+path.string());
    const size_t bytes=(expected.isUniform()?1:expected.dataSize())*expected.storageTypeSize();
    if(std::memcmp(actual.constDataAsByteArray(),expected.constDataAsByteArray(),bytes)!=0)
        throw std::runtime_error("Managed/native encoded storage mismatch: "+path.string());
    for(Index i=0;i<expected.dataSize();i++) {auto a=actual.get(i),b=expected.get(i);if(std::memcmp(&a,&b,sizeof(a)))throw std::runtime_error("Managed/native decoded value mismatch: "+path.string());}
    ++verified;
}
template<class T,class Codec> void profiles() {
    using A=TypedAttributeArray<T,Codec>;
    for(int profile=0;profile<20;profile++,caseIndex++) {
        Index n=9,stride=1;bool constant=true;
        const Index width=sizeof(typename A::StorageType);
        if(profile==0)n=0;
        if(profile==1){n=9;stride=3;}
        if(profile==2){n=5;stride=23;constant=false;}
        if(profile==5)n=1;
        if(profile==6)n=std::max(Index(1),48/width);
        if(profile==7)n=48/width+1;
        if(profile==8)n=127/width;
        if(profile==9)n=128/width+1;
        if(profile==10||profile==18)n=257;
        if(profile==11)n=1024;
        if(profile==12){n=7;stride=3;}
        if(profile==13){n=7;stride=23;constant=false;}
        if(profile==19){n=0;stride=0;constant=false;}
        const T initial=inputValue<T>(0,profile);
        A array(n,stride,constant,initial);
        const bool expanded=profile>=5&&profile!=19;
        if(expanded){array.expand();for(Index i=0;i<array.dataSize();i++)array.set(i,inputValue<T>(int(i),profile));}
        array.setHidden(profile==3||profile==4||profile==14||profile==17);
        array.setTransient(profile==3||profile==4||profile==15||profile==17);
        array.setStreaming(profile==3||profile==4||profile==16||profile==17);
        const bool outputTransient=profile==3||profile==15;
        const bool omitted=array.isTransient()&&!outputTransient;
        if(!generating) {
            checkNativeRead(managedDirectory/(std::to_string(caseIndex)+"-raw.bin"),array,omitted);
            checkNativeRead(managedDirectory/(std::to_string(caseIndex)+"-blosc.bin"),array,omitted);
            continue;
        }
        if(caseIndex)output<<',';
        output<<"{\"id\":"<<caseIndex<<",\"profile\":"<<profile<<",\"type\":\""<<array.valueType()<<"\",\"codec\":\""<<Codec::name()<<"\",\"n\":"<<n<<",\"s\":"<<stride<<",\"constant\":"<<constant<<",\"expanded\":"<<expanded<<",\"outputTransient\":"<<outputTransient<<",\"omitted\":"<<omitted<<",\"initial\":";
        jsonValue(initial);output<<",\"inputs\":[";
        if(expanded)for(Index i=0;i<array.dataSize();i++){if(i)output<<',';jsonValue(inputValue<T>(int(i),profile));}output<<']';
        output<<",\"size\":"<<array.size()<<",\"stride\":"<<array.stride()<<",\"dataSize\":"<<array.dataSize()<<",\"flags\":"<<int(array.flags())<<",\"uniform\":"<<array.isUniform()<<",\"storageSize\":"<<width<<",\"storage\":";
        hex(array.constDataAsByteArray(),(array.isUniform()?1:array.dataSize())*width);
        output<<",\"values\":[";for(Index i=0;i<array.dataSize();i++){if(i)output<<',';jsonValue(array.get(i));}output<<']';
        for(int compressed=0;compressed<2;compressed++){
            std::ostringstream stream(std::ios::binary);io::setDataCompression(stream,compressed?io::COMPRESS_BLOSC:io::COMPRESS_NONE);
            array.write(stream,outputTransient);const auto bytes=stream.str();
            output<<(compressed?",\"blosc\":":",\"raw\":");hex(bytes.data(),bytes.size());
            if(!omitted){std::istringstream input(bytes,std::ios::binary);A roundtrip;roundtrip.read(input);if(!input||!(roundtrip==array))throw std::runtime_error("Native self-check failed");}
        }
        output<<'}';
    }
}
template<class T>void fixed(){profiles<T,FixedPointCodec<true>>();profiles<T,FixedPointCodec<false>>();profiles<T,FixedPointCodec<true,UnitRange>>();profiles<T,FixedPointCodec<false,UnitRange>>();}
int main(int argc,char**argv) {
    try {
        if(argc!=3)return 2;initialize();
        if(!compression::bloscCanCompress())throw std::runtime_error("Oracle requires Blosc-enabled OpenVDB");
        generating=std::string(argv[1])=="generate";
        if(generating){output.open(argv[2],std::ios::binary);output<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";}
        else if(std::string(argv[1])=="verify")managedDirectory=argv[2];else return 2;
        profiles<int32_t,NullCodec>();profiles<float,NullCodec>();profiles<double,NullCodec>();profiles<Vec3s,NullCodec>();
        profiles<int32_t,TruncateCodec>();profiles<float,TruncateCodec>();profiles<Vec3s,TruncateCodec>();
        fixed<float>();fixed<double>();fixed<Vec3s>();fixed<Vec3d>();
        profiles<Vec3s,UnitVecCodec>();profiles<Vec3d,UnitVecCodec>();
        if(generating){output<<"]}\n";if(!output.good())return 3;}
        std::cout<<(generating?"Generated ":"Verified ")<<(generating?caseIndex:verified)<<" attribute streams/profiles\n";
        return 0;
    }catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 1;}
}
