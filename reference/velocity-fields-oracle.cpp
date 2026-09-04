// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Independent native oracle for tools/VelocityFields.h.
#include <openvdb/openvdb.h>
#include <openvdb/tools/VelocityFields.h>
#include <iostream>
#include <iomanip>
#include <limits>
#include <cmath>
#include <sstream>
#include <openvdb/io/io.h>
using namespace openvdb;
static bool firstVelocityCase=true;
void number(double v) { if(std::isnan(v))std::cout<<"\"NaN\"";else if(std::isinf(v))std::cout<<(v<0?"\"-Infinity\"":"\"Infinity\"");else std::cout<<v; }
template<class V> void vec(const V& v) { std::cout<<'[';number(v[0]);std::cout<<',';number(v[1]);std::cout<<',';number(v[2]);std::cout<<']'; }
template<class T> void begin(const char* kind) { if(!firstVelocityCase)std::cout<<",\n";firstVelocityCase=false;std::cout<<"{\"kind\":\""<<kind<<"\",\"precision\":\""<<(sizeof(T)==4?"float":"double")<<'"'; }
math::Transform::Ptr makeTransform(int mode) {
    auto t=math::Transform::createLinearTransform();
    if(mode==1) { t->preScale(0.375);t->postTranslate(Vec3d(1.25,-2.5,0.75)); }
    if(mode==2) { t->preScale(Vec3d(0.7,1.125,1.375));t->postRotate(0.21,math::Z_AXIS);t->postShear(0.13,math::X_AXIS,math::Y_AXIS);t->postTranslate(Vec3d(-0.25,0.75,1.125)); }
    if(mode==3) { t=math::Transform::createFrustumTransform(BBoxd(Vec3d(-8,-6,-4),Vec3d(8,6,12)),0.65,9.0,0.75);t->postTranslate(Vec3d(0.5,-0.25,1.25)); }
    // The managed Transform stores affine maps explicitly; preserve that map representation in the oracle.
    // Transform(map) simplifies diagonal matrices; its public reader preserves the serialized map type.
    if(mode!=3) { math::AffineMap affine(t->baseMap()->getAffineMap()->getMat4());std::stringstream bytes;writeString(bytes,affine.type());affine.write(bytes);io::setCurrentVersion(bytes);t->read(bytes);if(t->mapType()!=math::AffineMap::mapType())throw std::logic_error("Expected AffineMap"); }
    return t;
}
template<class Grid> typename Grid::Ptr makeVelocityGrid(int mode) {
    using T=typename Grid::ValueType::ValueType;using V=typename Grid::ValueType;
    auto grid=Grid::create(V(T(-0.125),T(0.25),T(0.375)));grid->setTransform(makeTransform(mode));
    for(int x=-5;x<=5;++x)for(int y=-5;y<=5;++y)for(int z=-5;z<=5;++z) {
        V v(T(0.03125*x*x+0.0625*y-0.0234375*z+0.2),T(-0.015625*y*y+0.125*z+0.015625*x*z-0.1),T(0.046875*z*z-0.0625*x+0.03125*x*y+0.3));
        Coord c(x,y,z);grid->tree().setValue(c,v);if((x+y+z)%5==0)grid->tree().setValueOff(c);
    }
    return grid;
}
Vec3d indexPosition(int i) {
    if(i==0)return Vec3d(0);if(i==1)return Vec3d(-0.5,0.5,-1.5);if(i==2)return Vec3d(-5,-5,-5);if(i==3)return Vec3d(5,5,5);
    if(i==4)return Vec3d(-5.5,5.5,-5.5);if(i==5)return Vec3d(7.75,-8.125,6.625);if(i==6)return Vec3d(-1e-9,1e-9,0.999999999);
    if(i==7)return Vec3d(16777216.125,-16777216.25,0.375);
    return Vec3d((i*17%67-33)/7.0,(i*29%71-35)/9.0,(i*13%73-36)/11.0);
}
template<class Integrator,class Scalar,class Location> void integrate(const Integrator& integrator,Scalar dt,Location& location,int order) {
    switch(order) {case 0:integrator.template rungeKutta<0>(dt,location);break;case 1:integrator.template rungeKutta<1>(dt,location);break;case 2:integrator.template rungeKutta<2>(dt,location);break;case 3:integrator.template rungeKutta<3>(dt,location);break;case 4:integrator.template rungeKutta<4>(dt,location);break;}
}
template<class Grid,bool Staggered,int Order> void gridCase(int mode) {
    using T=typename Grid::ValueType::ValueType;using V=typename Grid::ValueType;
    auto grid=makeVelocityGrid<Grid>(mode);tools::VelocitySampler<Grid,Staggered,Order> sampler(*grid);tools::VelocityIntegrator<Grid,Staggered,Order> integrator(*grid);
    tools::DiscreteField<Grid,tools::Sampler<Order,Staggered>> discrete(*grid);
    begin<T>("grid");std::cout<<",\"map\":"<<mode<<",\"order\":"<<Order<<",\"staggered\":"<<(Staggered?"true":"false")<<",\"samples\":[";
    for(int i=0;i<24;++i) {
        if(i)std::cout<<',';const Vec3d index=indexPosition(i),world=grid->indexToWorld(index);const Vec3s worldF(world);V value,valueF;
        bool active=sampler.sample(world,value),activeF=sampler.sample(worldF,valueF);Coord coord(i%9-4,i%7-3,i%5-2);
        std::cout<<"{\"world\":";vec(world);std::cout<<",\"coord\":";vec(coord);std::cout<<",\"sample\":";vec(value);std::cout<<",\"sampleFloat\":";vec(valueF);
        std::cout<<",\"active\":"<<(active?"true":"false")<<",\"activeFloat\":"<<(activeF?"true":"false")<<",\"discrete\":";vec(discrete(world,T(1.25)));std::cout<<",\"discreteCoord\":";vec(discrete(coord,T(1.25)));
        std::cout<<",\"runs\":[";
        for(int time=0;time<3;++time)for(int order=0;order<5;++order) {
            if(time||order)std::cout<<',';const double rawDt=time==0?0.073125928871:time==1?-0.1191257654321:0;const T dt=T(rawDt);
            Vec3d result=world;Vec3s resultF=worldF;integrate(integrator,dt,result,order);integrate(integrator,dt,resultF,order);
            std::cout<<"{\"dt\":";number(rawDt);std::cout<<",\"order\":"<<order<<",\"double\":";vec(result);std::cout<<",\"float\":";vec(resultF);std::cout<<'}';
        }
        std::cout<<"]}";
    }
    std::cout<<"]}";
}
template<class T> void enrightCases() {
    tools::EnrightField<T> field;
    for(int block=0;block<8;++block) {
        begin<T>("enright");std::cout<<",\"samples\":[";
        for(int j=0;j<32;++j) {
            int i=block*32+j;Vec3d pos((i*17%257-50)/131.0,(i*29%263-55)/137.0,(i*43%269-57)/139.0);double time=(i*13%53-26)/7.0;
            if(i<8) {pos=Vec3d((i&1)?1.0:0,(i&2)?1.0:0,(i&4)?1.0:0);time=i*0.75;}
            if(i==8)time=1.5;if(i==9)time=3;if(i==10)time=6;
            if(i==11)pos[1]=std::numeric_limits<double>::quiet_NaN();if(i==12)pos[2]=std::numeric_limits<double>::infinity();if(i==13)time=std::numeric_limits<double>::infinity();
            if(i==14)pos=Vec3d(-0.0);if(i==15)pos=Vec3d(0.25,0.5,0.75);
            if(j)std::cout<<',';std::cout<<"{\"world\":";vec(pos);std::cout<<",\"time\":";number(time);std::cout<<",\"value\":";vec(field(pos,T(time)));Coord coord(i%9-4,i%7-3,i%5-2);std::cout<<",\"coord\":";vec(coord);std::cout<<",\"coordValue\":";vec(field(coord,T(time)));std::cout<<'}';
        }
        std::cout<<"]}";
    }
}
template<class Grid> void gridCases() {for(int mode=0;mode<4;++mode) {gridCase<Grid,false,0>(mode);gridCase<Grid,false,1>(mode);gridCase<Grid,false,2>(mode);gridCase<Grid,true,0>(mode);gridCase<Grid,true,1>(mode);gridCase<Grid,true,2>(mode);}}
int main() {openvdb::initialize();std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[\n";enrightCases<float>();enrightCases<double>();gridCases<Vec3SGrid>();gridCases<Vec3DGrid>();std::cout<<"]}\n";return 0;}
