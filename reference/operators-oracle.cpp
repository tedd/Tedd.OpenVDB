// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Every expected value is computed by pinned native OpenVDB.
#define main unusedMathOracleMain
#include "math-oracle.cpp"
#undef main
#include <openvdb/math/Operators.h>
#include <openvdb/tools/GridOperators.h>

template<class T>void values(const T* data,int length){std::cout<<'[';for(int i=0;i<length;++i){if(i)std::cout<<',';number(double(data[i]));}std::cout<<']';}
template<class G>void input(const G& grid,const openvdb::Coord& p){
    field("center",p.asVec3d());key("input");std::cout<<'[';bool first=true;
    for(int x=-3;x<=3;++x)for(int y=-3;y<=3;++y)for(int z=-3;z<=3;++z){if(!first)std::cout<<',';first=false;number(double(grid.tree().getValue(p.offsetBy(x,y,z))));}std::cout<<']';
}
template<class T>using ScalarGrid=openvdb::Grid<typename openvdb::tree::Tree4<T,5,4,3>::Type>;
template<class T>typename ScalarGrid<T>::Ptr makeInput(int iteration,openvdb::Coord center){
    auto grid=ScalarGrid<T>::create();
    for(int x=-3;x<=3;++x)for(int y=-3;y<=3;++y)for(int z=-3;z<=3;++z){
        T value=T(iteration==0?3:iteration==1?2*x-3*y+z+1:iteration==2?x*x+2*y*y+3*z*z+x*y-2*x*z+1:randomNumber()*10);
        if(iteration==3)value=T(value*1e-9);
        if(iteration==4&&x==0&&y==0&&z==0)value=std::numeric_limits<T>::quiet_NaN();
        grid->tree().setValue(center.offsetBy(x,y,z),value);
    }return grid;
}
template<class T>void convenience(){
    using G=ScalarGrid<T>;const openvdb::Coord center(2,-3,1);const Vec3<T> velocity(-2,0,3);
    for(int iteration=0;iteration<24;++iteration){
        auto grid=makeInput<T>(iteration,center);double dx=iteration==5?-0.7:0.31+iteration*0.13;
        GradStencil<G> grad(*grid,dx);WenoStencil<G>weno(*grid,dx);CurvatureStencil<G>curve(*grid,dx);
        grad.moveTo(center);weno.moveTo(center);curve.moveTo(center);
        begin("convenience");key("precision");std::cout<<(sizeof(T)==4?"\"float\"":"\"double\"");input(*grid,center);field("dx",dx);
        field("gradient",Vec3d(grad.gradient()));field("biased",Vec3d(grad.gradient(velocity)));
        field("norm",double(grad.normSqGrad()));field("laplacian",double(grad.laplacian()));field("cpt",Vec3d(grad.cpt()));field("crossing",double(grad.zeroCrossing()));
        field("wenoGradient",Vec3d(weno.gradient()));field("wenoBiased",Vec3d(weno.gradient(velocity)));
        field("wenoNorm",double(weno.normSqGrad()));field("wenoIsoNorm",double(weno.normSqGrad(T(4))));field("wenoLaplacian",double(weno.laplacian()));field("wenoCrossing",double(weno.zeroCrossing()));
        T mean,gauss;curve.curvatures(mean,gauss);field("mean",double(mean));field("gaussian",double(gauss));
        T mn,gn;curve.curvaturesNormGrad(mn,gn);field("meanNorm",double(mn));field("gaussianNorm",double(gn));
        auto pair=curve.principalCurvatures();field("minimum",double(pair.first));field("maximum",double(pair.second));end();
    }
}

