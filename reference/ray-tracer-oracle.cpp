// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Independent native oracle for openvdb/tools/RayTracer.h.
#include <openvdb/openvdb.h>
#include <openvdb/tools/RayTracer.h>
#include <openvdb/tools/LevelSetSphere.h>
#include <openvdb/tools/LevelSetUtil.h>
#include <tbb/global_control.h>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <limits>
#include <memory>
#include <string>
#include <vector>

using namespace openvdb;
using Rgba = tools::Film::RGBA;
static bool firstCase = true;

void number(double value)
{
    if (std::isnan(value)) std::cout << "\"NaN\"";
    else if (std::isinf(value)) std::cout << (value < 0 ? "\"-Infinity\"" : "\"Infinity\"");
    else std::cout << value;
}
template<typename V> void vector3(const V& value)
{
    std::cout << '['; number(value[0]); std::cout << ','; number(value[1]); std::cout << ','; number(value[2]); std::cout << ']';
}
void rgba(const Rgba& value)
{
    std::cout << '['; number(value.r); std::cout << ','; number(value.g); std::cout << ','; number(value.b); std::cout << ','; number(value.a); std::cout << ']';
}
void begin(const char* kind)
{
    if (!firstCase) std::cout << ",\n";
    firstCase = false;
    std::cout << "{\"kind\":\"" << kind << '"';
}
void filmPixels(const tools::Film& film)
{
    std::cout << '[';
    for (size_t y = 0; y < film.height(); ++y) for (size_t x = 0; x < film.width(); ++x) {
        if (x || y) std::cout << ',';
        rgba(film.pixel(x, y));
    }
    std::cout << ']';
}

void filmCases()
{
    begin("rgba");
    Rgba a(0.2f, 0.3f, 0.4f, 0.25f), b(0.5f, 0.6f, 0.7f, 0.75f);
    Rgba add = a + b, multiply = a * b, scale = a * 2.0f, accumulate = a, over = a;
    accumulate += b; over.over(b);
    std::cout << ",\"values\":["; rgba(Rgba()); std::cout << ','; rgba(Rgba(0.375f)); std::cout << ','; rgba(add); std::cout << ','; rgba(multiply); std::cout << ','; rgba(scale); std::cout << ','; rgba(accumulate); std::cout << ','; rgba(over); std::cout << "]}";

    begin("film");
    tools::Film film(7, 5);
    std::cout << ",\"default\":"; filmPixels(film);
    film.checkerboard(Rgba(0.2f, 0.3f, 0.4f, 0.5f), Rgba(0.8f, 0.7f, 0.6f, 0.1f), 2);
    std::cout << ",\"checker\":"; filmPixels(film);
    auto bytes = film.convertToBitBuffer<unsigned char>(true);
    std::cout << ",\"bytes\":[";
    for (size_t i = 0; i < film.numPixels() * 4; ++i) { if (i) std::cout << ','; std::cout << unsigned(bytes[i]); }
    std::cout << "]}";
}

struct CameraConfiguration
{
    int width, height;
    Vec3d rotation, translation, target, up;
    double lens, aperture, frameWidth, nearPlane, farPlane;
    bool lookAt;
};

void cameraCases()
{
    const CameraConfiguration configs[] = {
        {1,1,Vec3d(0),Vec3d(0),Vec3d(0),Vec3d(0,1,0),50,41.2136,1,0.001,100,false},
        {4,3,Vec3d(10,-20,30),Vec3d(1.25,-2.5,8),Vec3d(0),Vec3d(0,1,0),35,24.576,7.5,0.125,80,false},
        {7,5,Vec3d(-17.5,42.25,91),Vec3d(-3.5,2.25,11.75),Vec3d(0.25,-0.5,1),Vec3d(0,1,0),80,41.2136,12,0.01,250,true},
        {9,6,Vec3d(0),Vec3d(4,-3,9),Vec3d(-1,2,-0.5),Vec3d(0.2,1,0.1),52.5,36,4.25,0.5,40,true}
    };
    for (int kind = 0; kind < 2; ++kind) for (int c = 0; c < 4; ++c) {
        const auto& config = configs[c]; tools::Film film(config.width, config.height);
        std::unique_ptr<tools::BaseCamera> camera;
        if (kind == 0) camera.reset(new tools::PerspectiveCamera(film, config.rotation, config.translation, config.lens, config.aperture, config.nearPlane, config.farPlane));
        else camera.reset(new tools::OrthographicCamera(film, config.rotation, config.translation, config.frameWidth, config.nearPlane, config.farPlane));
        if (config.lookAt) camera->lookAt(config.target, config.up);
        begin("camera"); std::cout << ",\"camera\":" << kind << ",\"configuration\":" << c << ",\"rays\":[";
        const double offsets[][2] = {{0,0},{0.5,0.5},{1,1},{0.125,0.875},{0.999999999,1e-9}};
        bool first = true;
        for (int p = 0; p < 5; ++p) {
            size_t x = p == 0 ? 0 : p == 1 ? size_t(config.width / 2) : p == 2 ? size_t(config.width - 1) : size_t((p * 5 + c) % config.width);
            size_t y = p == 0 ? 0 : p == 1 ? size_t(config.height / 2) : p == 2 ? size_t(config.height - 1) : size_t((p * 3 + c) % config.height);
            auto ray = camera->getRay(x, y, offsets[p][0], offsets[p][1]);
            if (!first) std::cout << ','; first = false;
            std::cout << "{\"x\":" << x << ",\"y\":" << y << ",\"ox\":"; number(offsets[p][0]); std::cout << ",\"oy\":"; number(offsets[p][1]);
            std::cout << ",\"eye\":"; vector3(ray.eye()); std::cout << ",\"direction\":"; vector3(ray.dir()); std::cout << ",\"times\":["; number(ray.t0()); std::cout << ','; number(ray.t1()); std::cout << "]}";
        }
        std::cout << "]}";
    }
}

