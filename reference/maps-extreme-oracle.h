// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Included by maps-oracle.cpp, using its native map wire serializer.
void extremeNumber(std::ostream& out,double value) {
    if(std::isfinite(value))out<<value;
    else out<<'"'<<(std::isnan(value)?"NaN":value>0?"Infinity":"-Infinity")<<'"';
}
void extremeVector(std::ostream& out,const Vec3d& value) {
    out<<'[';for(int i=0;i<3;i++){if(i)out<<',';extremeNumber(out,value[i]);}out<<']';
}
void generateExtremeMaps(const fs::path& path) {
    std::ofstream out(path,std::ios::binary);out<<std::setprecision(17)<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
    const double maximum=std::numeric_limits<double>::max();
    const std::vector<Vec3d> scales={Vec3d(maximum),Vec3d(-maximum),Vec3d(1e200),Vec3d(-1e200),Vec3d(maximum,2,.5),Vec3d(1e200,1e200,1e-100)};
    int count=0;
    for(size_t i=0;i<scales.size();i++)for(int kind=0;kind<(i<4?4:2);kind++) {
        MapBase::Ptr map;
        if(kind==0)map.reset(new ScaleMap(scales[i]));
        if(kind==1)map.reset(new ScaleTranslateMap(scales[i],Vec3d(.25,-.5,.75)));
        if(kind==2)map.reset(new UniformScaleMap(scales[i][0]));
        if(kind==3)map.reset(new UniformScaleTranslateMap(scales[i][0],Vec3d(.25,-.5,.75)));
        if(count++)out<<',';
        out<<"{\"type\":\""<<map->type()<<"\",\"scale\":";extremeVector(out,scales[i]);
        out<<",\"determinant\":";extremeNumber(out,map->determinant());out<<",\"voxelSize\":";extremeVector(out,map->voxelSize());
        out<<",\"wire\":\""<<hex(wire(map))<<"\",\"samples\":[";
        const std::vector<Vec3d> points={Vec3d(0),Vec3d(1),Vec3d(-1),Vec3d(.5),Vec3d(std::numeric_limits<double>::min())};
        for(size_t j=0;j<points.size();j++){if(j)out<<',';out<<"{\"point\":";extremeVector(out,points[j]);out<<",\"mapped\":";extremeVector(out,map->applyMap(points[j]));out<<",\"inverse\":";extremeVector(out,map->applyInverseMap(points[j]));out<<'}';}
        out<<"]}";
    }
    out<<"]}\n";if(!out.good())throw std::runtime_error("Extreme map fixture write failed");std::cout<<"Generated "<<count<<" finite extreme-scale profiles\n";
}