template<DScheme S,class G,class M>void derivative(const G& grid,const M& map,const openvdb::Coord& p){
    auto accessor=grid.getConstAccessor();Vec3d g=ISGradient<S>::result(accessor,p),w=Gradient<M,S>::result(map,accessor,p);
    std::cout<<'[';emit(g);std::cout<<',';emit(w);std::cout<<']';
}
template<class G,class M>void derivatives(const G& g,const M& m,const openvdb::Coord& p){
#define D(S) derivative<S>(g,m,p)
    std::cout<<'[';D(CD_2NDT);std::cout<<',';D(CD_2ND);std::cout<<',';D(CD_4TH);std::cout<<',';D(CD_6TH);
    std::cout<<',';D(FD_1ST);std::cout<<',';D(FD_2ND);std::cout<<',';D(FD_3RD);
    std::cout<<',';D(BD_1ST);std::cout<<',';D(BD_2ND);std::cout<<',';D(BD_3RD);
    std::cout<<',';D(FD_WENO5);std::cout<<',';D(BD_WENO5);std::cout<<',';D(FD_HJWENO5);std::cout<<',';D(BD_HJWENO5);std::cout<<']';
#undef D
}
template<BiasedGradientScheme S,class G,class M>void bias(const G& grid,const M& map,const openvdb::Coord& p){
    auto a=grid.getConstAccessor();using T=typename G::ValueType;Vec3<T>v(-2,0,3);
    std::cout<<'[';emit(Vec3d(ISGradientBiased<S,Vec3<T>>::result(a,p,v)));std::cout<<',';emit(Vec3d(GradientBiased<M,S>::result(map,a,p,v)));
    std::cout<<',';number(double(ISGradientNormSqrd<S>::result(a,p)));std::cout<<',';number(double(GradientNormSqrd<M,S>::result(map,a,p)));std::cout<<']';
}
template<DDScheme S,DScheme F,class G,class M>void second(const G& grid,const M& map,const openvdb::Coord& p){
    auto a=grid.getConstAccessor();typename G::ValueType alpha,beta;
    ISMeanCurvature<S,F>::result(a,p,alpha,beta);
    std::cout<<'[';number(double(ISLaplacian<S>::result(a,p)));std::cout<<',';number(double(Laplacian<M,S>::result(map,a,p)));
    std::cout<<',';number(double(MeanCurvature<M,S,F>::result(map,a,p)));std::cout<<',';number(double(MeanCurvature<M,S,F>::normGrad(map,a,p)));
    std::cout<<',';number(double(alpha));std::cout<<',';number(double(beta));std::cout<<']';
}
template<class T,class M>void scalarCase(const ScalarGrid<T>& grid,const M& map,const openvdb::Coord& center,int mapKind){
    begin("scalar");key("precision");std::cout<<(sizeof(T)==4?"\"float\"":"\"double\"");input(grid,center);field("map",double(mapKind));
    key("gradients");derivatives(grid,map,center);
    key("bias");std::cout<<'[';bias<FIRST_BIAS>(grid,map,center);std::cout<<',';bias<SECOND_BIAS>(grid,map,center);std::cout<<',';bias<THIRD_BIAS>(grid,map,center);
    std::cout<<',';bias<WENO5_BIAS>(grid,map,center);std::cout<<',';bias<HJWENO5_BIAS>(grid,map,center);std::cout<<']';
    key("seconds");std::cout<<'[';second<CD_SECOND,CD_2ND>(grid,map,center);std::cout<<',';second<CD_FOURTH,CD_4TH>(grid,map,center);std::cout<<',';second<CD_SIXTH,CD_6TH>(grid,map,center);std::cout<<']';
    auto a=grid.getConstAccessor();field("cptIndex",Vec3d(CPT<M,CD_2ND>::result(map,a,center)));field("cptWorld",Vec3d(CPT_RANGE<M,CD_2ND>::result(map,a,center)));end();
}
Mat4d affineMatrix(){return Mat4d(1.3,0.2,-0.1,0.0,-0.3,0.8,0.25,0.0,0.1,-0.15,1.7,0.0,3.0,-2.0,4.0,1.0);}
NonlinearFrustumMap frustum(){return NonlinearFrustumMap(BBox<Vec3d>(Vec3d(-4,-8,-3),Vec3d(8,10,12)),0.6,5,AffineMap::Ptr(new AffineMap(affineMatrix())));}
template<class T>void scalars(){
    const openvdb::Coord center(2,-3,1);AffineMap affine(affineMatrix());auto f=frustum();
    for(int iteration=0;iteration<16;++iteration){auto grid=makeInput<T>(iteration,center);scalarCase<T>(*grid,affine,center,0);scalarCase<T>(*grid,f,center,1);}
}
template<DScheme S,class G,class M>void vectorDerivative(const G& grid,const M& map,const openvdb::Coord& center){
    auto a=grid.getConstAccessor();std::cout<<'[';number(double(ISDivergence<S>::result(a,center)));std::cout<<',';number(double(Divergence<M,S>::result(map,a,center)));
    std::cout<<',';emit(Vec3d(ISCurl<S>::result(a,center)));std::cout<<',';emit(Vec3d(Curl<M,S>::result(map,a,center)));std::cout<<']';
}
template<class T,class M>void vectorCase(const M& map,int mapKind,int iteration){
    using G=openvdb::Grid<typename openvdb::tree::Tree4<Vec3<T>,5,4,3>::Type>;auto grid=G::create();
    const openvdb::Coord center(2,-3,1);
    begin("vector");key("precision");std::cout<<(sizeof(T)==4?"\"float\"":"\"double\"");field("center",center.asVec3d());field("map",double(mapKind));key("input");std::cout<<'[';bool first=true;
    for(int x=-3;x<=3;++x)for(int y=-3;y<=3;++y)for(int z=-3;z<=3;++z){
        Vec3<T>v=iteration==0?Vec3<T>(x*x-y,y*y+z,z*z+x):Vec3<T>(T(randomNumber()*10),T(randomNumber()*10),T(randomNumber()*10));
        if(iteration==1&&x==0&&y==0&&z==0)v[0]=std::numeric_limits<T>::quiet_NaN();
        grid->tree().setValue(center.offsetBy(x,y,z),v);if(!first)std::cout<<',';first=false;emit(Vec3d(v));
    }std::cout<<']';key("derivatives");std::cout<<'[';
#define V(S) vectorDerivative<S>(*grid,map,center)
    V(CD_2NDT);std::cout<<',';V(CD_2ND);std::cout<<',';V(CD_4TH);std::cout<<',';V(CD_6TH);
    std::cout<<',';V(FD_1ST);std::cout<<',';V(FD_2ND);std::cout<<',';V(FD_3RD);std::cout<<',';V(BD_1ST);std::cout<<',';V(BD_2ND);std::cout<<',';V(BD_3RD);
    std::cout<<']';end();
#undef V
}
template<class T>void vectors(){AffineMap affine(affineMatrix());auto f=frustum();for(int i=0;i<8;++i){vectorCase<T>(affine,0,i);vectorCase<T>(f,1,i);}}

int main(){
    openvdb::initialize();std::cout<<std::setprecision(17);
    std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    convenience<float>();convenience<double>();scalars<float>();scalars<double>();vectors<float>();vectors<double>();
    std::cout<<"]}";openvdb::uninitialize();return 0;
}
