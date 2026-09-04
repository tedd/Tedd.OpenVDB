// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tools/FindActiveValues.h>
#include <openvdb/tools/Count.h>
#include <tbb/global_control.h>
#include <fstream>
#include <vector>
#include <iomanip>
using namespace openvdb;
std::ofstream output;
void coord(const Coord&c){output<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<']';}
void box(const CoordBBox&b){output<<'[';coord(b.min());output<<',';coord(b.max());output<<']';}
Int32Tree create(int scene){
    Int32Tree t(23);
    if(scene==1){t.setValueOn(Coord(0),1);t.setValueOn(Coord(2),2);t.setValueOn(Coord(7),3);t.setValueOff(Coord(4),40);}
    if(scene==2){t.addLeaf(new Int32Tree::LeafNodeType(Coord(0),7,true));t.setValueOff(Coord(3),-10);}
    if(scene==3){t.addTile(3,Coord(-4096,0,0),-5,true);t.addTile(3,Coord(4096,0,0),-2,false);t.addTile(2,Coord(0),8,true);t.addTile(1,Coord(128,0,0),9,true);t.setValueOn(Coord(256,0,0),10);t.setValueOn(Coord(256,2,2),11);t.setValueOn(Coord(8192,7,7),12);}
    if(scene==4){t.addTile(3,Coord(0),7,true);t.setValueOff(Coord(0),-10);}
    if(scene==5){t.fill(CoordBBox(Coord(-11,-9,-7),Coord(15,17,19)),4,true);t.fill(CoordBBox(Coord(-2),Coord(3)),0,false);t.addTile(1,Coord(32),-20,false);}
    if(scene==6){for(int i=0;i<256;i++){Coord c((i*47)%313-150,(i*13)%101-50,(i*31)%191-90);t.setValueOn(c,i-100);}t.addTile(2,Coord(256),51,true);t.addTile(1,Coord(-256),52,true);t.addTile(3,Coord(-8192),53,true);}
    return t;
}
std::vector<CoordBBox> queries(){
    std::vector<CoordBBox> b;
    for(int extent:{0,1,3,7,8,15,127,128,4095})for(Coord p:std::vector<Coord>{Coord(0),Coord(1),Coord(2),Coord(7),Coord(-8),Coord(125,0,0),Coord(256,1,1)})
        b.emplace_back(p,p.offsetBy(extent));
    for(int x:{-8193,-4097,-4096,-1,0,7,8,127,128,255,256,4095,4096,8192})b.emplace_back(Coord(x,-1,-1),Coord(x+2,8,8));
    b.emplace_back(Coord(-5000),Coord(9000));b.emplace_back(Coord(0),Coord(4095));b.emplace_back(Coord(0),Coord(127));b.push_back(CoordBBox::inf());
    return b;
}
int main(int argc,char**argv){
    if(argc!=2)return 2;initialize();tbb::global_control serial(tbb::global_control::max_allowed_parallelism,1);
    output.open(argv[1]);output<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    bool first=true;
    for(int scene=0;scene<7;scene++){
        auto tree=create(scene);tools::FindActiveValues<Int32Tree> finder(tree);
        for(const auto& region:queries()){
            if(!first)output<<',';first=false;auto extrema=tools::minMax(tree,false);
            output<<"{\"scene\":"<<scene<<",\"bounds\":";box(region);
            output<<",\"any\":"<<finder.anyActiveValues(region)<<",\"cachedAny\":"<<finder.anyActiveValues(region,true)<<",\"voxels\":"<<finder.anyActiveVoxels(region)<<",\"tiles\":"<<finder.anyActiveTiles(region)<<",\"none\":"<<finder.noActiveValues(region);
            output<<",\"nativeCachedCount\":"<<finder.count(region)<<",\"count\":"<<tools::countActiveValues(tree,region)<<",\"leafCount\":"<<tools::countActiveLeafVoxels(tree,region,false);
            output<<",\"totals\":["<<tools::countActiveVoxels(tree,false)<<','<<tools::countActiveLeafVoxels(tree,false)<<','<<tools::countInactiveVoxels(tree,false)<<','<<tools::countInactiveLeafVoxels(tree,false)<<','<<tools::countActiveTiles(tree,false)<<','<<extrema.min()<<','<<extrema.max()<<"],\"activeTiles\":[";
            bool f=true;for(auto tile:finder.activeTiles(region)){if(!f)output<<',';f=false;output<<"{\"bounds\":";box(tile.bbox);output<<",\"value\":"<<tile.value<<",\"level\":"<<tile.level<<",\"active\":"<<tile.state<<'}';}
            output<<"]}";
        }
    }
    output<<"]}\n";return output.good()?0:3;
}
