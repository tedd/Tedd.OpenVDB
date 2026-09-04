// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main mesh_distance_oracle_main
#include "mesh-to-volume-oracle.cpp"
#undef main
template<class Grid>void helper(int style,int scene,int mode,float ex,float in) {
    using T=typename Grid::ValueType;Mesh m=mesh(scene);
    auto transform=math::Transform::createLinearTransform(mode?0.75:1);
    if(mode==2){transform->postRotate(0.31,math::Z_AXIS);transform->postTranslate(Vec3d(0.5,-1,0.75));}
    if(mode==3)transform->postScale(Vec3d(1,1.5,0.5));
    std::vector<Vec3s>points;for(auto p:m.points)points.emplace_back(p);std::vector<Vec3I>triangles;std::vector<Vec4I>quads;
    for(auto f:m.faces){if(f[3]==util::INVALID_IDX)triangles.emplace_back(f[0],f[1],f[2]);else quads.push_back(f);}
    typename Grid::Ptr result;std::string error;
    try{
        if(style==0){tools::QuadAndTriangleDataAdapter<Vec3s,Vec4I> adapter(points,m.faces);result=tools::meshToVolume<Grid>(adapter,*transform,ex,in);}
        else if(style==1)result=tools::createLevelSetBox<Grid>(BBoxd(Vec3d(-2,-1.5,-2.25),Vec3d(2.75,2.5,1.75)),*transform,double(ex));
        else if(style==2)result=tools::meshToSignedDistanceField<Grid>(*transform,points,triangles,quads,ex,in);
        else if(style==3)result=tools::meshToUnsignedDistanceField<Grid>(*transform,points,triangles,quads,ex);
        else if(style==4)result=tools::meshToLevelSet<Grid>(*transform,points,triangles,quads,ex);
        else {
            result=Grid::create(T(100));
            for(int x=0;x<16;x++)for(int y=0;y<8;y++)for(int z=0;z<8;z++){
                Coord c(x-8,y,z);bool barrier=x==1||x==14||y==1||y==6||z==1||z==6;
                if(scene==1&&x==1&&y==3&&z==3)barrier=false;
                T v=barrier?T(0.5):T(100);if(scene==2&&x==5&&y==3&&z==3)v=T(-100);
                if(scene==3&&x==7)continue;
                result->tree().setValueOff(c,v);if((x*64+y*8+z)%7==0)result->tree().setActiveState(c,true);
            }
            result->tree().addTile(1,Coord(32),T(4),true);
            tools::traceExteriorBoundaries(result->tree());
        }
    }catch(const openvdb::Exception&e){error=e.what();}
    if(!first)out<<',';first=false;out<<"{\"type\":\""<<(std::is_same_v<T,float>?"float":"double")<<"\",\"style\":"<<style<<",\"scene\":"<<scene<<",\"mode\":"<<mode<<",\"exterior\":";num(ex);out<<",\"interior\":";num(in);
    out<<",\"points\":[";for(size_t i=0;i<points.size();i++){if(i)out<<',';vec(points[i]);}out<<"],\"triangles\":[";
    for(size_t i=0;i<triangles.size();i++){if(i)out<<',';vec(triangles[i]);}out<<"],\"quads\":[";
    for(size_t i=0;i<quads.size();i++){if(i)out<<',';auto f=quads[i];out<<'['<<f[0]<<','<<f[1]<<','<<f[2]<<','<<f[3]<<']';}
    out<<"],\"error\":";if(!error.empty()){out<<"true}";return;}out<<"false,\"background\":";num(result->background());
    out<<",\"class\":"<<int(result->getGridClass())<<",\"active\":"<<result->activeVoxelCount()<<",\"leaves\":";leaves(result->tree());
    out<<",\"probes\":[";bool pf=true;for(int x:{-16,-8,-4,0,4,8,16,32})for(int y:{0,3,32})for(int z:{0,3,32}){Coord c(x,y,z);if(!pf)out<<',';pf=false;out<<'['<<x<<','<<y<<','<<z<<',';num(result->tree().getValue(c));out<<','<<result->tree().isValueOn(c)<<','<<result->tree().getValueDepth(c)<<']';}out<<"]}";
}
template<class Grid>void all(){
    for(auto bands:std::vector<std::pair<float,float>>{{0,3},{3,0},{-1,3},{3,-1},{0,0},{INFINITY,3},{3,NAN},{3,INFINITY}})helper<Grid>(0,0,0,bands.first,bands.second);
    for(int mode=0;mode<4;mode++){
        for(float width:{0.5f,3.f,5.f})helper<Grid>(1,0,mode,width,width);
        for(int style:{2,3,4})for(int scene:{0,2,8})helper<Grid>(style,scene,mode,3,4);
    }
    for(int scene=0;scene<4;scene++)helper<Grid>(5,scene,0,3,3);
}
int main(int argc,char**argv){if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";all<FloatGrid>();all<DoubleGrid>();out<<"]}\n";return out.good()?0:3;}
