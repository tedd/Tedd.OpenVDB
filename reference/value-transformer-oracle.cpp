// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/ValueTransformer.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <cmath>
#include <type_traits>
#include <map>
#include <tuple>
using namespace openvdb;
std::ofstream out;bool first=true;
void number(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
template<class T>void value(const T&v){
 if constexpr(VecTraits<T>::IsVec){out<<'[';for(int i=0;i<T::size;i++){if(i)out<<',';value(v[i]);}out<<']';}
 else if constexpr(std::is_integral_v<T>)out<<v;else number(double(v));
}
template<class T>T make(double x,double y,double z,double w){
 if constexpr(VecTraits<T>::IsVec){T v;double a[]={x,y,z,w};for(int i=0;i<T::size;i++)v[i]=typename T::ValueType(a[i]);return v;}
 else if constexpr(std::is_same_v<T,math::half>)return T(float(x));else return T(x);
}
template<class Tree>void state(const Tree&t,Coord c){out<<"{\"active\":"<<t.activeVoxelCount()<<",\"leaves\":"<<t.leafCount()<<",\"depth\":"<<t.getValueDepth(c)<<",\"on\":"<<t.isValueOn(c)<<",\"value\":";value(t.getValue(c));out<<",\"near\":[";value(t.getValue(c.offsetBy(1)));out<<','<<t.isValueOn(c.offsetBy(1))<<','<<t.getValueDepth(c.offsetBy(1))<<"]}";}
template<class Target,class T>void modify(Target&tree,Coord c,T v,int operation){switch(operation){case 0:tools::setValueOnMin(tree,c,v);break;case 1:tools::setValueOnMax(tree,c,v);break;case 2:tools::setValueOnSum(tree,c,v);break;case 3:tools::setValueOnMult(tree,c,v);break;}}
template<class T>void modifiers(const char*type){
 using Tree=typename tree::Tree4<T,5,4,3>::Type;
 for(int scene=0;scene<6;scene++)for(int operation=0;operation<4;operation++)for(int accessor=0;accessor<2;accessor++){
    T background=make<T>(5,6,7,8);if constexpr(std::is_same_v<T,int64_t>)background=9007199254740993LL;
    Tree tree(background);Coord c(-1,3,128);T initial=make<T>(4,5,6,7);
    if(scene==1)tree.setValueOff(c,initial);if(scene==2)tree.setValueOn(c,initial);
    if(scene>=3)tree.addTile(scene-2,c,initial,scene!=4);
    typename Tree::Accessor acc(tree);
    if(!first)out<<',';first=false;out<<"{\"kind\":\"modifier\",\"type\":\""<<type<<"\",\"scene\":"<<scene<<",\"operation\":"<<operation<<",\"accessor\":"<<accessor<<",\"background\":";value(background);out<<",\"initial\":";value(initial);out<<",\"steps\":[";
    double xs[]={0,3,-2,1,7};for(int step=0;step<5;step++){if(step)out<<',';T candidate=make<T>(xs[step],2+step,3-step,4+step);if(accessor)modify(acc,c,candidate,operation);else modify(tree,c,candidate,operation);out<<"{\"candidate\":";value(candidate);out<<",\"state\":";state(tree,c);out<<'}';}out<<"]}";
 }
}
FloatGrid::Ptr input(){
 auto g=FloatGrid::create();auto&t=g->tree();for(int x=-2;x<=2;x++)for(int y=-2;y<=2;y++)for(int z=-2;z<=2;z++)if((x+y+z)%3!=0)t.setValueOn(Coord(x,y,z),float(x+2*y-3*z));
 t.addTile(1,Coord(8,0,0),7.f,true);t.setValueOff(Coord(24,0,0),2.f);t.addTile(3,Coord(4096),-2.f,false);return g;
}
template<class Tree>void groups(const Tree&t){
 using Key=std::tuple<int,bool,double>;std::map<Key,uint64_t> counts;
 for(auto it=t.cbeginValueAll();it;++it)counts[{it.getDepth(),it.isValueOn(),double(*it)}]++;
 out<<'[';bool f=true;for(auto&entry:counts){if(!f)out<<',';f=false;out<<'['<<std::get<0>(entry.first)<<','<<std::get<1>(entry.first)<<',';number(std::get<2>(entry.first));out<<','<<entry.second<<']';}out<<']';
}
struct ApplyOp{mutable int64_t count=11;template<class It>void operator()(const It&it)const{++count;it.setValue(*it+0.5f);}};
struct Writer{int64_t count=11;template<class It,class Acc>void operator()(const It&it,Acc&acc){++count;double v=double(*it)*2+1;CoordBBox box;it.getBoundingBox(box);box.translate(Coord(2,0,0));acc.tree().fill(box,v,true);}};
struct SumOp{double sum=11;uint64_t count=13,joins=0;template<class It>void operator()(const It&it){sum+=double(*it)*double(it.getVoxelCount());++count;}void join(const SumOp&other){sum+=other.sum;count+=other.count;++joins;}};
template<class It>void applyCase(It it,FloatTree&tree,int mode,int shared,int threaded){
 ApplyOp op;tools::foreach(it,op,threaded!=0,shared!=0);if(!first)out<<',';first=false;out<<"{\"kind\":\"foreach\",\"mode\":"<<mode<<",\"shared\":"<<shared<<",\"threaded\":"<<threaded<<",\"count\":"<<op.count<<",\"groups\":";groups(tree);out<<'}';
}
template<class It>void sumCase(It it,int mode,int threaded){
 SumOp op;tools::accumulate(it,op,threaded!=0);if(!first)out<<',';first=false;out<<"{\"kind\":\"accumulate\",\"mode\":"<<mode<<",\"threaded\":"<<threaded<<",\"count\":"<<op.count<<",\"sum\":";number(op.sum);out<<",\"joins\":"<<op.joins<<'}';
}
void callbacks(){
 for(int mode=0;mode<3;mode++)for(int shared=0;shared<2;shared++)for(int threaded=0;threaded<2;threaded++){
    auto g=input();if(mode==0)applyCase(g->beginValueOn(),g->tree(),mode,shared,threaded);else if(mode==1)applyCase(g->beginValueOff(),g->tree(),mode,shared,threaded);else applyCase(g->beginValueAll(),g->tree(),mode,shared,threaded);
 }
 for(int mode=0;mode<3;mode++)for(int threaded=0;threaded<2;threaded++){auto g=input();if(mode==0)sumCase(g->cbeginValueOn(),mode,threaded);else if(mode==1)sumCase(g->cbeginValueOff(),mode,threaded);else sumCase(g->cbeginValueAll(),mode,threaded);}
 for(int shared=0;shared<2;shared++)for(int threaded=0;threaded<2;threaded++)for(int merge=0;merge<3;merge++){
    auto g=input();DoubleTree output(9);output.setValueOn(Coord(-20),17);Writer op;tools::transformValues(g->cbeginValueOn(),output,op,threaded!=0,shared!=0,MergePolicy(merge));
    if(!first)out<<',';first=false;out<<"{\"kind\":\"transform\",\"shared\":"<<shared<<",\"threaded\":"<<threaded<<",\"merge\":"<<merge<<",\"count\":"<<op.count<<",\"groups\":";groups(output);out<<",\"active\":"<<output.activeVoxelCount()<<",\"probes\":[";bool f=true;
    for(int i=0;i<97;i++){Coord c((i*11)%30-6,(i*7)%15-5,(i*13)%17-5);if(!f)out<<',';f=false;out<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<',';number(output.getValue(c));out<<','<<output.isValueOn(c)<<','<<output.getValueDepth(c)<<']';}out<<"]}";
 }
}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 modifiers<bool>("bool");modifiers<int>("int");modifiers<int64_t>("long");modifiers<float>("float");modifiers<double>("double");modifiers<math::half>("half");
 modifiers<Vec2i>("vec2i");modifiers<Vec2s>("vec2f");modifiers<Vec2d>("vec2d");modifiers<Vec3i>("vec3i");modifiers<Vec3s>("vec3f");modifiers<Vec3d>("vec3d");modifiers<Vec4i>("vec4i");modifiers<Vec4s>("vec4f");modifiers<Vec4d>("vec4d");callbacks();
 out<<"]}\n";return out.good()?0:3;}