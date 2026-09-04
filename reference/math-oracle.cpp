// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Independent executable: every expected value is evaluated by pinned native OpenVDB.
#include <openvdb/openvdb.h>
#include <openvdb/math/Maps.h>
#include <openvdb/math/Quat.h>
#include <openvdb/math/Transform.h>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <limits>
#include <random>
#include <string>
#include <vector>
using namespace openvdb::math;
void number(double x) {
    if(std::isnan(x))std::cout<<"\"NaN\"";
    else if(std::isinf(x))std::cout<<(x>0?"\"Infinity\"":"\"-Infinity\"");
    else std::cout<<x;
}
void key(const char* s){std::cout<<",\""<<s<<"\":";}
void emit(double x){number(x);}
void emit(const Vec3d& v){std::cout<<'[';for(int i=0;i<3;++i){if(i)std::cout<<',';number(v[i]);}std::cout<<']';}
void emit(const Quatd& v){std::cout<<'[';for(int i=0;i<4;++i){if(i)std::cout<<',';number(v[i]);}std::cout<<']';}
template<class M>void matrix(const M& m,int size){std::cout<<'[';for(int r=0;r<size;++r)for(int c=0;c<size;++c){if(r||c)std::cout<<',';number(m[r][c]);}std::cout<<']';}
void emit(const Mat3d& m){matrix(m,3);}
void emit(const Mat4d& m){matrix(m,4);}
template<class T>void field(const char* s,const T& x){key(s);emit(x);}
int count=0;
void begin(const char* kind){if(count++)std::cout<<',';std::cout<<"{\"kind\":\""<<kind<<"\"";}
void end(){std::cout<<'}';}
std::mt19937 randomEngine(54987);
double randomNumber(){return double(randomEngine())/double(std::mt19937::max())*2-1;}
Vec3d randomVector(){return Vec3d(randomNumber(),randomNumber(),randomNumber());}
void frustumCase(const NonlinearFrustumMap& map,const Vec3d& p,const Vec3d& v,const Mat3d& h){
    begin("frustum");
    field("min",map.getBBox().min());field("max",map.getBBox().max());
    field("taper",map.getTaper());field("depth",map.getDepth());field("secondary",map.secondMap().getMat4());
    field("p",p);field("v",v);field("h",h);
    Vec3d world=map.applyMap(p);field("world",world);field("inverse",map.applyInverseMap(world));
    field("j",map.applyJacobian(v,p));field("ij",map.applyInverseJacobian(v,p));field("jt",map.applyJT(v,p));
    field("ijt",map.applyIJT(v,p));field("ijc",map.applyIJC(h,v,p));field("size",map.voxelSize());
    field("sizeAt",map.voxelSize(p));field("determinant",map.determinant());field("determinantAt",map.determinant(p));
    field("j0",map.applyJacobian(v));field("ij0",map.applyInverseJacobian(v));field("jt0",map.applyJT(v));field("ijt0",map.applyIJT(v));
    end();
}
int main(){
    openvdb::initialize();std::cout<<std::setprecision(17);
    std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    for(int i=0;i<60;++i){
        Vec3d from=randomVector(),to=randomVector();
        if(i<6){from=Vec3d(0);from[i%3]=1;to=(i<3?1.:-1.)*from;}
        if(i==6){from=Vec3d(0);to=Vec3d(0);}
        begin("rotation");field("from",from);field("to",to);field("result",rotation<Mat3d>(from,to));end();
    }
    for(int i=0;i<50;++i){
        Mat3d m(randomNumber()+3,randomNumber(),randomNumber(),randomNumber(),randomNumber()+3,randomNumber(),randomNumber(),randomNumber(),randomNumber()+3);
        begin("mat3");field("m",m);field("inverse",m.inverse());field("transpose",m.transpose());field("cofactor",m.cofactor());
        field("adjoint",m.adjoint());field("determinant",m.det());end();
        Mat4d n;
        for(int r=0;r<4;++r)for(int c=0;c<4;++c)n[r][c]=randomNumber()+(r==c?4:0);
        if(i==0)n=Mat4d(0,0,0,1,0,1,0,0,0,0,1,0,1,0,0,0);
        if(i==1)n=Mat4d(1,0,0,1,0,1,0,0,0,0,1,0,1,2,3,-2);
        Vec3d p=i==1?Vec3d(2,3,5):randomVector();
        begin("mat4");field("m",n);field("p",p);field("inverse",n.inverse());field("determinant",n.det());
        field("transformH",n.transformH(p));field("pretransformH",n.pretransformH(p));end();
    }
    for(int i=0;i<50;++i){
        Quatd a(randomNumber(),randomNumber(),randomNumber(),randomNumber());
        Quatd b(randomNumber(),randomNumber(),randomNumber(),randomNumber());a.normalize();b.normalize();
        if(i==0)b=a;
        if(i==1)b=-a;
        if(i==2)b=-a+Quatd(1e-9,0,0,0);
        double t=i%5==0?0.5:randomNumber();Vec3d omega=randomVector();
        begin("quat");field("a",a);field("b",b);field("t",t);field("omega",omega);
        field("product",a*b);field("inverse",a.inverse());field("slerp",slerp(a,b,t));field("derivative",a.derivative(omega));
        Mat3d ma(a);field("matrix",ma);field("matrixQuat",Quatd(ma,Quatd::UnsafeConstruct()));
        field("axis",a.axis());field("angle",a.angle());end();
    }
    std::vector<Vec3d> special={Vec3d(0),Vec3d(1,2,3),Vec3d(std::numeric_limits<double>::quiet_NaN(),1,0),
        Vec3d(std::numeric_limits<double>::infinity(),0,1),Vec3d(-0.,0.,-0.),Vec3d(1e-16,0,0)};
    for(const Vec3d& v:special){begin("vector");field("v",v);field("unitSafe",v.unitSafe());field("length",v.length());field("equalsSelf",double(v==v));end();}

    {
        double nan=std::numeric_limits<double>::quiet_NaN();Vec3d v(nan,1,0),norm=v;
        Mat3d m=Mat3d::identity();m[0][0]=nan;Mat4d n=Mat4d::identity();n[0][0]=nan;
        Quatd q(nan,0,0,1),qnorm=q;
        bool vectorUnitThrows=false,m3InverseThrows=false,m4InverseThrows=false,qInverseThrows=false;
        try{v.unit();}catch(const std::exception&){vectorUnitThrows=true;}
        try{m.inverse();}catch(const std::exception&){m3InverseThrows=true;}
        try{n.inverse();}catch(const std::exception&){m4InverseThrows=true;}
        try{q.inverse();}catch(const std::exception&){qInverseThrows=true;}
        begin("nan");
        field("vectorNormalize",double(norm.normalize()));field("vectorUnitThrows",double(vectorUnitThrows));
        field("vectorApprox",double(v.eq(Vec3d(3,1,0))));field("matrixApprox",double(m.eq(Mat3d::identity())));
        field("matrix4Approx",double(n.eq(Mat4d::identity())));field("matrixUnitary",double(isUnitary(m)));
        field("matrix3InverseThrows",double(m3InverseThrows));field("matrix4InverseThrows",double(m4InverseThrows));
        field("quatInverseThrows",double(qInverseThrows));field("quatNormalize",double(qnorm.normalize()));
        field("quatApprox",double(q.eq(Quatd(3,0,0,1))));field("quatUnit",q.unit());field("quatSlerp",slerp(q,Quatd(0,0,0,1),0.5));
        field("quatMatrix",Mat3d(q));field("quatFromMatrix",Quatd(m));field("quatFromNanAxis",Quatd(v,0.3));
        field("vectorLargeRelative",double(Vec3d(1e12,1,1).eq(Vec3d(1e12+1,1,1),1e-7)));
        end();
    }

    Mat3d h(4,1,2,1,6,-1,2,-1,10);
    for(int i=0;i<50;++i){
        Vec3d lo=randomVector()*5,ext(8,12,20);openvdb::BBoxd box(lo,lo+ext);
        Mat4d secondary=scale<Mat4d>(Vec3d(2,3,4))*rotation<Mat4d>(Vec3d(1,2,3),0.37);
        secondary.setTranslation(Vec3d(11,-7,3));
        double taper=i%4==0?1:i%4==1?0.25:i%4==2?2:0.2,depth=i%3+1.;
        NonlinearFrustumMap map(box,taper,depth,MapBase::Ptr(new AffineMap(secondary)));
        Vec3d p=lo+(randomVector()+Vec3d(1))*ext*0.5;
        frustumCase(map,p,randomVector(),h);
    }
    for(int i=0;i<9;++i){
        Vec3d position(3,-5,7),direction(0,0,1),up(0,1,0);
        if(i<6){direction=Vec3d(0);direction[i%3]=i<3?1:-1;up=direction.x()==0?Vec3d(1,0,0):Vec3d(0,1,0);}
        else{direction=Vec3d(1,0,1);up=Vec3d(0,i-5.,0);}
        double aspect=2,near=3,depth=12;int x=80,z=120;
        NonlinearFrustumMap map(position,direction,up,aspect,near,depth,x,z);
        begin("camera");field("position",position);field("direction",direction);field("up",up);field("aspect",aspect);
        field("near",near);field("depth",depth);field("x",double(x));field("z",double(z));
        field("min",map.getBBox().min());field("max",map.getBBox().max());field("secondary",map.secondMap().getMat4());
        field("taper",map.getTaper());field("normalizedDepth",map.getDepth());
        field("nearCenter",map.applyMap(Vec3d(40,20,0)));field("farCenter",map.applyMap(Vec3d(40,20,120)));
        field("corner",map.applyMap(Vec3d(80,40,120)));end();
    }
    std::cout<<"]}";openvdb::uninitialize();return 0;
}
