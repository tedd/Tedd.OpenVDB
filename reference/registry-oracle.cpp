// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#define main builtin_metadata_main
#include "metadata-oracle.cpp"
#undef main
#include <cstring>

std::ofstream results; int observations = 0;
void result(const std::string& name,const std::string& value){if(observations++)results<<",\n";results<<"{\"name\":\""<<name<<"\",\"value\":\""<<value<<"\"}";}
void truth(const std::string& name,bool value){result(name,value?"true":"false");}
std::string bytes(const openvdb::MetaMap& map){std::ostringstream stream(std::ios::binary);map.writeMeta(stream);return stream.str();}
std::string bytes(const openvdb::Metadata& value){std::ostringstream stream(std::ios::binary);value.write(stream);return stream.str().substr(4);}

template<bool Temporary>class MaterialMetadata final:public openvdb::Metadata {
public:
 int32_t material=0; float weight=0;
 MaterialMetadata()=default; MaterialMetadata(int32_t m,float w):material(m),weight(w){}
 openvdb::Name typeName()const override{return Temporary?"__voxel_temporary":"voxel_material";}
 static Ptr create(){return Ptr(new MaterialMetadata);}
 Ptr copy()const override{return Ptr(new MaterialMetadata(material,weight));}
 void copy(const Metadata& other)override{auto p=dynamic_cast<const MaterialMetadata*>(&other);if(!p)throw std::runtime_error("wrong custom type");material=p->material;weight=p->weight;}
 std::string str()const override{std::ostringstream stream;stream<<material<<'@'<<weight;return stream.str();}
 bool asBool()const override{return material!=0||weight!=0;}
 openvdb::Index32 size()const override{return 8;}
protected:
 void readValue(std::istream& stream,openvdb::Index32 count)override{if(count!=8)throw std::runtime_error("custom payload size");stream.read(reinterpret_cast<char*>(&material),4);stream.read(reinterpret_cast<char*>(&weight),4);}
 void writeValue(std::ostream& stream)const override{stream.write(reinterpret_cast<const char*>(&material),4);stream.write(reinterpret_cast<const char*>(&weight),4);}
};
using Material=MaterialMetadata<false>;using Temporary=MaterialMetadata<true>;
void registerCustom(){openvdb::Metadata::registerType("voxel_material",Material::create);openvdb::Metadata::registerType("__voxel_temporary",Temporary::create);}
void unregisterCustom(){openvdb::Metadata::unregisterType("voxel_material");openvdb::Metadata::unregisterType("__voxel_temporary");}
void saveArchive(const fs::path& path,const openvdb::GridPtrVec& grids,const openvdb::MetaMap& metadata,int compression){openvdb::io::File file(path.string());file.setCompression(compression);file.setGridStatsMetadataEnabled(false);file.write(grids,metadata);}
void generateRegistry(const fs::path& directory){
 fs::create_directories(directory);results.open(directory/"native-registry.json.tmp");results<<"[\n";
 for(const char* type:{"bool","int32","int64","half","float","double","string","vec2i","vec2s","vec2d","vec3i","vec3s","vec3d","vec4i","vec4s","vec4d","mat4s","mat4d","ptidx32","ptidx64"}){auto value=openvdb::Metadata::createMetadata(type);result(std::string("default-type-")+type,value->typeName());result(std::string("default-bytes-")+type,hex(bytes(*value)));}
 registerCustom();truth("registered",openvdb::Metadata::isRegisteredType("voxel_material"));
 try{openvdb::Metadata::registerType("voxel_material",Material::create);truth("duplicate-rejected",false);}catch(const openvdb::KeyError&){truth("duplicate-rejected",true);}
 try{openvdb::Metadata::createMetadata("missing_type");truth("missing-rejected",false);}catch(const openvdb::LookupError&){truth("missing-rejected",true);}
 auto defaultCustom=openvdb::Metadata::createMetadata("voxel_material");result("custom-default-bytes",hex(bytes(*defaultCustom)));result("custom-default-text",hex(defaultCustom->str()));truth("custom-default-bool",defaultCustom->asBool());
 Material material(17,1.25f);result("custom-bytes",hex(bytes(material)));result("custom-text",hex(material.str()));truth("custom-bool",material.asBool());
 openvdb::MetaMap map;map.insertMeta("z",openvdb::StringMetadata(""));map.insertMeta("material",material);map.insertMeta("a",openvdb::Int32Metadata(11));map.insertMeta("\xee\x80\x80",openvdb::Int32Metadata(2));map.insertMeta("\xf0\x90\x80\x80",openvdb::Int32Metadata(3));
 result("map-wire",hex(bytes(map)));result("map-text",hex(map.str("  ")));truth("missing-null",!map["absent"]);
 auto shallow=map.copyMeta();auto deep=map.deepCopyMeta();truth("shallow-shared",(*shallow)["a"]==map["a"]);truth("deep-distinct",(*deep)["a"]!=map["a"]);truth("deep-equal",*deep==map);
 map.insertMeta("a",openvdb::Int32Metadata(44));truth("replacement-detaches",(*shallow)["a"]!=map["a"]);truth("replacement-old-value",shallow->metaValue<int32_t>("a")==11);
 try{map.insertMeta("a",openvdb::FloatMetadata(1));truth("type-conflict",false);}catch(const openvdb::TypeError&){truth("type-conflict",true);}
 try{map.insertMeta("",material);truth("empty-name",false);}catch(const openvdb::ValueError&){truth("empty-name",true);}
 openvdb::MetaMap incoming;incoming.insertMeta("b",openvdb::Int32Metadata(5));incoming.insertMeta("z",openvdb::Int32Metadata(6));try{map.insertMeta(incoming);truth("map-conflict",false);}catch(const openvdb::TypeError&){truth("map-conflict",true);}truth("partial-insert",bool(map["b"]));
 map=map;truth("self-assign-retains",map.metaCount()==6);map.removeMeta("absent");truth("remove-missing-retains",map.metaCount()==6);
 float nanA,nanB;uint32_t abits=0x7fc12345,bbits=0x7fc12346;std::memcpy(&nanA,&abits,4);std::memcpy(&nanB,&bbits,4);
 truth("float-signed-zero",openvdb::FloatMetadata(0.0f)==openvdb::FloatMetadata(-0.0f));truth("float-nan-same",openvdb::FloatMetadata(nanA)==openvdb::FloatMetadata(nanA));truth("float-nan-different",openvdb::FloatMetadata(nanA)==openvdb::FloatMetadata(nanB));
 truth("double-signed-zero",openvdb::DoubleMetadata(0.0)==openvdb::DoubleMetadata(-0.0));truth("half-signed-zero",openvdb::HalfMetadata(openvdb::math::half(0.0f))==openvdb::HalfMetadata(openvdb::math::half(-0.0f)));
 truth("vec-nan-same",openvdb::Vec3SMetadata(openvdb::Vec3s(nanA))==openvdb::Vec3SMetadata(openvdb::Vec3s(nanA)));truth("vec-nan-different",openvdb::Vec3SMetadata(openvdb::Vec3s(nanA))==openvdb::Vec3SMetadata(openvdb::Vec3s(nanB)));
 openvdb::UnknownMetadata raw("voxel_material");raw.setValue(openvdb::UnknownMetadata::ByteVec{17,0,0,0,0,0,160,63});truth("custom-opaque-equal",raw==material);
 auto grid=openvdb::FloatGrid::create();grid->setName("native_registry");grid->tree().setValueOn(openvdb::Coord(-1,2,3),7.25f);grid->insertMeta("material",material);grid->insertMeta("temporary",Temporary(8,-2.5f));openvdb::UnknownMetadata opaque("unregistered_persistent");opaque.setValue({0,1,127,255});grid->insertMeta("opaque",opaque);openvdb::MetaMap fm;fm.insertMeta("file_material",Material(22,4.5f));
 for(int compression:{0,4,6})saveArchive(directory/("native-registry-"+std::to_string(compression)+".vdb"),{grid},fm,compression);
 unregisterCustom();truth("unregistered",!openvdb::Metadata::isRegisteredType("voxel_material"));openvdb::Metadata::unregisterType("missing_type");truth("unregister-missing",true);
 std::istringstream untypedInput(bytes(map),std::ios::binary);openvdb::MetaMap untyped;untyped.readMeta(untypedInput);truth("unknown-preserved",bool(dynamic_cast<openvdb::UnknownMetadata*>(untyped["material"].get())));truth("unknown-byte-equality",*untyped["material"]==material);
 for(int compression:{0,4,6}){std::ifstream source(directory/("native-registry-"+std::to_string(compression)+".vdb"),std::ios::binary);openvdb::io::Stream file(source,false);auto grids=file.getGrids();truth("unknown-temporary-dropped-"+std::to_string(compression),!(*grids)[0]->operator[]("temporary"));saveArchive(directory/("native-unknown-"+std::to_string(compression)+".vdb"),*grids,*file.getMetadata(),compression);}
 registerCustom();openvdb::MetaMap aliases;for(auto pair:std::initializer_list<std::pair<const char*,size_t>>{{"ptidx32",4},{"ptidx64",8}}){openvdb::UnknownMetadata alias(pair.first);openvdb::UnknownMetadata::ByteVec value(pair.second,0);value[0]=47;alias.setValue(value);aliases.insertMeta(pair.first,alias);}std::istringstream aliasInput(bytes(aliases),std::ios::binary);openvdb::MetaMap decoded;decoded.readMeta(aliasInput);result("alias-wire",hex(bytes(aliases)));result("alias32-type",decoded["ptidx32"]->typeName());result("alias64-type",decoded["ptidx64"]->typeName());
 openvdb::Metadata::unregisterType("float");openvdb::MetaMap known;known.insertMeta("float",openvdb::FloatMetadata(nanA));result("unregistered-float-wire",hex(bytes(known)));std::istringstream knownInput(bytes(known),std::ios::binary);openvdb::MetaMap readKnown;readKnown.readMeta(knownInput);truth("unregistered-builtin-opaque",bool(dynamic_cast<openvdb::UnknownMetadata*>(readKnown["float"].get())));
 openvdb::Metadata::clearRegistry();truth("clear-removes-builtin",!openvdb::Metadata::isRegisteredType("int32"));truth("clear-removes-custom",!openvdb::Metadata::isRegisteredType("voxel_material"));
 results<<"\n]\n";results.close();fs::path target=directory/"native-registry.json";if(fs::exists(target))fs::remove(target);fs::rename(directory/"native-registry.json.tmp",target);std::cout<<"Generated "<<observations<<" native registry/map observations\n";
}
int main(int argc,char** argv){try{openvdb::initialize();if(argc==3&&std::string(argv[1])=="generate")generateRegistry(argv[2]);else if(argc==5&&std::string(argv[1])=="verify"){if(std::string(argv[4])=="registered")registerCustom();else if(std::string(argv[4])!="unknown")throw std::runtime_error("registry mode");verify(argv[2],argv[3]);}else throw std::runtime_error("usage: registry-reference generate directory | verify actual expected registered|unknown");return 0;}catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}}
