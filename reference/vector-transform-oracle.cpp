// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/VectorTransformer.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <tuple>
#include <map>
using namespace openvdb;
std::ofstream out;bool first=true;
void vector(const Vec3d&v){out<<'['<<v.x()<<','<<v.y()<<','<<v.z()<<']';}
Mat4d matrix(int id){
    Mat4d m=Mat4d::identity();
    if(id==1){m.preScale(Vec3d(2,0.5,-3));m.postTranslate(Vec3d(5,-7,11));}
    if(id==2){m.preRotate(math::X_AXIS,0.71);m.postShear(math::X_AXIS,math::Z_AXIS,0.3);m.postTranslate(Vec3d(1,2,3));}
    if(id==3){m.preScale(Vec3d(2,3,4));m(0,3)=0.125;m(1,3)=-0.25;m.postTranslate(Vec3d(2,-3,4));}
    if(id==4)m=Mat4d::zero();return m;
}
template<class V>void run(const char*type){
 using Tree=typename tree::Tree4<V,5,4,3>::Type;using G=Grid<Tree>;
 for(int mode=0;mode<5;mode++)for(int world=0;world<2;world++)for(int mat=0;mat<5;mat++){
    auto g=G::create(V(0.25,-0.5,1.25));g->setVectorType(VecType(mode));g->setIsInWorldSpace(world!=0);
    auto&t=g->tree();t.setValueOn(Coord(0),V(2,-3,4));t.setValueOn(Coord(1,0,0),V(0));t.setValueOn(Coord(2,0,0),V(7,8,-9));
    t.setValueOff(Coord(3,0,0),V(-2,3,-4));t.addTile(1,Coord(8,0,0),V(1,2,3),true);t.addTile(2,Coord(128,0,0),V(4,-5,6),false);
    t.addTile(3,Coord(4096),V(1,-1,2),true);t.addTile(3,Coord(-4096),V(-3,2,1),false);
    Mat4d m=matrix(mat);std::string error="";
    try{tools::transformVectors(*g,m);}catch(const ArithmeticError&){error="ArithmeticError";}
    if(!first)out<<',';first=false;out<<"{\"kind\":\"grid\",\"type\":\""<<type<<"\",\"mode\":"<<mode<<",\"world\":"<<world<<",\"matrixId\":"<<mat<<",\"matrix\":[";
    for(int i=0;i<16;i++){if(i)out<<',';out<<m.asPointer()[i];}
    out<<"],\"error\":\""<<error<<"\",\"background\":";vector(Vec3d(t.background()));
    out<<",\"active\":"<<t.activeVoxelCount()<<",\"leaves\":"<<t.leafCount()<<",\"groups\":[";
    using Key=std::tuple<int,bool,double,double,double>;std::map<Key,uint64_t> counts;
    for(auto it=t.cbeginValueAll();it;++it){Vec3d v(*it);counts[{it.getDepth(),it.isValueOn(),v.x(),v.y(),v.z()}]++;}
    bool f=true;for(const auto&entry:counts){if(!f)out<<',';f=false;out<<'['<<std::get<0>(entry.first)<<','<<std::get<1>(entry.first)<<',';vector(Vec3d(std::get<2>(entry.first),std::get<3>(entry.first),std::get<4>(entry.first)));out<<','<<entry.second<<']';}
    out<<"],\"probes\":[";f=true;
    std::vector<Coord> probes={Coord(0),Coord(1,0,0),Coord(2,0,0),Coord(3,0,0),Coord(7),Coord(8,0,0),Coord(128,0,0),Coord(4096),Coord(-4096),Coord(8192)};
    for(int i=0;i<97;i++)probes.emplace_back((i*11)%300,(i*7)%80,(i*13)%120);
    for(Coord c:probes){if(!f)out<<',';f=false;out<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<',';vector(Vec3d(t.getValue(c)));out<<','<<t.isValueOn(c)<<','<<t.getValueDepth(c)<<']';}
    out<<"]}";
 }
}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 run<Vec3s>("vec3f");run<Vec3d>("vec3d");run<Vec3i>("vec3i");
 for(int mode=0;mode<5;mode++)for(int world=0;world<2;world++){auto g=FloatGrid::create();g->setVectorType(VecType(mode));g->setIsInWorldSpace(world!=0);bool error=false;try{tools::transformVectors(*g,Mat4d::identity());}catch(const TypeError&){error=true;}out<<",{\"kind\":\"scalar\",\"type\":\"float\",\"mode\":"<<mode<<",\"world\":"<<world<<",\"error\":"<<error<<'}';}
 out<<"],\"metadata\":[";for(int mode=-1;mode<7;mode++){if(mode!=-1)out<<',';auto type=VecType(mode);out<<"{\"id\":"<<mode<<",\"text\":\""<<GridBase::vecTypeToString(type)<<"\",\"examples\":\""<<GridBase::vecTypeExamples(type)<<"\",\"description\":\""<<GridBase::vecTypeDescription(type)<<"\"}";}
 out<<"]}\n";return out.good()?0:3;}