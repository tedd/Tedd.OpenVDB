// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/math/Stats.h>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <vector>
struct Input{double value;uint64_t count;};
uint32_t next(uint32_t&s){s^=s<<13;s^=s>>17;s^=s<<5;return s;}
int main(int argc,char**argv){if(argc!=2)return 2;std::ofstream o(argv[1]);o<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
for(int seed=1;seed<=25;seed++)for(int mode=0;mode<4;mode++){
 uint32_t state=seed;std::vector<Input> values;openvdb::math::Stats a,b;
 for(int i=0;i<20;i++){double value=(seed%2?1e9:0)+double(int(next(state)%2001)-1000)*0.125;uint64_t count=mode==0?1:(uint64_t(1)<<((i%4)*12));values.push_back({value,count});auto&s=mode==2&&i>=10?b:a;if(mode==0)s.add(value);else s.add(value,count);}
 if(mode==2)a.add(b);if(mode==3)a.add(a);
 if(!first)o<<',';first=false;o<<"{\"seed\":"<<seed<<",\"mode\":"<<mode<<",\"inputs\":[";bool f=true;for(auto&v:values){if(!f)o<<',';f=false;o<<'['<<v.value<<','<<v.count<<']';}
 o<<"],\"count\":"<<a.size()<<",\"min\":"<<a.min()<<",\"max\":"<<a.max()<<",\"mean\":"<<a.mean()<<",\"variance\":"<<a.variance()<<'}';
}
o<<"],\"histograms\":[";first=true;
for(int bins=1;bins<=10;bins++){openvdb::math::Histogram h(-1,1,bins);for(double value:{-2.0,-1.0,-0.75,-0.25,0.0,0.25,0.75,1.0,1.0+5e-11,1.0+2e-10})h.add(value,3);if(!first)o<<',';first=false;o<<"{\"bins\":"<<bins<<",\"count\":"<<h.size()<<",\"values\":[";for(int i=0;i<bins;i++){if(i)o<<',';o<<h.count(i);}o<<"]}";}o<<"]}";return o?0:3;}
