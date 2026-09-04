// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/Filter.h>
#include <iomanip>
#include <iostream>
using Half=openvdb::math::half;
using Tree=typename openvdb::tree::Tree4<Half,5,4,3>::Type;
using Grid=openvdb::Grid<Tree>;
int main(){openvdb::initialize();std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
 for(int operation=0;operation<4;operation++)for(int maskType=0;maskType<3;maskType++){
 auto grid=Grid::create(Half(0.125f));auto& tree=grid->tree();
 tree.setValueOn(openvdb::Coord(0,0,0),Half(5));tree.setValueOn(openvdb::Coord(1,0,0),Half(-1.337f));tree.setValueOn(openvdb::Coord(0,1,0),Half(19.03f));tree.setValueOn(openvdb::Coord(0,0,1),Half(0.12597656f));
 tree.setValueOn(openvdb::Coord(7,0,0),Half(3.14159f));tree.setValueOn(openvdb::Coord(-1,0,0),Half(7.777f));tree.setValueOff(openvdb::Coord(2,0,0),Half(6.28125f));tree.addTile(1,openvdb::Coord(16,0,0),Half(2.71875f),true);
 auto mask=openvdb::FloatGrid::create(0.3712345f);mask->tree().setValueOff(openvdb::Coord(1,0,0),0.8234567f);
 openvdb::tools::Filter<Grid> filter(*grid);filter.setGrainSize(0);filter.setProcessTiles(operation==3);filter.invertMask(maskType==2);
 const auto* maskPtr=maskType==0?nullptr:mask.get();
 switch(operation){case 0:filter.mean(1,2,maskPtr);break;case 1:filter.gaussian(1,1,maskPtr);break;case 2:filter.median(1,2,maskPtr);break;case 3:filter.offset(Half(1.337f),maskPtr);break;}
 if(!first)std::cout<<',';first=false;std::cout<<"{\"name\":\"half-filter-"<<operation<<'-'<<maskType<<"\",\"operation\":"<<operation<<",\"mask\":"<<maskType<<",\"samples\":[";bool f=true;
 for(int x=-2;x<=17;x++)for(int y=-1;y<=2;y++)for(int z=-1;z<=2;z++){if(!f)std::cout<<',';f=false;openvdb::Coord c(x,y,z);Half value;bool active=tree.probeValue(c,value);std::cout<<'['<<x<<','<<y<<','<<z<<','<<float(value)<<','<<(active?1:0)<<','<<tree.getValueDepth(c)<<']';}std::cout<<"]}";
 }std::cout<<"]}\n";}
