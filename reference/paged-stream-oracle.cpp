// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/points/StreamCompression.h>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <numeric>
#include <sstream>
#include <stdexcept>
#include <vector>
using namespace openvdb;
using namespace openvdb::compression;
namespace fs = std::filesystem;

static std::vector<int> sizes(int profile)
{
    static const int thresholds[] = {1, 47, 48, 60, 127, 128, 129, 4096};
    if (profile < 8) return {thresholds[profile]};
    if (profile == 8) return {PageSize};
    if (profile == 9) return {PageSize + 1};
    if (profile == 10) return {PageSize / 3, PageSize / 3, PageSize / 3, PageSize / 3, 1};
    if (profile == 11) return {PageSize - 1, 2};
    if (profile == 12) return {200, 200, PageSize};
    if (profile == 13) return {PageSize + 1, PageSize + 1, 3};
    std::vector<int> result;
    int count = 2 + profile % 5;
    for (int i = 0; i < count; ++i) result.push_back(17 + ((profile * 7919 + i * 104729) % 50000));
    return result;
}
static int mode(int profile)
{
    if (profile >= 8 && profile <= 13) return 0;
    return profile % 4;
}
static std::vector<std::vector<uint8_t>> blocks(int profile)
{
    auto lengths = sizes(profile);
    std::vector<std::vector<uint8_t>> result;
    for (size_t block = 0; block < lengths.size(); ++block) {
        std::vector<uint8_t> value(static_cast<size_t>(lengths[block]), uint8_t(0));
        const int kind = mode(profile);
        if (kind == 0) std::fill(value.begin(), value.end(), uint8_t((profile * 13 + int(block) * 7) & 255));
        else if (kind == 1) for (size_t i = 0; i < value.size(); ++i) value[i] = uint8_t((i + block * 17 + profile) & 255);
        else if (kind == 2) {
            uint32_t state = 0x9e3779b9u ^ uint32_t(profile * 65537 + int(block));
            for (auto& byte : value) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; byte = uint8_t(state); }
        } else for (size_t i = 0; i < value.size(); ++i) value[i] = uint8_t(((i / 31) + block * 7 + profile) & 255);
        result.push_back(std::move(value));
    }
    return result;
}
static std::pair<std::string, std::string> encode(const std::vector<std::vector<uint8_t>>& values)
{
    std::ostringstream headers(std::ios::binary), data(std::ios::binary);
    PagedOutputStream sizeWriter(headers); sizeWriter.setSizeOnly(true);
    PagedOutputStream dataWriter(data);
    for (const auto& value : values) {
        sizeWriter.write(reinterpret_cast<const char*>(value.data()), std::streamsize(value.size()));
        dataWriter.write(reinterpret_cast<const char*>(value.data()), std::streamsize(value.size()));
    }
    sizeWriter.flush(); dataWriter.flush();
    return {headers.str(), data.str()};
}
static void verify(const std::string& encoded, const std::vector<std::vector<uint8_t>>& values)
{
    std::stringstream stream(encoded, std::ios::in | std::ios::binary);
    PagedInputStream sizesReader(stream); sizesReader.setSizeOnly(true);
    std::vector<PageHandle::Ptr> handles;
    for (const auto& value : values) handles.push_back(sizesReader.createHandle(std::streamsize(value.size())));
    PagedInputStream dataReader(stream);
    for (size_t i = 0; i < values.size(); ++i) dataReader.read(handles[i], std::streamsize(values[i].size()), false);
    for (size_t i = 0; i < values.size(); ++i) {
        auto actual = handles[i]->read();
        if (!std::equal(values[i].begin(), values[i].end(), reinterpret_cast<const uint8_t*>(actual.get())))
            throw std::runtime_error("Paged stream readback differs");
    }
    if (stream.tellg() != std::streampos(encoded.size())) throw std::runtime_error("Paged stream did not consume exact bytes");
}
static void hex(std::ostream& out, const std::string& bytes)
{
    static constexpr char digits[] = "0123456789abcdef";
    out << '"';
    for (unsigned char value : bytes) out << digits[value >> 4] << digits[value & 15];
    out << '"';
}
int main(int argc, char** argv)
{
    try {
        if (argc != 3) return 2;
        initialize();
        const std::string operation = argv[1];
        if (operation == "generate") {
            std::ofstream out(argv[2], std::ios::binary);
            out << "{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
            for (int profile = 0; profile < 25; ++profile) {
                auto values = blocks(profile);
                auto encoded = encode(values);
                verify(encoded.first + encoded.second, values);
                if (profile) out << ',';
                out << "{\"profile\":" << profile << ",\"mode\":" << mode(profile) << ",\"sizes\":[";
                auto lengths = sizes(profile);
                for (size_t i = 0; i < lengths.size(); ++i) { if (i) out << ','; out << lengths[i]; }
                out << "],\"headers\":"; hex(out, encoded.first);
                out << ",\"data\":"; hex(out, encoded.second);
                out << '}';
            }
            out << "]}\n";
            return out.good() ? 0 : 3;
        }
        if (operation == "verify") {
            int verified = 0;
            for (int profile = 0; profile < 25; ++profile) {
                fs::path path = fs::path(argv[2]) / (std::to_string(profile) + ".bin");
                std::ifstream input(path, std::ios::binary);
                if (!input) throw std::runtime_error("Missing managed paged stream");
                std::string bytes((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
                verify(bytes, blocks(profile)); ++verified;
            }
            std::cout << "Verified " << verified << " managed paged streams\n";
            return 0;
        }
        return 2;
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
