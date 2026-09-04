// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <string>
struct Input { double radius, voxel, width, x,y,z; };
template<typename GridT> void emit(const Input& input,int index,const char* type) {
    using T=typename GridT::ValueType;
    T radius=T(input.radius), voxel=T(input.voxel), width=T(input.width);
    openvdb::math::Vec3<T> center(T(input.x),T(input.y),T(input.z));
    openvdb::tools::LevelSetSphere<GridT> factory(radius,center);
    auto grid=factory.getLevelSet(voxel,width,false);
    std::cout << "{\"name\":\"" << type << '-' << index << "\",\"type\":\"" << type << "\",\"radius\":" << radius << ",\"voxel\":" << voxel << ",\"width\":" << width
       << ",\"center\":[" << center.x() << ',' << center.y() << ',' << center.z() << "],\"background\":" << grid->background() << ",\"activeCount\":" << grid->activeVoxelCount() << ",\"leafCount\":" << grid->tree().leafCount() << ",\"samples\":[";
    double extent=double(radius/voxel+width)+2;
    int minX=int(std::floor(double(center.x()/voxel)-extent)),maxX=int(std::ceil(double(center.x()/voxel)+extent));
    int minY=int(std::floor(double(center.y()/voxel)-extent)),maxY=int(std::ceil(double(center.y()/voxel)+extent));
    int minZ=int(std::floor(double(center.z()/voxel)-extent)),maxZ=int(std::ceil(double(center.z()/voxel)+extent));
    bool first=true;
    for(int x=minX;x<=maxX;++x)for(int y=minY;y<=maxY;++y)for(int z=minZ;z<=maxZ;++z){
      openvdb::Coord coord(x,y,z);T value;bool active=grid->tree().probeValue(coord,value);
      if(!first)std::cout<<',';first=false;
      std::cout << '[' << x << ',' << y << ',' << z << ',' << value << ',' << (active?1:0) << ',' << grid->tree().getValueDepth(coord) << ']';
    }
    std::cout << "]}";
}
int main(){
    openvdb::initialize();std::cout<<std::setprecision(17);
    Input inputs[]={{1.49,1,3,0,0,0},{1.5,1,3,0,0,0},{2,1,2,-.25,.5,1.75},{4.25,.75,2.5,-1.25,2.5,.75},{5,.5,2,-.25,.5,1.75},{5,1,3,0,0,0},{8,1.25,3.5,7.2,-3.1,1.6},{1.1,.1,2.3,-.3,.2,.1}};
    std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    bool first=true;int index=0;for(const auto& input:inputs){if(!first)std::cout<<',';first=false;emit<openvdb::FloatGrid>(input,index,"float");std::cout<<',';emit<openvdb::DoubleGrid>(input,index,"double");++index;}std::cout<<"]}\n";
}