void constantShaderCases()
{
    const Rgba colors[] = {Rgba(1.0f), Rgba(0.2f,0.4f,0.8f,0.1f), Rgba(-0.25f,1.25f,0.5f,0.75f)};
    const Vec3d positions[] = {Vec3d(0),Vec3d(2,3,4),Vec3d(-1.5,7,0.125)};
    const Vec3d normals[] = {Vec3d(0,0,1),Vec3d(-0.5,0.25,1),Vec3d(0.2,-0.9,0.3)};
    const Vec3d directions[] = {Vec3d(0,0,-1),Vec3d(0,0.6,-0.8),Vec3d(-0.75,0.5,0.125)};
    BBoxd bounds(Vec3d(-2,-3,-4),Vec3d(6,9,12));
    for (int color = 0; color < 3; ++color) for (int sample = 0; sample < 3; ++sample) {
        tools::MatteShader<> matte(colors[color]); tools::NormalShader<> normal(colors[color]);
        tools::PositionShader<> position(bounds, colors[color]); tools::DiffuseShader<> diffuse(colors[color]);
        begin("constantShader"); std::cout << ",\"color\":" << color << ",\"sample\":" << sample << ",\"outputs\":[";
        rgba(matte(positions[sample],normals[sample],directions[sample])); std::cout << ',';
        rgba(normal(positions[sample],normals[sample],directions[sample])); std::cout << ',';
        rgba(position(positions[sample],normals[sample],directions[sample])); std::cout << ',';
        rgba(diffuse(positions[sample],normals[sample],directions[sample])); std::cout << "]}";
    }
}

template<typename Grid> void sampledShaderCase(const char* precision)
{
    using V = typename Grid::ValueType; using S = typename V::ValueType;
    auto grid = Grid::create(V(S(-0.1),S(0.2),S(0.3)));
    grid->setTransform(math::Transform::createLinearTransform(0.75)); grid->transform().postTranslate(Vec3d(1.25,-2.5,0.5));
    for (int x=-2;x<=2;++x) for(int y=-2;y<=2;++y) for(int z=-2;z<=2;++z)
        grid->tree().setValue(Coord(x,y,z),V(S(0.1*x+0.03*y),S(-0.05*y+0.02*z+0.4),S(0.07*z-0.025*x+0.6)));
    BBoxd bounds(Vec3d(-3,-4,-5),Vec3d(7,8,9));
    tools::MatteShader<Grid> matte(*grid); tools::NormalShader<Grid> normal(*grid);
    tools::PositionShader<Grid> position(bounds,*grid); tools::DiffuseShader<Grid> diffuse(*grid);
    const Vec3d index[] = {Vec3d(-2),Vec3d(-0.5,0.5,-1.5),Vec3d(0),Vec3d(1.49,-1.5,1.5),Vec3d(3,0,-4)};
    const Vec3d nml[] = {Vec3d(0,0,1),Vec3d(-0.5,0.25,1),Vec3d(1,0,0),Vec3d(0.2,-0.9,0.3),Vec3d(-1,0.5,0)};
    const Vec3d dir[] = {Vec3d(0,0,-1),Vec3d(0,0.6,-0.8),Vec3d(-1,0,0),Vec3d(-0.75,0.5,0.125),Vec3d(0.5,-0.5,0.5)};
    begin("sampledShader"); std::cout << ",\"precision\":\"" << precision << "\",\"samples\":[";
    for(int i=0;i<5;++i) { if(i)std::cout<<',';Vec3d world=grid->indexToWorld(index[i]);std::cout<<"{\"index\":";vector3(index[i]);std::cout<<",\"outputs\":[";rgba(matte(world,nml[i],dir[i]));std::cout<<',';rgba(normal(world,nml[i],dir[i]));std::cout<<',';rgba(position(world,nml[i],dir[i]));std::cout<<',';rgba(diffuse(world,nml[i],dir[i]));std::cout<<"]}"; }
    std::cout << "]}";
}

