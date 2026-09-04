// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/io/File.h>
#include <openvdb/Metadata.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iomanip>
using namespace openvdb;
namespace fs=std::filesystem;
static void patchUuid(const fs::path& path,const std::string& uuid){std::fstream f(path,std::ios::in|std::ios::out|std::ios::binary);f.seekp(21);f.write(uuid.data(),36);if(!f)throw std::runtime_error("UUID patch failed");}
static void writeFile(const fs::path& path,int value){auto grid=FloatGrid::create(-2.5f);grid->setName("density");grid->tree().setValueOn(Coord(-1,2,3),float(value)+.25f);GridPtrVec grids{grid};MetaMap meta;meta.insertMeta("marker",StringMetadata("lifecycle"));io::File file(path.string());file.setCompression(io::COMPRESS_NONE);file.setGridStatsMetadataEnabled(false);file.write(grids,meta);}
static bool throwsOpen(io::File& file){try{file.open(false);return false;}catch(const openvdb::Exception&){return true;}}
static bool throwsClosedRead(io::File& file){try{file.getMetadata();return false;}catch(const openvdb::Exception&){return true;}}
static void generate(const fs::path& directory){fs::create_directories(directory);fs::path filePath=directory/"native-file-lifecycle.vdb";writeFile(filePath,7);
 io::File original(filePath.string());const bool initialIdentical=original.isIdentical("");const bool first=original.open(false);const std::string firstTag=original.getUniqueTag();const auto firstSize=original.getSize();const bool doubleOpen=throwsOpen(original);
 io::File copied(original);const bool copyClosed=!copied.isOpen();const bool copyTag=copied.getUniqueTag()==firstTag;const bool copyClosedRead=throwsClosedRead(copied);const bool copySame=!copied.open(false);const bool copyGrid=copied.readGrid("density")->baseTree().activeVoxelCount()==1;copied.close();
 original.close();original.close();const bool same=!original.open(false);original.close();
 const std::string fixed="12345678-1234-1234-1234-123456789ABC";patchUuid(filePath,fixed);const bool changed=original.open(false);const bool patchedTag=original.getUniqueTag()==fixed;original.close();const bool patchedSame=!original.open(false);
 io::File patchedCopy(original);original.close();const bool patchedCopySame=!patchedCopy.open(false);patchedCopy.close();
 writeFile(filePath,9);const bool rewriteChanged=original.open(false);const std::string finalTag=original.getUniqueTag();const bool rewrittenValue=gridPtrCast<FloatGrid>(original.readGrid("density"))->tree().getValue(Coord(-1,2,3))==9.25f;original.close();
 std::ofstream out(directory/"native-file-lifecycle.json.tmp",std::ios::binary);out<<"{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"firstTag\":\""<<firstTag<<"\",\"fixedTag\":\""<<fixed<<"\",\"finalTag\":\""<<finalTag<<"\",\"size\":"<<firstSize<<",\"events\":{";
 out<<"\"initialIdentical\":"<<initialIdentical<<",\"firstChanged\":"<<first<<",\"doubleOpenThrows\":"<<doubleOpen<<",\"copyClosed\":"<<copyClosed<<",\"copyTag\":"<<copyTag<<",\"copyClosedReadThrows\":"<<copyClosedRead<<",\"copySame\":"<<copySame<<",\"copyGrid\":"<<copyGrid<<",\"same\":"<<same<<",\"changed\":"<<changed<<",\"patchedTag\":"<<patchedTag<<",\"patchedSame\":"<<patchedSame<<",\"patchedCopySame\":"<<patchedCopySame<<",\"rewriteChanged\":"<<rewriteChanged<<",\"rewrittenValue\":"<<rewrittenValue<<"}}\n";out.close();fs::rename(directory/"native-file-lifecycle.json.tmp",directory/"native-file-lifecycle.json");std::cout<<"Generated native File lifecycle fixture\n";}
static void replaceFile(const fs::path& source,const fs::path& target){std::ifstream in(source,std::ios::binary);std::ofstream out(target,std::ios::binary|std::ios::trunc);out<<in.rdbuf();if(!out)throw std::runtime_error("replace failed");}
static void verify(const fs::path& first,const fs::path& second,const fs::path& work){fs::copy_file(first,work,fs::copy_options::overwrite_existing);io::File file(work.string());if(!file.open(false))throw std::runtime_error("first managed file not new");const auto tag=file.getUniqueTag();auto grid=gridPtrCast<FloatGrid>(file.readGrid("managed"));if(!grid||grid->tree().getValue(Coord(1,2,3))!=1.25f)throw std::runtime_error("first managed grid mismatch");io::File copy(file);file.close();if(copy.open(false))throw std::runtime_error("copied native File lost UUID");copy.close();replaceFile(second,work);if(!file.open(false)||file.getUniqueTag()==tag)throw std::runtime_error("managed rewrite UUID not detected");grid=gridPtrCast<FloatGrid>(file.readGrid("managed"));if(!grid||grid->tree().getValue(Coord(1,2,3))!=2.25f)throw std::runtime_error("second managed grid mismatch");std::cout<<"Verified native reopen/copy over two managed UUIDs\n";}
int main(int argc,char**argv){try{initialize();if(argc==3&&std::string(argv[1])=="generate")generate(argv[2]);else if(argc==5&&std::string(argv[1])=="verify")verify(argv[2],argv[3],argv[4]);else return 2;return 0;}catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 1;}}
