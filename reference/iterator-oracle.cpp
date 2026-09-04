// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <fstream>
#include <iostream>
using Tree=openvdb::Int32Tree;using C=openvdb::Coord;
void setup(Tree&t){t.addTile(3,C(-4096,0,0),9,true);t.addTile(3,C(4096,0,0),19,false);t.addTile(2,C(0,0,0),11,true);t.addTile(1,C(128,0,0),13,true);t.setValueOn(C(256,0,0),17);t.setValueOff(C(257,0,0),19);}
void mix(uint64_t&hash,int64_t value){hash^=uint64_t(value);hash*=1099511628211ULL;}
template<class Iter> void run(std::ostream&o,Iter it,int min,int max,int mutation){it.setMinDepth(min);it.setMaxDepth(max);uint64_t hash=14695981039346656037ULL,count=0;o<<"\"first\":[";
for(;it;++it){if(mutation==1)it.setValue(42);else if(mutation==2)it.modifyValue([](int&v){v+=1;});else if(mutation==3)it.setActiveState(true);C c=it.getCoord();int v=*it;bool on=it.isValueOn();int level=it.getLevel();mix(hash,c.x());mix(hash,c.y());mix(hash,c.z());mix(hash,v);mix(hash,on);mix(hash,level);
if(count<20){if(count)o<<',';o<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<','<<v<<','<<on<<','<<level<<']';}count++;}
o<<"],\"count\":"<<count<<",\"hash\":"<<hash;}
int main(int argc,char**argv){try{if(argc!=2)return 2;openvdb::initialize();std::ofstream o(argv[1]);o<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
for(int mode=0;mode<3;mode++)for(int min=0;min<4;min++)for(int max=min;max<4;max++)for(int mutation=0;mutation<4;mutation++){Tree t(7);setup(t);if(!first)o<<',';first=false;o<<"{\"mode\":"<<mode<<",\"min\":"<<min<<",\"max\":"<<max<<",\"mutation\":"<<mutation<<',';if(mode==0)run(o,t.beginValueAll(),min,max,mutation);else if(mode==1)run(o,t.beginValueOn(),min,max,mutation);else run(o,t.beginValueOff(),min,max,mutation);o<<'}';}o<<"]}";if(!o)return 3;}catch(std::exception&e){std::cerr<<e.what()<<'\n';return 1;}}