std::unique_ptr<tools::BaseShader> renderShader(int mode)
{
    Rgba color(0.9f,0.4f,0.2f,0.35f);
    if(mode==0)return std::unique_ptr<tools::BaseShader>(new tools::MatteShader<>(color));
    if(mode==1)return std::unique_ptr<tools::BaseShader>(new tools::NormalShader<>(color));
    if(mode==2)return std::unique_ptr<tools::BaseShader>(new tools::PositionShader<>(BBoxd(Vec3d(-4),Vec3d(4)),color));
    return std::unique_ptr<tools::BaseShader>(new tools::DiffuseShader<>(color));
}

template<typename Grid> void levelSetCases(const char* precision)
{
    auto grid=tools::createLevelSetSphere<Grid>(2.75f,Vec3f(0.25f,-0.5f,0.125f),0.5f,3.0f,false);
    for(int cameraKind=0;cameraKind<2;++cameraKind)for(int shaderMode=0;shaderMode<4;++shaderMode) {
        tools::Film film(9,7,Rgba(0.02f,0.04f,0.08f,0.3f));std::unique_ptr<tools::BaseCamera> camera;
        if(cameraKind==0)camera.reset(new tools::PerspectiveCamera(film,Vec3d(0),Vec3d(0,0,9),45,36,0.05,30));
        else camera.reset(new tools::OrthographicCamera(film,Vec3d(0),Vec3d(0,0,9),8,0.05,30));
        camera->lookAt(Vec3d(0.25,-0.5,0.125));auto shader=renderShader(shaderMode);size_t samples=(shaderMode&1)?4:1;
        tools::LevelSetRayTracer<Grid> tracer(*grid,*shader,*camera,samples,7703);tracer.render(false);
        begin("levelSet");std::cout<<",\"precision\":\""<<precision<<"\",\"camera\":"<<cameraKind<<",\"shader\":"<<shaderMode<<",\"samples\":"<<samples<<",\"pixels\":";filmPixels(film);std::cout<<'}';
    }
}

template<typename Grid> void volumeCases(const char* precision)
{
    auto grid=tools::createLevelSetSphere<Grid>(2.75f,Vec3f(0.25f,-0.5f,0.125f),0.5f,3.0f,false);tools::sdfToFogVolume(*grid);
    using Intersector=tools::VolumeRayIntersector<Grid>;
    for(int cameraKind=0;cameraKind<2;++cameraKind)for(int mode=0;mode<2;++mode) {
        tools::Film film(9,7,Rgba(0.4f));std::unique_ptr<tools::BaseCamera> camera;
        if(cameraKind==0)camera.reset(new tools::PerspectiveCamera(film,Vec3d(0),Vec3d(0,0,9),45,36,0.05,30));
        else camera.reset(new tools::OrthographicCamera(film,Vec3d(0),Vec3d(0,0,9),8,0.05,30));
        camera->lookAt(Vec3d(0.25,-0.5,0.125));Intersector intersector(*grid);tools::VolumeRender<Intersector> renderer(intersector,*camera);
        if(mode){renderer.setPrimaryStep(0.625);renderer.setShadowStep(1.375);renderer.setLightDir(-0.2,0.7,0.4);renderer.setLightColor(0.5,0.8,0.3);renderer.setAbsorption(0.2,0.05,0.125);renderer.setScattering(1.1,1.7,0.9);renderer.setLightGain(0.35);renderer.setCutOff(0.0025);}
        renderer.render(false);begin("volume");std::cout<<",\"precision\":\""<<precision<<"\",\"camera\":"<<cameraKind<<",\"mode\":"<<mode<<",\"pixels\":";filmPixels(film);std::cout<<'}';
    }
}

int main()
{
    openvdb::initialize();tbb::global_control threads(tbb::global_control::max_allowed_parallelism,1);
    std::cout<<std::setprecision(17)<<"{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[\n";
    filmCases();cameraCases();constantShaderCases();sampledShaderCase<Vec3SGrid>("float");sampledShaderCase<Vec3DGrid>("double");
    levelSetCases<FloatGrid>("float");levelSetCases<DoubleGrid>("double");volumeCases<FloatGrid>("float");volumeCases<DoubleGrid>("double");
    std::cout<<"]}\n";return 0;
}