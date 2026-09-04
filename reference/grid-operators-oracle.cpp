// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main unusedMathOracleMain
#include "math-oracle.cpp"
#undef main
#include <openvdb/tools/GridOperators.h>
template<class T>void gridValue(const T& v){number(double(v));}
template<class T>void gridValue(const Vec3<T>& v){emit(Vec3d(v));}
Mat4d gridMatrix(){return Mat4d(1.3,0.2,-0.1,0.0,-0.3,0.8,0.25,0.0,0.1,-0.15,1.7,0.0,3.0,-2.0,4.0,1.0);}
template<class G>void output(const G& g,int scenario,int mapKind,int precision,int operation,bool masked){
    begin("grid");field("scenario",double(scenario));field("map",double(mapKind));field("precision",double(precision));field("operation",double(operation));field("masked",double(masked));
    key("background");gridValue(g.background());field("activeCount",double(g.activeVoxelCount()));field("leafCount",double(g.tree().leafCount()));
    key("vectorType");std::cout<<'\"'<<openvdb::GridBase::vecTypeToString(g.getVectorType())<<'\"';
    field("class",double(g.getGridClass()));key("name");std::cout<<'\"'<<g.getName()<<'\"';
    key("values");std::cout<<'[';bool first=true;
    for(auto iter=g.cbeginValueOn();iter;++iter){if(!first)std::cout<<',';first=false;std::cout<<'[';emit(iter.getCoord().asVec3d());std::cout<<',';number(double(iter.getLevel()));std::cout<<',';gridValue(*iter);std::cout<<']';}std::cout<<']';
    key("probes");std::cout<<'[';
    const openvdb::Coord probes[]={openvdb::Coord(-1,0,0),openvdb::Coord(0),openvdb::Coord(1),openvdb::Coord(7),openvdb::Coord(8),openvdb::Coord(4096),openvdb::Coord(-4096),openvdb::Coord(0,2,0)};
    for(int i=0;i<8;++i){if(i)std::cout<<',';std::cout<<'[';emit(probes[i].asVec3d());std::cout<<',';gridValue(g.tree().getValue(probes[i]));std::cout<<',';number(double(g.tree().isValueOn(probes[i])));std::cout<<',';number(double(g.tree().getValueDepth(probes[i])));std::cout<<']';}
    std::cout<<']';end();
}
template<class T>void gridCases(){
    using S=openvdb::Grid<typename openvdb::tree::Tree4<T,5,4,3>::Type>;
    using V=openvdb::Grid<typename openvdb::tree::Tree4<Vec3<T>,5,4,3>::Type>;
    for(int scenario=0;scenario<5;++scenario)for(int mapKind=0;mapKind<2;++mapKind){
        auto scalar=S::create(T(3));auto vector=V::create(Vec3<T>(3,-4,0));
        if(scenario<3){
            if(scenario==0){
                for(int x=-1;x<=1;++x)for(int y=-1;y<=1;++y)for(int z=-1;z<=1;++z){
                    scalar->tree().setValue(openvdb::Coord(x,y,z),T(x*x+2*y*y+3*z*z+x*y+2));
                    vector->tree().setValue(openvdb::Coord(x,y,z),Vec3<T>(T(x*x-y),T(y*y+z),T(z*z+x)));
                }
            }else{
                scalar->tree().addTile(1,openvdb::Coord(0),T(2),true);vector->tree().addTile(1,openvdb::Coord(0),Vec3<T>(2,1,-3),true);
                scalar->tree().setValueOff(openvdb::Coord(-1,0,0),T(19));vector->tree().setValueOff(openvdb::Coord(-1,0,0),Vec3<T>(19,2,-4));
            }
        }
        if(scenario==4){scalar->tree().addTile(3,openvdb::Coord(4096),T(5),false);vector->tree().addTile(3,openvdb::Coord(4096),Vec3<T>(5,0,0),false);}
        openvdb::math::MapBase::Ptr map(new AffineMap(gridMatrix()));
        if(mapKind==1)map.reset(new NonlinearFrustumMap(openvdb::BBoxd(Vec3d(-4,-8,-3),Vec3d(8,10,12)),.6,5,map));
        scalar->setTransform(Transform::Ptr(new Transform(map->copy())));vector->setTransform(Transform::Ptr(new Transform(map->copy())));
        scalar->setName("input");scalar->setGridClass(openvdb::GRID_LEVEL_SET);vector->setName("vector input");vector->setVectorType(openvdb::VEC_COVARIANT);
        if(scenario==2)vector->setGridClass(openvdb::GRID_STAGGERED);
        auto mask=openvdb::MaskGrid::create();mask->tree().setValueOn(openvdb::Coord(0));mask->tree().setValueOn(openvdb::Coord(7,0,0));mask->tree().setValueOn(openvdb::Coord(-1,0,0));
        mask->setTransform(Transform::createLinearTransform(43));
        for(int masked=0;masked<2;++masked){
#define OUT(OP,EXPR) output(*(EXPR),scenario,mapKind,sizeof(T),OP,bool(masked))
            OUT(0,masked?openvdb::tools::gradient(*scalar,*mask,false):openvdb::tools::gradient(*scalar,false));
            OUT(1,masked?openvdb::tools::laplacian(*scalar,*mask,false):openvdb::tools::laplacian(*scalar,false));
            OUT(2,masked?openvdb::tools::meanCurvature(*scalar,*mask,false):openvdb::tools::meanCurvature(*scalar,false));
            OUT(3,masked?openvdb::tools::cpt(*scalar,*mask,false):openvdb::tools::cpt(*scalar,false));
            OUT(4,masked?openvdb::tools::divergence(*vector,*mask,false):openvdb::tools::divergence(*vector,false));
            OUT(5,masked?openvdb::tools::curl(*vector,*mask,false):openvdb::tools::curl(*vector,false));
            OUT(6,masked?openvdb::tools::magnitude(*vector,*mask,false):openvdb::tools::magnitude(*vector,false));
            OUT(7,masked?openvdb::tools::normalize(*vector,*mask,false):openvdb::tools::normalize(*vector,false));
#undef OUT
        }
    }
}
int main(){openvdb::initialize();std::cout<<std::setprecision(17);std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    gridCases<float>();gridCases<double>();std::cout<<"]}";openvdb::uninitialize();return 0;}
