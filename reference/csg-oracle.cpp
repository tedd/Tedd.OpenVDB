// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Fixture generator only: native OpenVDB is not a managed runtime dependency.
#include <openvdb/openvdb.h>
#include <openvdb/tools/Composite.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <tbb/global_control.h>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <set>
#include <string>
#include <vector>
using Coord = openvdb::Coord;
using Tree = openvdb::FloatTree;
struct Op { std::string op; Coord min,max; float value; bool active; int level; };
struct Case { std::string name,operation; bool prune,cancel; int radius=2; float backgroundA=3,backgroundB=5; std::vector<Op> a,b; };
Op point(Coord c,float v,bool on=true){return {"set",c,c,v,on,0};}
Op tile(Coord c,int level,float v,bool on=false){return {"tile",c,c,v,on,level};}
Op fill(Coord lo,Coord hi,float v,bool on=true){return {"fill",lo,hi,v,on,0};}
Op touch(Coord c){return {"touch",c,c,0,false,0};}
Op sphere(Coord c,float radius){return {"sphere",c,c,radius,false,0};}
void coord(Coord c){std::cout<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<']';}
void initialize(Tree& tree,const std::vector<Op>& ops){for(const auto& op:ops){
 if(op.op=="fill")tree.fill(openvdb::CoordBBox(op.min,op.max),op.value,op.active);
 else if(op.op=="tile")tree.addTile(op.level,op.min,op.value,op.active);
 else if(op.op=="touch")tree.touchLeaf(op.min);
 else if(op.op=="sphere"){auto grid=openvdb::tools::createLevelSetSphere<openvdb::FloatGrid>(op.value,openvdb::Vec3f(op.min.x(),op.min.y(),op.min.z()),1,tree.background(),false);tree.merge(grid->tree());}
 else if(op.active)tree.setValueOn(op.min,op.value);else tree.setValueOff(op.min,op.value);
}}
void operations(const std::vector<Op>& ops){std::cout<<'[';bool first=true;for(const auto& op:ops){if(!first)std::cout<<',';first=false;
 std::cout<<"{\"op\":\""<<op.op<<"\",\"min\":";coord(op.min);std::cout<<",\"max\":";coord(op.max);
 std::cout<<",\"value\":"<<op.value<<",\"active\":"<<(op.active?"true":"false")<<",\"level\":"<<op.level<<'}';}std::cout<<']';}
