// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main mergeOracleMain
#include "advanced-tree-oracle.cpp"
#undef main
#include <openvdb/tools/Dense.h>
#include <iomanip>
template<openvdb::tools::MemoryLayout Layout> void runDense(std::ostream&o,int target,int pattern) {
 using DT=openvdb::tools::Dense<double,Layout>;openvdb::DoubleTree t(1.0);
 if(target==1)t.setValueOff(C(7,7,7),9.0);
 if(target==2)t.setValueOn(C(7,7,7),9.0);
 if(target==3)t.addTile(1,C(0),9.0,true);
 if(target==4)t.addTile(3,C(0),9.0,true);
 openvdb::CoordBBox bounds(C(-1,0,0),C(8,2,2));DT dense(bounds,1.0);
 for(auto c=bounds.begin();c;++c){double v=1.0;if(pattern==1)v=2.0;if(pattern==2)v=1.0005;if(pattern==3)v=(((*c).x()+(*c).y()+(*c).z())%3)?double((*c).x()*3+(*c).y()+(*c).z())*0.25:1.0;dense.setValue(*c,v);}
 openvdb::tools::copyFromDense(dense,t,0.001,true);
 o<<"{\"layout\":"<<int(Layout)<<",\"target\":"<<target<<",\"pattern\":"<<pattern<<",\"result\":";snapshot(o,t);o<<'}';
}
int main(int argc,char**argv){try{if(argc!=2)return 2;openvdb::initialize();std::ofstream o(argv[1]);o<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
for(int layout=0;layout<2;layout++)for(int target=0;target<5;target++)for(int pattern=0;pattern<4;pattern++){if(!first)o<<',';first=false;if(layout==0)runDense<openvdb::tools::LayoutXYZ>(o,target,pattern);else runDense<openvdb::tools::LayoutZYX>(o,target,pattern);}o<<"]}";if(!o)return 3;}catch(std::exception&e){std::cerr<<e.what()<<'\n';return 1;}}
