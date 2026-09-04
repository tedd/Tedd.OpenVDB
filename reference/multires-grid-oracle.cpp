// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/MultiResGrid.h>
#include <tbb/global_control.h>
#include <fstream>
#include <iomanip>
#include <cmath>
using namespace openvdb;
std::ofstream out;bool first=true;
void number(double v){if(std::isfinite(v))out<<v;else out<<'"'<<(std::isnan(v)?"NaN":v>0?"Infinity":"-Infinity")<<'"';}
void coord(const Coord&c){out<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<']';}
template<class T>typename Grid<typename tree::Tree4<T,5,4,3>::Type>::Ptr source(int scene){
    using Tree=typename tree::Tree4<T,5,4,3>::Type;using G=Grid<Tree>;
    auto g=G::create(T(scene==5?1.5:scene==4?9:0));g->setName("detail");g->insertMeta("custom",StringMetadata("retained"));
    g->setTransform(math::Transform::createLinearTransform(0.5));g->transform().postTranslate(Vec3d(1.25,-2.5,4));
    auto&t=g->tree();
    if(scene==1){t.setValueOn(Coord(0),T(64));t.setValueOn(Coord(-3,1,5),T(-7));t.setValueOnly(Coord(9,-8,0),T(21));}
    if(scene==2)for(int x=-4;x<=4;x++)for(int y=-4;y<=4;y++)for(int z=-4;z<=4;z++)t.setValueOn(Coord(x,y,z),T(0.125*x*x+0.0625*y*z-0.25*z+0.5));
    if(scene==3){t.addTile(1,Coord(0),T(2),true);t.addTile(1,Coord(-16,8,0),T(-1),true);t.setValueOn(Coord(17,2,-5),T(8));}
    if(scene==4){t.touchLeaf(Coord(-8));t.setValueOnly(Coord(1,2,3),T(-11));t.addTile(1,Coord(16),T(27),false);t.setValueOn(Coord(4,3,2),T(5));}
    if(scene==5){g->setGridClass(GRID_LEVEL_SET);for(int x=-9;x<=9;x++)for(int y=-9;y<=9;y++)for(int z=-9;z<=9;z++){double d=0.5*std::sqrt(double(x*x+y*y+z*z))-2.7;if(std::abs(d)<1.5)t.setValueOn(Coord(x,y,z),T(d));else if(d<0)t.setValueOff(Coord(x,y,z),T(-1.5));}}
    if(scene==6)for(int x=-5;x<=5;x++)for(int y=-3;y<=3;y++)for(int z=-2;z<=2;z++){double d=((x+5)*73+(y+3)*19+(z+2)*11)%29-14;if(((x-y+z)%3)!=0)t.setValueOn(Coord(x,y,z),T(d/7.0));}
    if(scene==7){t.setValueOn(Coord(-1),T(-0.0));t.setValueOn(Coord(2,0,0),T(1.0e12));t.setValueOn(Coord(-2,0,0),T(-1.0e12));t.setValueOn(Coord(0,2,0),T(1.234567890123));t.setValueOn(Coord(0,-2,0),T(-0.23456789));}
    return g;
}
template<class Tree>void snapshot(const Grid<Tree>&g){
    const auto&t=g.tree();out<<"{\"name\":\""<<g.getName()<<"\",\"class\":"<<int(g.getGridClass())<<",\"background\":";number(t.background());out<<",\"activeCount\":"<<t.activeVoxelCount()<<",\"leaves\":"<<t.leafCount()<<",\"tiles\":"<<t.activeTileCount()<<",\"origin\":[";
    Vec3d origin=g.transform().indexToWorld(Vec3d(0));for(int i=0;i<3;i++){if(i)out<<',';number(origin[i]);}
    out<<"],\"voxelSize\":[";auto size=g.voxelSize();for(int i=0;i<3;i++){if(i)out<<',';number(size[i]);}
    out<<"],\"values\":[";bool f=true;for(auto it=t.cbeginValueOn();it;++it){if(!f)out<<',';f=false;out<<'[';coord(it.getCoord());out<<',';number(*it);out<<','<<it.getDepth()<<']';}
    out<<"],\"probes\":[";f=true;for(int i=0;i<257;i++){Coord c((i*13)%33-16,(i*7)%27-13,(i*11)%29-14);if(!f)out<<',';f=false;out<<'[';coord(c);out<<',';number(t.getValue(c));out<<','<<t.isValueOn(c)<<','<<t.getValueDepth(c)<<']';}out<<"]}";
}
template<int Order,class Multi>void sample(const Multi&m){
    bool f=true;double levels[]={0,0.25,0.5,0.9,1,1.5,2};for(double level:levels)for(int i=0;i<12;i++){
        Vec3d p(double(i%5)-2.125,double((i*3)%7)-3.5,double((i*7)%9)-4.25);
        if(!f)out<<',';f=false;out<<'['<<level<<','<<p.x()<<','<<p.y()<<','<<p.z()<<',';number(m.template sampleValue<Order>(p,level));out<<']';
    }
}
template<class T>void run(const char*type){
    using Tree=typename tree::Tree4<T,5,4,3>::Type;using Multi=tools::MultiResGrid<Tree>;
    for(int scene=0;scene<8;scene++)for(int injection=0;injection<2;injection++){
        for(int action=0;action<3;action++){
            auto input=source<T>(scene);Multi m(3,*input,injection!=0);
            if(action==1){m.tree(0).setValueOn(Coord(0),T(15));m.tree(1).setValueOnly(Coord(29),T(42));m.restrictActiveVoxels(1,1);}
            if(action==2){m.tree(1).setValueOn(Coord(0),T(-5));m.tree(0).setValueOnly(Coord(29),T(42));m.prolongateActiveVoxels(0,1);}
            if(!first)out<<',';first=false;out<<"{\"type\":\""<<type<<"\",\"scene\":"<<scene<<",\"injection\":"<<injection<<",\"action\":"<<action<<",\"kind\":\"levels\",\"grids\":[";
            for(int i=0;i<3;i++){if(i)out<<',';snapshot(*m.grid(i));}
            out<<"],\"samples\":[[";sample<0>(m);out<<"],[";sample<1>(m);out<<"],[";sample<2>(m);out<<"]],\"transfers\":[";
            for(int i=0;i<27;i++){if(i)out<<',';Coord c(i%3-1,(i/3)%3-1,i/9-1);out<<'[';coord(c);out<<',';number(m.restrictVoxel(c,1,false));out<<',';number(m.restrictVoxel(c,1,true));out<<',';number(m.prolongateVoxel(c,0));out<<']';}out<<"]}";
        }
        Multi m(3,*source<T>(scene),injection!=0);
        for(int order=0;order<3;order++)for(float level:{0.5f,1.5f}){
            if(!first)out<<',';first=false;out<<"{\"type\":\""<<type<<"\",\"scene\":"<<scene<<",\"injection\":"<<injection<<",\"kind\":\"fraction\",\"order\":"<<order<<",\"level\":"<<level<<",\"grid\":";
            if(order==0)snapshot(*m.template createGrid<0>(level,1));else if(order==1)snapshot(*m.template createGrid<1>(level,1));else snapshot(*m.template createGrid<2>(level,1));out<<'}';
        }
    }
}
template<class T>void contracts(const char* type) {
    using Tree=typename tree::Tree4<T,5,4,3>::Type;using Multi=tools::MultiResGrid<Tree>;
    for(int id=0;id<8;id++){
        std::string error="",name="";auto input=source<T>(0);bool shared=false,inputEmpty=false;
        try{
            if(id==2)input->insertMeta("MultiResGrid_Levels",StringMetadata("conflict"));
            if(id==3)input->insertMeta("MultiResGrid_Level",StringMetadata("conflict"));
            if(id==4)input->insertMeta("MultiResGrid_Level",Int64Metadata(0));
            if(id==5){input->removeMeta("name");input->insertMeta("name",Int32Metadata(123));}
            if(id>=6){input->tree().setValueOn(Coord(0),T(7));auto tree=input->treePtr();if(id==6){Multi m(2,input,false);shared=tree==m.treePtr(0);inputEmpty=input->empty();name=m.grid(0)->getName();}else{Multi m(2,*input,false);shared=tree==m.treePtr(0);inputEmpty=input->empty();name=m.grid(0)->getName();}}
            else {Multi m(id<2?id:2,*input);name=id==4?m.template createGrid<1>(0)->getName():m.grid(0)->getName();}
        }catch(const TypeError&){error="TypeError";}catch(const ValueError&){error="ValueError";}
        if(!first)out<<',';first=false;out<<"{\"kind\":\"contract\",\"type\":\""<<type<<"\",\"scene\":"<<id<<",\"injection\":0,\"error\":\""<<error<<"\",\"name\":\""<<name<<"\",\"shared\":"<<shared<<",\"inputEmpty\":"<<inputEmpty<<'}';
    }
}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
 run<float>("float");run<double>("double");contracts<float>("float");contracts<double>("double");out<<"]}\n";return out.good()?0:3;}