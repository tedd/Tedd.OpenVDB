// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/tree/NodeManager.h>
#include <fstream>
#include <iostream>
#include <vector>
using Tree=openvdb::Int32Tree; using C=openvdb::Coord;
void setup(Tree& t,int seed) {
    if(seed==0)return;
    t.addTile(3,C(-4096,0,0),seed+3,true); t.addTile(3,C(4096,0,0),seed+5,false);
    t.addTile(2,C(0,0,0),seed+7,(seed&1)!=0);
    for(int i=0;i<seed+2;i++) {
        C c((i%3)*4096+128*(i/3),-128*(i%2),8*(i%5));
        t.setValueOn(c,seed*20+i); t.setValueOff(c+C(1,0,0),-seed-i);
    }
}
void mix(uint64_t&h,int64_t v){h^=uint64_t(v);h*=1099511628211ULL;}
struct Recorder {
    std::ostream& out; int mode; bool& first;
    template<class Node> void record(Node& node) const {
        if(!first)out<<',';first=false; auto c=node.origin();
        uint64_t hash=14695981039346656037ULL,count=0,on=0;
        for(auto it=node.beginValueAll();it;++it) {
            if(mode==1 && !it.isValueOn())it.setValue(*it+Node::LEVEL+1);
            C p=it.getCoord();mix(hash,p.x());mix(hash,p.y());mix(hash,p.z());mix(hash,*it);mix(hash,it.isValueOn());++count;if(it.isValueOn())++on;
        }
        out<<'['<<Node::LEVEL<<','<<c.x()<<','<<c.y()<<','<<c.z()<<','<<node.childCount()<<','<<count<<','<<on<<','<<hash<<']';
    }
    template<class Node> void operator()(Node& node) const {record(node);}
    template<class Node> bool operator()(Node& node,size_t index) const {
        if(mode==4) {
            if constexpr(Node::LEVEL==3) node.touchLeaf(C(16384,0,0));
            else if constexpr(Node::LEVEL>0) node.touchLeaf(node.origin()+C(8,16,24));
        }
        record(node);
        return mode==1? Node::LEVEL!=3 : mode==2? index%2==0 : mode==3? Node::LEVEL!=1 : true;
    }
};
template<unsigned Levels> void run(std::ostream&out,int seed,int kind,int mode) {
    Tree t(seed+1);setup(t,seed);out<<"{\"seed\":"<<seed<<",\"levels\":"<<Levels<<",\"kind\":"<<kind<<",\"mode\":"<<mode<<",\"nodes\":[";
    bool firstNode=true; Recorder op{out,mode,firstNode};
    if(kind==2){openvdb::tree::DynamicNodeManager<Tree,Levels> manager(t);manager.foreachTopDown(op,false,1);}
    else{openvdb::tree::NodeManager<Tree,Levels> manager(t);if(kind==0)manager.foreachTopDown(op,false,1);else manager.foreachBottomUp(op,false,1);}
    out<<"],\"active\":"<<t.activeVoxelCount()<<",\"counts\":[";auto counts=t.nodeCount();for(size_t i=0;i<counts.size();i++){if(i)out<<',';out<<counts[i];}out<<"]}";
}
int main(int argc,char**argv){try{if(argc!=2)return 2;openvdb::initialize();std::ofstream out(argv[1]);out<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";bool first=true;
for(int seed=0;seed<8;seed++)for(int levels=0;levels<=3;levels++)for(int kind=0;kind<3;kind++)for(int mode=0;mode<(kind==2?5:2);mode++){
if(!first)out<<',';first=false;
if(levels==0)run<0>(out,seed,kind,mode);else if(levels==1)run<1>(out,seed,kind,mode);else if(levels==2)run<2>(out,seed,kind,mode);else run<3>(out,seed,kind,mode);
}out<<"]}";return out?0:3;}catch(std::exception&e){std::cerr<<e.what()<<'\n';return 1;}}
