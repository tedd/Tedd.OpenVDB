// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main mesh_distance_oracle_main
#include "mesh-to-volume-oracle.cpp"
#undef main
void edgeData(const tools::MeshToVoxelEdgeData::EdgeData& d) {out<<'[';num(d.mXDist);out<<',';num(d.mYDist);out<<',';num(d.mZDist);out<<','<<int32_t(d.mXPrim)<<','<<int32_t(d.mYPrim)<<','<<int32_t(d.mZPrim)<<']';}
int main(int argc,char**argv) {
    if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);out.open(argv[1]);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    for(int scene=0;scene<=8;scene++)for(int mode=0;mode<6;mode++){
        Mesh m=mesh(scene);std::vector<Vec3s> points;
        for(auto p:m.points) {
            if(mode==1)p+=Vec3d(7.75,-8.25,15.375);
            else if(mode==2)p=Vec3d(p.z(),p.x(),p.y())*0.625;
            else if(mode==3)p=Vec3d(std::round(p.x()),std::round(p.y()),std::round(p.z()));
            else if(mode==4)p=p*0.03125+Vec3d(-0.125,0.1875,0.5);
            else if(mode==5)p=p*0.7+Vec3d(128.1,-128.3,-0.33);
            points.emplace_back(p);
        }
        if(mode==4){auto copy=m.faces;m.faces.insert(m.faces.end(),copy.begin(),copy.end());}
        tools::MeshToVoxelEdgeData converter;converter.convert(points,m.faces);auto acc=converter.getAccessor();
        if(!first)out<<',';first=false;out<<"{\"scene\":"<<scene<<",\"mode\":"<<mode<<",\"points\":[";
        for(size_t i=0;i<points.size();i++){if(i)out<<',';vec(points[i]);}out<<"],\"faces\":[";
        for(size_t i=0;i<m.faces.size();i++){if(i)out<<',';const auto& f=m.faces[i];out<<'['<<f[0]<<','<<f[1]<<','<<f[2]<<','<<int32_t(f[3])<<']';}
        out<<"],\"active\":"<<acc.tree().activeVoxelCount()<<",\"leaves\":[";
        bool leafFirst=true;for(auto leaf=acc.tree().cbeginLeaf();leaf;++leaf){if(!leafFirst)out<<',';leafFirst=false;out<<"{\"origin\":";vec(leaf->origin());out<<",\"values\":[";
            for(int offset=0;offset<512;offset++){if(offset)out<<',';out<<'['<<leaf->isValueOn(offset)<<',';edgeData(leaf->getValue(offset));out<<']';}out<<"]}";}
        out<<"],\"cells\":[";bool cellFirst=true;
        std::vector<Coord> cells;for(auto it=acc.tree().cbeginValueOn();it;++it){if(it.isVoxelValue()){cells.push_back(it.getCoord());cells.push_back(it.getCoord()-Coord(1));}}
        std::sort(cells.begin(),cells.end());cells.erase(std::unique(cells.begin(),cells.end()),cells.end());
        size_t stride=std::max(size_t(1),cells.size()/50);cells.push_back(Coord(10000));
        for(size_t i=0;i<cells.size();i+=stride){const auto& c=cells[i];std::vector<Vec3d> p{Vec3d(999)};std::vector<Index32> ids{1234};converter.getEdgeData(acc,c,p,ids);
            if(!cellFirst)out<<',';cellFirst=false;out<<"{\"coordinate\":";vec(c);out<<",\"points\":[";
            for(size_t j=0;j<p.size();j++){if(j)out<<',';vec(p[j]);}out<<"],\"primitives\":[";
            for(size_t j=0;j<ids.size();j++){if(j)out<<',';out<<int32_t(ids[j]);}out<<"]}";
        }
        out<<"]}";
    }
    out<<"]}\n";return out.good()?0:3;
}
