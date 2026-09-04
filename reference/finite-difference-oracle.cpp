// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// All expected outputs are computed by pinned native OpenVDB.
#define main unusedMathOracleMain
#include "math-oracle.cpp"
#undef main
#include <openvdb/math/FiniteDifference.h>
#include <openvdb/math/Stencils.h>
template<class T>struct LineAccessor {using ValueType=T;const T* values;T getValue(const openvdb::Coord& p)const{return values[p.x()+3];}};
template<class T>T first(int scheme,const T* p){
    const LineAccessor<T> accessor{p};const openvdb::Coord origin(0);
    switch(scheme){
        case CD_2NDT:return D1<CD_2NDT>::inX(accessor,origin);
        case CD_2ND:return D1<CD_2ND>::inX(accessor,origin);
        case CD_4TH:return D1<CD_4TH>::inX(accessor,origin);
        case CD_6TH:return D1<CD_6TH>::inX(accessor,origin);
        case FD_1ST:return D1<FD_1ST>::inX(accessor,origin);
        case FD_2ND:return D1<FD_2ND>::inX(accessor,origin);
        case FD_3RD:return D1<FD_3RD>::inX(accessor,origin);
        case BD_1ST:return D1<BD_1ST>::inX(accessor,origin);
        case BD_2ND:return D1<BD_2ND>::inX(accessor,origin);
        case BD_3RD:return D1<BD_3RD>::inX(accessor,origin);
        case FD_WENO5:return D1<FD_WENO5>::inX(accessor,origin);
        case BD_WENO5:return D1<BD_WENO5>::inX(accessor,origin);
        case FD_HJWENO5:return D1<FD_HJWENO5>::inX(accessor,origin);
        case BD_HJWENO5:return D1<BD_HJWENO5>::inX(accessor,origin);
        default:throw std::runtime_error("Unknown difference scheme");
    }
}

template<class T>void emitArray(const T* values,int size){std::cout<<'[';for(int i=0;i<size;++i){if(i)std::cout<<',';number(double(values[i]));}std::cout<<']';}
template<class T>void kernels(){
    for(int iteration=0;iteration<45;++iteration){
        T values[7];for(int i=0;i<7;++i)values[i]=T(iteration<3?iteration==0?2:iteration==1?i-3:(i-3)*(i-3):randomNumber()*100);
        if(iteration==44)values[3]=std::numeric_limits<T>::quiet_NaN();
        begin("difference");key("precision");std::cout<<(sizeof(T)==4?"\"float\"":"\"double\"");key("values");emitArray(values,7);
        key("first");std::cout<<'[';for(int scheme=0;scheme<NUM_DS_SCHEMES;++scheme){if(scheme)std::cout<<',';number(double(first(scheme,values)));}std::cout<<']';
        T seconds[]={D2<CD_SECOND>::difference(values[4],values[3],values[2]),
            D2<CD_FOURTH>::difference(values[5],values[4],values[3],values[2],values[1]),
            D2<CD_SIXTH>::difference(values[6],values[5],values[4],values[3],values[2],values[1],values[0])};
        key("second");emitArray(seconds,3);field("weno",double(WENO5(values[0],values[1],values[2],values[3],values[4])));
        field("godunovOut",double(GodunovsNormSqrd(true,values[0],values[1],values[2],values[3],values[4],values[5])));
        field("godunovIn",double(GodunovsNormSqrd(false,values[0],values[1],values[2],values[3],values[4],values[5])));end();
    }
}
template<class S,class G>void stencilCase(S& stencil,const G& grid,int shape){
    begin("stencil");field("shape",double(shape));key("input");std::cout<<'[';bool firstValue=true;
    for(int x=-3;x<=3;++x)for(int y=-3;y<=3;++y)for(int z=-3;z<=3;++z){if(!firstValue)std::cout<<',';firstValue=false;number(grid.tree().getValue(openvdb::Coord(x,y,z)));}std::cout<<']';
    stencil.moveTo(openvdb::Coord(0));field("size",double(stencil.size()));field("center",double(stencil.getCenterValue()));
    key("values");std::cout<<'[';for(int i=0;i<stencil.size();++i){if(i)std::cout<<',';number(stencil.getValue(i));}std::cout<<']';
    field("mean",double(stencil.mean()));field("median",double(stencil.median()));field("min",double(stencil.min()));field("max",double(stencil.max()));end();
}
int main(){
    openvdb::initialize();std::cout<<std::setprecision(17);
    std::cout<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    kernels<double>();kernels<float>();
    auto grid=openvdb::DoubleGrid::create();
    for(int x=-3;x<=3;++x)for(int y=-3;y<=3;++y)for(int z=-3;z<=3;++z)grid->tree().setValue(openvdb::Coord(x,y,z),randomNumber()*100);
    SevenPointStencil<openvdb::DoubleGrid> s0(*grid);stencilCase(s0,*grid,0);
    ThirteenPointStencil<openvdb::DoubleGrid> s1(*grid);stencilCase(s1,*grid,1);
    NineteenPointStencil<openvdb::DoubleGrid> s2(*grid);stencilCase(s2,*grid,2);
    SecondOrderDenseStencil<openvdb::DoubleGrid> s3(*grid);stencilCase(s3,*grid,3);
    FourthOrderDenseStencil<openvdb::DoubleGrid> s4(*grid);stencilCase(s4,*grid,4);
    SixthOrderDenseStencil<openvdb::DoubleGrid> s5(*grid);stencilCase(s5,*grid,5);
    BoxStencil<openvdb::DoubleGrid> s6(*grid);stencilCase(s6,*grid,6);
    DenseStencil<openvdb::DoubleGrid> s7(*grid,1);stencilCase(s7,*grid,7);
    DenseStencil<openvdb::DoubleGrid> s8(*grid,2);stencilCase(s8,*grid,8);
    DenseStencil<openvdb::DoubleGrid> s9(*grid,3);stencilCase(s9,*grid,9);
    std::cout<<"]}";openvdb::uninitialize();return 0;
}
