// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/Filter.h>
#include <fstream>
#include <iomanip>
#include <iostream>
using C=openvdb::Coord;
template<typename G> typename G::Ptr setup() {
 using T=typename G::ValueType;auto g=G::create(T(0.5));auto&t=g->tree();
 for(int x=0;x<4;x++)for(int y=0;y<4;y++)for(int z=0;z<4;z++) {
 C c(x,y,z);if((x+y+z)%3)t.setValueOn(c,T((x*3-y+z)*0.25));else t.setValueOff(c,T(-2.5)); }
 t.addTile(1,C(8,0,0),T(3.25),true);t.addTile(1,C(0,8,0),T(-1.25),false);t.setValueOn(C(-1,0,0),T(9));return g;
}
openvdb::FloatGrid::Ptr mask(int variant) {
 auto m=openvdb::FloatGrid::create(0.25f);m->tree().fill(openvdb::CoordBBox(C(0),C(8)),1.0f,false);m->tree().setValueOn(C(1),0.5f);
 if(variant==2){m->transform().postScale(2.0);m->transform().postTranslate(openvdb::Vec3d(1,-1,0.5));}return m;
}
template<typename G> void run(std::ostream&o,const char*type,int operation,bool tiles,int maskType) {
 auto g=setup<G>();auto m=mask(maskType);openvdb::tools::Filter<G> f(*g);f.setGrainSize(0);f.setProcessTiles(tiles);if(maskType==2){f.setMaskRange(0.1f,0.9f);f.invertMask();}
 const auto*mp=maskType==0?nullptr:m.get();int width=operation%2+1;int iterations=2;
 switch(operation){case 0:f.mean(width,iterations,mp);break;case 1:f.gaussian(width,iterations,mp);break;case 2:f.median(width,iterations,mp);break;case 3:f.offset(typename G::ValueType(0.75),mp);break;}
 o<<"{\"type\":\""<<type<<"\",\"operation\":"<<operation<<",\"tiles\":"<<tiles<<",\"mask\":"<<maskType<<",\"active\":"<<g->activeVoxelCount()<<",\"nodes\":[";
 auto counts=g->tree().nodeCount();for(size_t i=0;i<counts.size();i++){if(i)o<<',';o<<counts[i];}o<<"],\"samples\":[";bool first=true;
 for(auto leaf=g->tree().cbeginLeaf();leaf;++leaf)for(auto v=leaf->cbeginValueAll();v;++v){auto c=v.getCoord();if(!first)o<<',';first=false;o<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<','<<double(*v)<<','<<(v.isValueOn()?1:0)<<",3]";}
 for(C c:{C(8,0,0),C(15,7,7),C(0,8,0),C(4096,0,0)}){if(!first)o<<',';first=false;typename G::ValueType v;bool on=g->tree().probeValue(c,v);o<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<','<<double(v)<<','<<on<<','<<g->tree().getValueDepth(c)<<']';}
 o<<"]}";
}
int main(int argc,char**argv){try{if(argc!=2)return 2;openvdb::initialize();std::ofstream o(argv[1]);o<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
 for(int type=0;type<2;type++)for(int op=0;op<4;op++)for(int tiles=0;tiles<2;tiles++)for(int m=0;m<3;m++){if(!first)o<<',';first=false;if(type==0)run<openvdb::FloatGrid>(o,"float",op,tiles!=0,m);else run<openvdb::DoubleGrid>(o,"double",op,tiles!=0,m);}o<<"]}";if(!o)return 3;
 }catch(std::exception&e){std::cerr<<e.what()<<'\n';return 1;}}
