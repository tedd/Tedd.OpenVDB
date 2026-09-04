// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Native executable used only to generate checked-in managed conformance fixtures.
#include <openvdb/openvdb.h>
#include <openvdb/tools/Prune.h>
#include <openvdb/tools/Morphology.h>
#include <openvdb/tools/SignedFloodFill.h>
#include <algorithm>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <random>
#include <set>
#include <string>
#include <vector>
using Coord = openvdb::Coord;
using Tree = openvdb::FloatTree;
struct Op { std::string op; Coord min, max; float value; bool active; int level; };
struct Case {
    std::string name, operation;
    float background=3, tolerance=0, outside=7, inside=-9;
    int iterations=1, neighbors=6, mode=2, minLevel=0, radius=10;
    std::vector<Op> initial;
    std::vector<Coord> extra;
};
Op fill(Coord a, Coord b, float value, bool active=true) { return {"fill",a,b,value,active,0}; }
Op point(Coord a, float value, bool active=true) { return {"set",a,a,value,active,0}; }
Op tile(Coord a, int level, float value, bool active=true) { return {"tile",a,a,value,active,level}; }
Op touch(Coord a) { return {"touch",a,a,0,false,0}; }
void emitCoord(Coord c) { std::cout << '[' << c.x() << ',' << c.y() << ',' << c.z() << ']'; }
void emitCase(const Case& c) {
    Tree tree(c.background);
    for (const auto& op:c.initial) {
        if(op.op=="fill") tree.fill(openvdb::CoordBBox(op.min,op.max),op.value,op.active);
        else if(op.op=="tile") tree.addTile(op.level,op.min,op.value,op.active);
        else if(op.op=="touch") tree.touchLeaf(op.min);
        else if(op.active) tree.setValueOn(op.min,op.value);
        else tree.setValueOff(op.min,op.value);
    }
    if(c.operation=="prune") openvdb::tools::prune(tree,c.tolerance,false);
    else if(c.operation=="pruneTiles") openvdb::tools::pruneTiles(tree,c.tolerance,false);
    else if(c.operation=="pruneInactive") openvdb::tools::pruneInactive(tree,false);
    else if(c.operation=="pruneInactiveWithValue") openvdb::tools::pruneInactiveWithValue(tree,c.outside,false);
    else if(c.operation=="pruneLevelSet") openvdb::tools::pruneLevelSet(tree,false);
    else if(c.operation=="pruneLevelSetWithValues") openvdb::tools::pruneLevelSet(tree,c.outside,c.inside,false);
    else if(c.operation=="flood") openvdb::tools::signedFloodFill(tree,false,1,c.minLevel);
    else if(c.operation=="floodWithValues") openvdb::tools::signedFloodFillWithValues(tree,c.outside,c.inside,false,1,c.minLevel);
    else if(c.operation=="dilate") openvdb::tools::dilateActiveValues(tree,c.iterations,static_cast<openvdb::tools::NearestNeighbors>(c.neighbors),static_cast<openvdb::tools::TilePolicy>(c.mode),false);
    else if(c.operation=="erode") openvdb::tools::erodeActiveValues(tree,c.iterations,static_cast<openvdb::tools::NearestNeighbors>(c.neighbors),static_cast<openvdb::tools::TilePolicy>(c.mode),false);
    std::cout << "{\"name\":\"" << c.name << "\",\"operation\":\"" << c.operation << "\",\"background\":" << c.background
       << ",\"tolerance\":" << c.tolerance << ",\"outside\":" << c.outside << ",\"inside\":" << c.inside
       << ",\"iterations\":" << c.iterations << ",\"neighbors\":" << c.neighbors << ",\"mode\":" << c.mode << ",\"minLevel\":" << c.minLevel << ",\"initial\":[";
    bool first=true;
    for(const auto& op:c.initial) {
       if(!first)std::cout<<','; first=false;
       std::cout << "{\"op\":\"" << op.op << "\",\"min\":"; emitCoord(op.min); std::cout << ",\"max\":"; emitCoord(op.max);
       std::cout << ",\"value\":" << op.value << ",\"active\":" << (op.active?"true":"false") << ",\"level\":" << op.level << '}';
    }
    std::cout << "],\"resultBackground\":" << tree.background() << ",\"activeCount\":" << tree.activeVoxelCount() << ",\"leafCount\":" << tree.leafCount() << ",\"samples\":[";
    std::set<Coord> samples;
    for(int x=-4;x<=c.radius;++x)for(int y=-4;y<=c.radius;++y)for(int z=-4;z<=c.radius;++z)samples.insert(Coord(x,y,z));
    for(const auto& op:c.initial) {
      for(int d=-1;d<=1;++d) { samples.insert(op.min+Coord(d)); samples.insert(op.max+Coord(d)); }
    }
    for(auto v:c.extra)samples.insert(v);
    first=true;
    for(auto coord:samples) {
       if(!first)std::cout<<','; first=false;
       float value; bool active=tree.probeValue(coord,value);
       std::cout << '[' << coord.x() << ',' << coord.y() << ',' << coord.z() << ',' << value << ',' << (active?1:0) << ',' << tree.getValueDepth(coord) << ']';
    }
    std::cout << "]}";
}
int main() {
    openvdb::initialize(); std::cout << std::setprecision(17);
    std::vector<Case> cases;
    for(const auto& operation:{"prune","pruneTiles","pruneInactive","pruneInactiveWithValue","pruneLevelSet","pruneLevelSetWithValues"}) {
       Case c; c.name=std::string(operation)+"-mixed"; c.operation=operation;
       c.initial={fill(Coord(0),Coord(7),-2,false),point(Coord(1),8,false),point(Coord(9,1,1),1,true),tile(Coord(16,0,0),1,-4,false)};
       cases.push_back(c);
       c.name=std::string(operation)+"-all-inactive";
       c.initial={fill(Coord(0),Coord(7),-2,false),point(Coord(1),8,false)};
       cases.push_back(c);
    }
    for(int count:{254,256,258}) {
       Case c; c.name="prune-lower-median-"+std::to_string(count); c.operation="prune"; c.tolerance=2;
       c.initial={fill(Coord(0),Coord(7),12,true)};
       for(int i=0;i<count;++i)c.initial.push_back(point(Coord(i>>6,(i>>3)&7,i&7),10,true));
       cases.push_back(c);
    }
    { Case c;c.name="prune-range-not-first";c.operation="prune";c.tolerance=1;c.initial={fill(Coord(0),Coord(7),10),point(Coord(1),9),point(Coord(7),11)};cases.push_back(c); }
    { Case c;c.name="pruneTiles-leaves-retained";c.operation="pruneTiles";c.initial={fill(Coord(0),Coord(127),3),touch(Coord(0))};cases.push_back(c); }
    for(int minLevel=0;minLevel<=4;++minLevel) for(const auto& operation:{"flood","floodWithValues"}) {
       Case c;c.name=std::string(operation)+"-scanline-level-"+std::to_string(minLevel);c.operation=operation;c.minLevel=minLevel;
       c.initial={point(Coord(0,0,1),-0.5f),point(Coord(0,0,6),0.5f),point(Coord(0,3,0),-0.2f),point(Coord(3,0,0),0.2f),point(Coord(7),-1),point(Coord(9,1,1),1),tile(Coord(16,0,0),1,13,true)};
       cases.push_back(c);
    }
    for(const auto& operation:{"flood","floodWithValues"}) {
       Case c;c.name=std::string(operation)+"-inactive-negative-leaf";c.operation=operation;c.initial={fill(Coord(0),Coord(7),-2,false),touch(Coord(0))};cases.push_back(c);
       c.name=std::string(operation)+"-root-gap";c.initial={fill(Coord(0),Coord(7),-2,false),touch(Coord(0)),fill(Coord(0,0,8192),Coord(7,7,8199),-2,false),touch(Coord(0,0,8192)),tile(Coord(0,0,4096),3,77,true)};
       c.extra={Coord(0,0,4096),Coord(1,2,6000),Coord(0,0,8191),Coord(0,0,8192)};cases.push_back(c);
       c.name=std::string(operation)+"-empty-negative-background";c.initial.clear();c.background=-3;cases.push_back(c);
       c.name=std::string(operation)+"-tile-only";c.initial={tile(Coord(0),3,-2,false)};cases.push_back(c);
    }
    { Case c;c.name="flood-closed-sphere";c.operation="flood";c.radius=12;
      for(int x=-12;x<=12;++x)for(int y=-12;y<=12;++y)for(int z=-12;z<=12;++z){float d=std::sqrt(float(x*x+y*y+z*z))-10;if(std::abs(d)<=1)c.initial.push_back(point(Coord(x,y,z),d));}cases.push_back(c); }
    for(const auto& operation:{"dilate","erode"})for(int neighbors:{6,18,26})for(int mode=0;mode<3;++mode)for(int shape=0;shape<3;++shape) {
       Case c;c.name=std::string(operation)+"-"+std::to_string(neighbors)+"-"+std::to_string(mode)+"-shape-"+std::to_string(shape);c.operation=operation;c.neighbors=neighbors;c.mode=mode;c.iterations=shape==0?2:1;
       if(shape==0)c.initial={point(Coord(0),8),point(Coord(7),9),point(Coord(-1,2,1),-5,false)};
       else if(shape==1)c.initial={tile(Coord(0),1,8,true),point(Coord(-1,2,2),9,true),point(Coord(8,2,2),-5,false)};
       else { c.initial={fill(Coord(0),Coord(7),8,true)};std::mt19937 r(1729);for(int i=0;i<16;++i)c.initial.push_back(point(Coord(r()%8,r()%8,r()%8),float(i),false)); }
       cases.push_back(c);
    }
    for(int mode=0;mode<3;++mode) { Case c;c.name="erode-thin-plane-"+std::to_string(mode);c.operation="erode";c.mode=mode;c.initial={fill(Coord(0),Coord(7,7,0),-2,true)};cases.push_back(c); }
    std::cout << "{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    bool first=true;for(const auto& c:cases){if(!first)std::cout<<',';first=false;emitCase(c);}std::cout<<"]}\n";
}
