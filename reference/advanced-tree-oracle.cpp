// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/Count.h>
#include <fstream>
#include <iostream>
using Tree = openvdb::Int32Tree;
using C = openvdb::Coord;
void setup(Tree& t,int s) {
 switch(s) {
 case 0: break;
 case 1: t.addTile(3,C(0),-t.background(),false); break;
 case 2: t.addTile(3,C(0),9,true); break;
 case 3: t.addTile(2,C(0),10,true); break;
 case 4: t.addTile(1,C(0),11,true); break;
 case 5: t.setValueOn(C(0),21);t.setValueOff(C(1),-t.background());t.setValueOn(C(7),22);t.setValueOff(C(8),23);t.setValueOn(C(128),24);break;
 case 6: t.setValueOn(C(4096,0,0),25);t.setValueOff(C(4097,1,1),-t.background());break;
 case 7: t.denseFill(openvdb::CoordBBox(C(0),C(8)),31,true);t.setValueOff(C(1),32);break;
 case 8: t.addTile(3,C(0),-t.background(),false);t.setValueOn(C(0),41);t.addTile(1,C(8,0,0),42,false);break;
 }
}
template<typename TreeT> void snapshot(std::ostream& o,const TreeT& t) {
 auto counts=t.nodeCount();
 o<<"{\"active\":"<<t.activeVoxelCount()<<",\"inactive\":"<<openvdb::tools::countInactiveVoxels(t)<<",\"rootEntries\":"<<t.root().getTableSize()<<",\"nodes\":[";
 for(size_t i=0;i<counts.size();++i) { if(i)o<<',';o<<counts[i]; }
 o<<"],\"samples\":[";
 bool first=true;
 int xs[]={-4097,-1,0,1,2,7,8,9,127,128,129,4095,4096,4097};
 int yz[]={0,1,7,8,9,128};
 for(int x:xs) for(int y:yz) for(int z:yz) {
   C c(x,y,z);typename TreeT::ValueType v;bool on=t.probeValue(c,v);if(!first)o<<',';first=false;
   o<<'['<<x<<','<<y<<','<<z<<','<<v<<','<<(on?1:0)<<','<<t.getValueDepth(c)<<']';
 }
 o<<"]}";
}
int main(int argc,char**argv) {
 try {
 openvdb::initialize(); if(argc!=2) return 2;std::ofstream o(argv[1]);
 o<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
 for(int policy=0;policy<3;++policy) for(int a=0;a<9;++a) for(int b=0;b<9;++b) {
   if(!first)o<<',';first=false;Tree ta(5),tb(7);setup(ta,a);setup(tb,b);
   if(policy==0) ta.merge(tb,openvdb::MERGE_ACTIVE_STATES);
   else if(policy==1) ta.merge(tb,openvdb::MERGE_NODES);
   else ta.merge(tb,openvdb::MERGE_ACTIVE_STATES_AND_NODES);
   o<<"{\"policy\":"<<policy<<",\"a\":"<<a<<",\"b\":"<<b<<",\"result\":";snapshot(o,ta);
   o<<",\"sourceEntries\":"<<tb.root().getTableSize()<<'}';
 }
 o<<"]}"; if(!o) return 3;
 }catch(std::exception&e){std::cerr<<e.what()<<'\n';return 1;}
}