void number(float v){if(std::isfinite(v))std::cout<<v;else std::cout<<'"'<<(std::isnan(v)?"NaN":v<0?"-Infinity":"Infinity")<<'"';}
void result(const Tree& tree,const std::set<Coord>& samples){
 std::cout<<"{\"activeCount\":"<<tree.activeVoxelCount()<<",\"leafCount\":"<<tree.leafCount()<<",\"samples\":[";bool first=true;
 for(auto c:samples){if(!first)std::cout<<',';first=false;float v;bool on=tree.probeValue(c,v);std::cout<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<',';number(v);std::cout<<','<<(on?1:0)<<','<<tree.getValueDepth(c)<<']';}std::cout<<"]}";
}
void emit(const Case& c){Tree a(c.backgroundA),b(c.backgroundB);initialize(a,c.a);initialize(b,c.b);
 Tree::Ptr output;
 if(c.operation=="compSum")openvdb::tools::compSum(a,b);
 else if(c.operation=="compMul")openvdb::tools::compMul(a,b);
 else if(c.operation=="compMin")openvdb::tools::compMin(a,b);
 else if(c.operation=="compMax")openvdb::tools::compMax(a,b);
 else if(c.operation=="compDiv")openvdb::tools::compDiv(a,b);
 else if(c.operation=="compReplace")openvdb::tools::compReplace(a,b);
 else if(c.operation=="activeCopy")openvdb::tools::compActiveLeafVoxels(b,a);
 else if(c.operation=="activeAdd")openvdb::tools::compActiveLeafVoxels(b,a,[](float& dst,const float& src){dst+=src;});
 else if(c.operation=="unionCopy")output=openvdb::tools::csgUnionCopy(a,b);
 else if(c.operation=="intersectionCopy")output=openvdb::tools::csgIntersectionCopy(a,b);
 else if(c.operation=="differenceCopy")output=openvdb::tools::csgDifferenceCopy(a,b);
 else if(c.operation=="union")openvdb::tools::csgUnion(a,b,c.prune,c.cancel);
 else if(c.operation=="intersection")openvdb::tools::csgIntersection(a,b,c.prune,c.cancel);
 else openvdb::tools::csgDifference(a,b,c.prune,c.cancel);
 std::set<Coord> samples;
 for(int x=-2;x<=c.radius;++x)for(int y=-2;y<=c.radius;++y)for(int z=-2;z<=c.radius;++z)samples.insert(Coord(x,y,z));
 for(int p:{-4097,-4096,-129,-128,-9,-8,-1,0,1,7,8,9,127,128,129,4095,4096,4097})for(int q:{0,1,7,8,127,128}){samples.insert(Coord(p,q,q));samples.insert(Coord(q,p,q));samples.insert(Coord(q,q,p));}
 for(const auto* ops:{&c.a,&c.b})for(const auto& op:*ops)for(int d=-1;d<=1;++d){samples.insert(op.min+Coord(d));samples.insert(op.max+Coord(d));}
 std::cout<<"{\"name\":\""<<c.name<<"\",\"operation\":\""<<c.operation<<"\",\"prune\":"<<(c.prune?"true":"false")<<",\"cancel\":"<<(c.cancel?"true":"false")<<",\"backgroundA\":"<<c.backgroundA<<",\"backgroundB\":"<<c.backgroundB<<",\"initialA\":";operations(c.a);std::cout<<",\"initialB\":";operations(c.b);std::cout<<",\"resultA\":";result(output?*output:a,samples);std::cout<<",\"resultB\":";result(b,samples);std::cout<<'}';
}
int main(){tbb::global_control control(tbb::global_control::max_allowed_parallelism,1);openvdb::initialize();std::cout<<std::setprecision(17);std::vector<Case> cases;
 for(const auto& operation:{"union","intersection","difference","unionCopy","intersectionCopy","differenceCopy","compSum","compMul","compMin","compMax","compDiv","compReplace","activeCopy","activeAdd"})for(bool prune:{false,true})for(bool cancel:{false,true})for(int shape=0;shape<29;++shape){
 if(std::string(operation)!="union"&&std::string(operation)!="intersection"&&std::string(operation)!="difference"&&(prune||cancel))continue;
 Case c;c.operation=operation;c.prune=prune;c.cancel=cancel;c.name=std::string(operation)+"-prune-"+std::to_string(prune)+"-cancel-"+std::to_string(cancel)+"-shape-"+std::to_string(shape);
 switch(shape){
 case 0:c.radius=9;c.a={point(Coord(0),-1),point(Coord(1),2,false),point(Coord(2),-2,false),point(Coord(3),0,false),point(Coord(4),1)};c.b={point(Coord(0),-2,false),point(Coord(1),2),point(Coord(2),-1),point(Coord(3),0),point(Coord(4),1,false),point(Coord(7),-1),point(Coord(8),-2)};break;
 case 1:c.radius=9;c.a={fill(Coord(0),Coord(7),1),touch(Coord(0)),point(Coord(2),-2)};c.b={fill(Coord(0),Coord(7),-1),touch(Coord(0)),point(Coord(2),2)};break;
 case 2:c.radius=9;c.a={fill(Coord(0),Coord(7),1),touch(Coord(0)),point(Coord(2),-2)};c.b=c.a;break;
 case 3:c.radius=9;c.a={fill(Coord(0),Coord(7),-2,false),touch(Coord(0)),point(Coord(3),-0.5f)};c.b={fill(Coord(0),Coord(7),2,false),touch(Coord(0)),point(Coord(3),0.5f)};break;
 case 4:c.radius=9;c.a={point(Coord(-1),-1),point(Coord(7),2),point(Coord(8),-2)};c.b={point(Coord(-8),-2),point(Coord(7),-2),point(Coord(9),1)};break;
 case 5:case 6:case 7:c.a={tile(Coord(0),shape-4,-9)};c.b={point(Coord(1),-1)};break;
 case 8:case 9:case 10:c.a={tile(Coord(0),shape-7,-9,true)};c.b={point(Coord(1),-1)};break;
 case 11:case 12:case 13:c.a={tile(Coord(0),shape-10,9,true)};c.b={point(Coord(1),-1)};break;
 case 14:case 15:case 16:c.a={point(Coord(1),-1)};c.b={tile(Coord(0),shape-13,-9,true)};break;
 case 17:case 18:case 19:c.a={tile(Coord(0),shape-16,0)};c.b={point(Coord(1),-1)};break;
 case 20:c.a={point(Coord(-4096),-1)};c.b={point(Coord(4096),-2)};break;
 case 21:c.a={tile(Coord(0),3,-9,true)};c.b={tile(Coord(0),3,9,true)};break;
 case 22:c.a={tile(Coord(0),3,9,true)};c.b={tile(Coord(0),3,-9,true)};break;
 case 23:c.a={tile(Coord(0),3,3,false)};c.b={point(Coord(1),-1)};break;
 case 24:c.a={point(Coord(1),-1)};c.b={tile(Coord(0),3,5,false)};break;
 case 25:c.b={point(Coord(1),-1),point(Coord(2),-5,false),point(Coord(3),5,false),point(Coord(4),5,true)};break;
 case 26:c.a={point(Coord(1),-1)};break;
 case 27:break;
 case 28:c.radius=12;c.a={sphere(Coord(2),6)};c.b={sphere(Coord(5),5)};break;
 }
 cases.push_back(c);}
 std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;for(const auto& c:cases){if(!first)std::cout<<',';first=false;emit(c);}std::cout<<"]}\n";
}
