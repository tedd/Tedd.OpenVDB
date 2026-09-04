// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Native reference for the managed port of math/ConjGradient.h.
#include <openvdb/math/ConjGradient.h>
#include <iostream>
#include <iomanip>
#include <limits>
#include <cmath>
#include <string>
using namespace openvdb::math;
namespace p = openvdb::math::pcg;
static bool first = true;
void num(double value) { if(std::isnan(value)) std::cout << "\"NaN\""; else if(std::isinf(value)) std::cout << (value<0?"\"-Infinity\"":"\"Infinity\""); else std::cout << value; }
void start(const char* kind, const char* precision) { if(!first) std::cout<<",\n"; first=false; std::cout<<"{\"kind\":\""<<kind<<"\",\"precision\":\""<<precision<<"\""; }
void field(const char* name, double value) { std::cout<<",\""<<name<<"\":";num(value); }
template<class T> void vector(const p::Vector<T>& v) { std::cout<<'['; for(p::SizeType i=0;i<v.size();++i) { if(i)std::cout<<',';num(v[i]); } std::cout<<']'; }
template<class T, unsigned S> void matrix(const p::SparseStencilMatrix<T,S>& a) { std::cout<<'['; for(unsigned r=0;r<a.size();++r) { if(r)std::cout<<',';std::cout<<'[';bool f=true;for(auto i=a.getConstRow(r).cbegin();i;++i) { if(!f)std::cout<<',';f=false;std::cout<<'['<<i.column()<<',';num(*i);std::cout<<']'; }std::cout<<']';}std::cout<<']'; }
template<class T> const char* precision() { return sizeof(T)==4?"float":"double"; }
template<class T> void vectorCase(int size, int profile) {
    p::Vector<T> a(size), b(size);
    for(int i=0;i<size;++i) {
        a[i]=T((i*17+3)%101-50)/T(7); b[i]=T((i*29+1)%97-48)/T(11);
        if(profile==1) { a[i] = i%4==0?T(1e12):i%4==2?T(-1e12):T((i%7)-3); b[i]=T(1); }
        if(profile==2) { a[i]=T((i%11)-5)*T(1e-12);b[i]=T((i%7)-3)*T(1e-12); }
        if(profile==3) { a[i]= i%2?T(-0.0):T(0.0); b[i]=T(-1); }
        if(profile==4) { a[i]=i%3==0?std::numeric_limits<T>::quiet_NaN():i%3==1?std::numeric_limits<T>::infinity():-std::numeric_limits<T>::infinity();b[i]=T(1); }
    }
    start("vector",precision<T>());field("size",size);field("profile",profile);field("dot",a.dot(b));field("infinity",a.infNorm());field("l2",a.l2Norm());field("finite",a.isFinite());
    a.scale(1.00000000000001);field("scaledDot",a.dot(b));std::cout<<'}';
}
template<class T> p::SparseStencilMatrix<T,7> build(int profile) {
    const int n=profile==0?5:profile==1?9:profile==2?17:profile==3?27:profile==4?1331:63;
    p::SparseStencilMatrix<T,7> a(n);
    if(profile==0) { a.setValue(0,0,T(24));a.setValue(0,2,T(6));a.setValue(1,1,T(8));a.setValue(1,2,T(2));a.setValue(2,0,T(6));a.setValue(2,1,T(2));a.setValue(2,2,T(8));a.setValue(2,3,T(-6));a.setValue(2,4,T(2));a.setValue(3,2,T(-6));a.setValue(3,3,T(24));a.setValue(4,2,T(2));a.setValue(4,4,T(8));return a; }
    for(int r=0;r<n;++r) {
        if(profile==1) { a.setValue(r,r,T((r+1)*(r+1))/T(3));continue; }
        if(profile==2||profile==5) { a.setValue(r,r,profile==2?T(3):T(2.001));if(r>0)a.setValue(r,r-1,T(-1));if(r+1<n)a.setValue(r,r+1,T(-1));continue; }
        int d=profile==3?3:11;int x=r/(d*d),y=(r/d)%d,z=r%d;
        a.setValue(r,r,T(7));
        if(x>0)a.setValue(r,r-d*d,T(-1));if(x+1<d)a.setValue(r,r+d*d,T(-1));
        if(y>0)a.setValue(r,r-d,T(-1));if(y+1<d)a.setValue(r,r+d,T(-1));
        if(z>0)a.setValue(r,r-1,T(-1));if(z+1<d)a.setValue(r,r+1,T(-1));
    }
    return a;
}
template<class T> void solveCase(int profile,int mode,int maxIterations,int initial) {
    auto a=build<T>(profile);p::Vector<T> b(a.size()), x(a.size());
    for(unsigned i=0;i<a.size();++i) { b[i]=initial==2?T(0):T(int((i*13+2)%29)-14)/T(7);x[i]=initial==0?T(0):T(int(i%5)-2)/T(11); }
    p::State limit=p::terminationDefaults<T>();limit.iterations=maxIterations;if(initial==3) { limit.relativeError=0;limit.absoluteError=0; }
    std::unique_ptr<p::Preconditioner<T>> preconditioner;
    if(mode==0)preconditioner.reset(new p::JacobiPreconditioner<decltype(a)>(a));else preconditioner.reset(new p::IncompleteCholeskyPreconditioner<decltype(a)>(a));
    start("solve",precision<T>());field("profile",profile);field("mode",mode);field("maximum",maxIterations);field("initial",initial);
    field("limitRelative",limit.relativeError);field("limitAbsolute",limit.absoluteError);
    openvdb::util::NullInterrupter interrupter;auto state=p::solve(a,b,x,*preconditioner,interrupter,limit);field("success",state.success);field("iterations",state.iterations);field("relative",state.relativeError);field("absolute",state.absoluteError);
    std::cout<<",\"solution\":";vector(x);std::cout<<'}';
}
template<class T> void factorCase(int profile) {
    auto a=build<T>(profile);p::Vector<T> r(a.size()),z(a.size());for(unsigned i=0;i<a.size();++i)r[i]=T(int(i%17)-8)/T(13);
    p::IncompleteCholeskyPreconditioner<decltype(a)> ic(a);p::JacobiPreconditioner<decltype(a)> jacobi(a);
    start("factor",precision<T>());field("profile",profile);field("valid",ic.isValid());std::cout<<",\"lower\":";matrix(ic.lowerMatrix());std::cout<<",\"upper\":";matrix(ic.upperMatrix());
    ic.apply(r,z);std::cout<<",\"choleskyApplied\":";vector(z);jacobi.apply(r,z);std::cout<<",\"jacobiApplied\":";vector(z);std::cout<<'}';
}
template<class T> void invalidFactorCase(int profile) {
    p::SparseStencilMatrix<T,7> a(3);a.setValue(0,0,profile==0?T(0):profile==1?T(0.000009999):profile==2?T(0.00001):profile==4?std::numeric_limits<T>::quiet_NaN():T(1));a.setValue(1,1,T(1));a.setValue(2,2,profile==3?T(-1):T(1));
    p::IncompleteCholeskyPreconditioner<decltype(a)> ic(a);start("invalidFactor",precision<T>());field("profile",profile);field("valid",ic.isValid());std::cout<<",\"lower\":";matrix(ic.lowerMatrix());std::cout<<",\"upper\":";matrix(ic.upperMatrix());std::cout<<'}';
}
template<class T> void rowCase() {
    p::SparseStencilMatrix<T,7> a(7);auto row=a.getRowEditor(3);row.setValue(5,T(11));row.setValue(1,T(-2));row.setValue(3,T(0));row.setValue(0,T(4));row.setValue(5,T(12));row.scale(1.00000000000001);
    p::Vector<double> input(7),result(7);for(int i=0;i<7;++i)input[i]=(i-3)/7.0;a.vectorMultiply(input,result);
    start("row",precision<T>());std::cout<<",\"matrix\":";matrix(a);std::cout<<",\"productDouble\":";vector(result);
    p::Vector<float> single(7),singleResult(7);for(int i=0;i<7;++i)single[i]=float(input[i]);a.vectorMultiply(single,singleResult);std::cout<<",\"productFloat\":";vector(singleResult);std::cout<<'}';
}
struct InterruptAfter { int allowed;int calls=0;bool wasInterrupted() { return calls++>=allowed; } };
template<class T> void edgeCase(int mode) {
    auto a=build<T>(2);p::Vector<T> b(a.size(),T(1)),x(a.size(),T(.7));
    p::JacobiPreconditioner<decltype(a)> preconditioner(a);
    p::State limit=p::terminationDefaults<T>();limit.relativeError=0;limit.absoluteError=0;
    InterruptAfter interrupter{mode==0?0:1};bool interrupted=false;
    try { p::solve(a,b,x,preconditioner,interrupter,limit); }
    catch(const openvdb::RuntimeError&) { interrupted=true; }
    start("interruption",precision<T>());field("mode",mode);field("interrupted",interrupted);field("calls",interrupter.calls);std::cout<<",\"solution\":";vector(x);std::cout<<'}';
}
template<class T> void all() {
    edgeCase<T>(0);edgeCase<T>(1);
    for(int n:{0,1,5,99,1023,1024,1025,10037})for(int p=0;p<5;++p)vectorCase<T>(n,p);
    rowCase<T>();for(int p=0;p<6;++p)factorCase<T>(p);for(int p=0;p<5;++p)invalidFactorCase<T>(p);
    for(int profile=0;profile<6;++profile)for(int mode=0;mode<2;++mode)for(int max:{0,1,2,5,50})for(int initial=0;initial<4;++initial)solveCase<T>(profile,mode,max,initial);
}
int main() { std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[\n";all<float>();all<double>();std::cout<<"]}\n";return 0; }
