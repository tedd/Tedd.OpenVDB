// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main unusedPcgOracleMain
#include "pcg-oracle.cpp"
#undef main
#include <openvdb/openvdb.h>
#include <openvdb/tools/PoissonSolver.h>
#include <openvdb/tools/Morphology.h>
namespace poisson = openvdb::tools::poisson;
using openvdb::Coord;
struct Boundary {
    int mode;
    void operator()(const Coord& c,const Coord& n,double& source,double& diagonal) const {
        if(mode==0) { diagonal-=1;return; }
        if(mode==1) { int axis=n.x()!=c.x()?0:n.y()!=c.y()?1:2;double sign=n[axis]<c[axis]?-1:1;source=source*1.01+(axis+1)*.125+sign*.01;diagonal-=1+(axis+1)*.125;return; }
        if(mode==2) { if(n.x()==c.x()&&n.z()==c.z()) { if(n.y()<c.y())source-=1;else diagonal-=1; } }
    }
};
template<class T> using Tree = typename openvdb::tree::Tree4<T,5,4,3>::Type;
template<class T> void input(int profile,Tree<T>& source,openvdb::BoolTree& domain) {
    auto set=[&](Coord c){domain.setValue(c,true);source.setValue(c,T((c.x()*3-c.y()*2+c.z())/7.0));};
    if(profile==0) { source.setValueOff(Coord(-100,2,3),T(11));domain.setValueOff(Coord(4096,0,0),false);return; }
    if(profile==1) { set(Coord(1,-2,3));return; }
    if(profile==2) { for(Coord c:{Coord(-4097,-129,-9),Coord(-4096,-128,-8),Coord(-9,0,9),Coord(-8,0,8),Coord(-1,0,1),Coord(0,0,0),Coord(1,0,-1),Coord(7,0,0),Coord(8,0,0),Coord(127,0,0),Coord(128,0,0),Coord(4095,0,0),Coord(4096,0,0)})set(c);return; }
    if(profile==4) { auto box=openvdb::CoordBBox(Coord(-8),Coord(-1));source.fill(box,T(.75),true);domain.fill(box,true,true);source.setValueOff(Coord(2,1,1),T(11));return; }
    if(profile==7) { for(int x=-4;x<=4;x+=2)for(int y=-2;y<=2;y+=2)for(int z=-2;z<=2;z+=2)set(Coord(x,y,z));return; }
    int xmin=profile==6?0:-2,xmax=profile==6?2:profile==3?3:2;
    int ymin=profile==6?-5:profile==3?-3:-2,ymax=profile==6?-1:2;
    int zmin=profile==6?0:profile==3?-1:-2,zmax=profile==6?2:profile==3?4:2;
    for(int x=xmin;x<=xmax;++x)for(int y=ymin;y<=ymax;++y)for(int z=zmin;z<=zmax;++z) {
        Coord c(x,y,z);domain.setValue(c,true);
        if(profile==5) { if((x+y+z)%3==0)source.setValueOff(c,T(5.5));else if((x+y+z)%3==1)source.setValue(c,T(-.25)); }
        else if(profile==6)source.setValue(c,T(0));else set(c);
    }
    if(profile==5)domain.setValueOff(Coord(0),false);
}
template<class T> void poissonCase(int profile,int mode,bool staggered,bool jacobi,int maximum) {
    Tree<T> source(T(1.25));openvdb::BoolTree domain(false);input<T>(profile,source,domain);
    auto index=poisson::createIndexTree(domain);auto b=poisson::createVectorFromTree<double>(source,*index);
    openvdb::BoolTree interior(*index,false,openvdb::TopologyCopy());openvdb::tools::erodeActiveValues(interior,1,openvdb::tools::NN_FACE,openvdb::tools::IGNORE_TILES);
    auto laplacian=poisson::createISLaplacianWithBoundaryConditions(*index,interior,Boundary{mode},*b,staggered);
    start("poisson",precision<T>());field("profile",profile);field("mode",mode);field("staggered",staggered);field("jacobi",jacobi);field("maximum",maximum);
    field("indexBackground",index->background());field("leafCount",index->leafCount());field("rootEntries",index->root().getTableSize());field("activeCount",index->activeVoxelCount());
    std::cout<<",\"index\":[";bool f=true;for(auto l=index->cbeginLeaf();l;++l)for(auto it=l->cbeginValueOn();it;++it) { if(!f)std::cout<<',';f=false;Coord c=it.getCoord();std::cout<<'['<<c.x()<<','<<c.y()<<','<<c.z()<<','<<*it<<']'; }std::cout<<']';
    std::cout<<",\"source\":";vector(*poisson::createVectorFromTree<double>(source,*index));
    std::cout<<",\"matrix\":";::matrix(*laplacian);std::cout<<",\"modifiedSource\":";vector(*b);
    auto packed=poisson::createVectorFromTree<float>(source,*index);auto rebuilt=poisson::createTreeFromVector(*packed,*index,T(-17));
    std::cout<<",\"roundTripFloat\":";vector(*poisson::createVectorFromTree<double>(*rebuilt,*index));field("roundTripBackground",rebuilt->background());
    p::State limit=p::terminationDefaults<T>();limit.iterations=maximum;limit.relativeError=1e-9;limit.absoluteError=1e-11;
    field("limitRelative",limit.relativeError);field("limitAbsolute",limit.absoluteError);
    openvdb::util::NullInterrupter interrupter;typename Tree<T>::Ptr solution;
    if(jacobi)solution=poisson::solveWithBoundaryConditionsAndPreconditioner<p::JacobiPreconditioner<poisson::LaplacianMatrix>>(source,domain,Boundary{mode},limit,interrupter,staggered);
    else solution=poisson::solveWithBoundaryConditionsAndPreconditioner<p::IncompleteCholeskyPreconditioner<poisson::LaplacianMatrix>>(source,domain,Boundary{mode},limit,interrupter,staggered);
    field("success",limit.success);field("iterations",limit.iterations);field("relative",limit.relativeError);field("absolute",limit.absoluteError);field("outputBackground",solution->background());
    std::cout<<",\"solution\":";vector(*poisson::createVectorFromTree<double>(*solution,*index));std::cout<<'}';
}
template<class T> void conversionCase(const char* target) {
    double inputValues[]={0.,-0.,1.0004882812500002,-1.0004882812500002,65519.99999999,std::numeric_limits<double>::quiet_NaN(),std::numeric_limits<double>::infinity(),-std::numeric_limits<double>::infinity()};
    openvdb::BoolTree domain(false);for(int i=0;i<8;++i)domain.setValue(Coord(0,0,i),true);
    auto index=poisson::createIndexTree(domain);p::Vector<double> input(8);for(int i=0;i<8;++i)input[i]=inputValues[i];
    auto output=poisson::createTreeFromVector(input,*index,T(0));
    start("conversion","double");field("profile",-1);std::cout<<",\"target\":\""<<target<<"\",\"input\":";vector(input);std::cout<<",\"converted\":";vector(*poisson::createVectorFromTree<double>(*output,*index));std::cout<<'}';
}
template<class T> void allPoisson() {
    for(int profile=0;profile<8;++profile)for(bool staggered:{false,true})for(int mode:{0,1})for(bool jacobi:{false,true})poissonCase<T>(profile,mode,staggered,jacobi,60);
    for(bool staggered:{false,true})for(bool jacobi:{false,true}) { poissonCase<T>(6,2,staggered,jacobi,100);poissonCase<T>(3,0,staggered,jacobi,0);poissonCase<T>(3,1,staggered,jacobi,1);poissonCase<T>(1,3,staggered,jacobi,5); }
}
int main() { openvdb::initialize();std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[\n";allPoisson<float>();allPoisson<double>();conversionCase<openvdb::math::half>("half");conversionCase<bool>("bool");std::cout<<"]}\n"; }
