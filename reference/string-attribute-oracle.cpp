// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/points/AttributeArrayString.h>
#include <openvdb/io/io.h>
#include <algorithm>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <string_view>
#include <vector>

using namespace openvdb;
using namespace openvdb::points;
namespace fs = std::filesystem;

static std::ofstream output;
static fs::path managedDirectory;
static bool generating = true;
static bool first = true;
static int streamCase = 0;
static int verified = 0;

static void jsonString(std::string_view value)
{
    output << '"';
    for (unsigned char c : value) {
        if (c == '"' || c == '\\') output << '\\' << char(c);
        else if (c < 32) output << "\\u00" << std::hex << std::setw(2)
            << std::setfill('0') << int(c) << std::dec;
        else output << char(c);
    }
    output << '"';
}

static void hex(const char* bytes, size_t size)
{
    static constexpr char digits[] = "0123456789abcdef";
    output << '"';
    for (size_t i = 0; i < size; ++i) {
        const unsigned c = static_cast<unsigned char>(bytes[i]);
        output << digits[c >> 4] << digits[c & 15];
    }
    output << '"';
}

static void cacheSnapshot(const char* name, const MetaMap& metadata)
{
    StringMetaCache cache(metadata);
    std::vector<std::pair<Name, Index>> entries(cache.map().begin(), cache.map().end());
    std::sort(entries.begin(), entries.end());
    if (!first) output << ',';
    first = false;
    output << "{\"kind\":\"cache\",\"name\":";
    jsonString(name);
    output << ",\"entries\":[";
    for (size_t i = 0; i < entries.size(); ++i) {
        if (i) output << ',';
        output << '[';
        jsonString(entries[i].first);
        output << ',' << entries[i].second << ']';
    }
    output << "]}";
}

static void caches()
{
    MetaMap metadata;
    cacheSnapshot("empty", metadata);
    metadata.insertMeta("string:0", StringMetadata("alpha"));
    cacheSnapshot("one", metadata);
    metadata.insertMeta("string:5", Int32Metadata(7));
    metadata.insertMeta("other", StringMetadata("ignored"));
    cacheSnapshot("ignored", metadata);
    metadata.insertMeta("string:9", StringMetadata("alpha"));
    metadata.insertMeta("string:0002", StringMetadata("beta"));
    metadata.insertMeta("string:17", StringMetadata(u8"p\u00E5\u96EA"));
    cacheSnapshot("duplicates", metadata);
}

static void inserter()
{
    MetaMap metadata;
    metadata.insertMeta("string:0", StringMetadata("A"));
    metadata.insertMeta("string:2", StringMetadata("C"));
    metadata.insertMeta("irrelevant", Int32Metadata(4));
    StringMetaInserter inserter(metadata);
    std::vector<std::pair<std::string, Index>> operations;
    auto add = [&](const char* value, Index hint = 0) {
        operations.emplace_back(value, inserter.insert(value, hint));
    };
    add("A");
    add("B");
    add("D", 1000);
    add("E", 1000);
    metadata.removeMeta("string:1");
    inserter.resetCache();
    add("F");
    add("G", 999);
    add("H", 1001);
    add("");

    if (!first) output << ',';
    first = false;
    output << "{\"kind\":\"inserter\",\"ops\":[";
    for (size_t i = 0; i < operations.size(); ++i) {
        if (i) output << ',';
        output << '[';
        jsonString(operations[i].first);
        output << ',' << operations[i].second << ']';
    }
    output << "],\"metadata\":[";
    bool firstMeta = true;
    for (auto it = metadata.beginMeta(); it != metadata.endMeta(); ++it) {
        const auto* string = dynamic_cast<const StringMetadata*>(it->second.get());
        if (!string || it->first.compare(0, 7, "string:")) continue;
        if (!firstMeta) output << ',';
        firstMeta = false;
        output << '[';
        jsonString(it->first);
        output << ',';
        jsonString(string->value());
        output << ']';
    }
    output << "]}";
}

static MetaMap stringMetadata()
{
    MetaMap metadata;
    metadata.insertMeta("string:0", StringMetadata("one"));
    metadata.insertMeta("string:1", StringMetadata("two"));
    metadata.insertMeta("string:9", StringMetadata("ten"));
    metadata.insertMeta("string:101", StringMetadata("hundred-two"));
    metadata.insertMeta("string:4463", StringMetadata("wrapped-70000"));
    metadata.insertMeta("string:65534", StringMetadata("wrapped-max"));
    metadata.insertMeta("string:4294967294", StringMetadata("maximum"));
    return metadata;
}

template<bool Truncated>
static void verifyManagedStream(
    const fs::path& path,
    const TypedAttributeArray<Index, StringCodec<Truncated>>& expected)
{
    using Array = TypedAttributeArray<Index, StringCodec<Truncated>>;
    std::ifstream stream(path, std::ios::binary);
    if (!stream) throw std::runtime_error("Missing managed string stream: " + path.string());

    Array actual;
    actual.read(stream);
    if (!stream || stream.peek() != EOF) {
        throw std::runtime_error("Native reader did not consume exactly one string stream: " + path.string());
    }
    if (actual.size() != expected.size() ||
        actual.stride() != expected.stride() ||
        actual.dataSize() != expected.dataSize() ||
        actual.flags() != expected.flags() ||
        actual.isUniform() != expected.isUniform()) {
        throw std::runtime_error("Managed/native string attribute metadata mismatch: " + path.string());
    }

    const size_t bytes = (expected.isUniform() ? 1 : expected.dataSize()) * expected.storageTypeSize();
    if (std::memcmp(actual.constDataAsByteArray(), expected.constDataAsByteArray(), bytes) != 0) {
        throw std::runtime_error("Managed/native string attribute storage mismatch: " + path.string());
    }
    for (Index i = 0; i < expected.dataSize(); ++i) {
        if (actual.get(i) != expected.get(i)) {
            throw std::runtime_error("Managed/native string attribute index mismatch: " + path.string());
        }
    }
    ++verified;
}

template<bool Truncated>
static void streams()
{
    using Codec = StringCodec<Truncated>;
    using Array = TypedAttributeArray<Index, Codec>;
    const std::vector<std::vector<Index>> sets = {
        {},
        { 0 },
        { 1, 2, 10, 102 },
        { 0, 1, 2, 10, 102, 1, 0, 2, 10, 102, 1, 0, 2 },
        { 70000, 4294967295u, 65537, 0, 102 },
        { 1, 1, 1, 1, 1, 1, 1, 1 }
    };

    for (int profile = 0; profile < 8; ++profile, ++streamCase) {
        const Index length = profile == 0 ? 0 : profile == 3 ? 7 :
            profile == 4 ? 5 : profile == 5 ? 64 : profile == 6 ? 7 : 11;
        const Index strideOrTotal = profile == 3 ? 3 : profile == 4 ? 13 : 1;
        const bool constant = profile != 4;
        const Index initial = profile == 1 ? 1 : 0;

        Array array(length, strideOrTotal, constant, initial);
        if (profile >= 2) {
            array.expand();
            const auto& values = sets[
                profile == 2 ? 2 : profile == 5 ? 5 : profile == 6 ? 4 : 3];
            for (Index i = 0; i < array.dataSize(); ++i) {
                array.set(i, values[size_t(i) % values.size()]);
            }
        }
        if (profile == 6) {
            array.setHidden(true);
            array.setStreaming(true);
        }
        if (profile == 7) array.setTransient(true);

        if (!generating) {
            verifyManagedStream(
                managedDirectory / (std::to_string(streamCase) + "-raw.bin"), array);
            verifyManagedStream(
                managedDirectory / (std::to_string(streamCase) + "-blosc.bin"), array);
            continue;
        }

        if (!first) output << ',';
        first = false;
        output << "{\"kind\":\"stream\",\"id\":" << streamCase
            << ",\"truncated\":" << Truncated
            << ",\"profile\":" << profile
            << ",\"n\":" << length
            << ",\"s\":" << strideOrTotal
            << ",\"constant\":" << constant
            << ",\"initial\":" << initial
            << ",\"expanded\":" << (profile >= 2)
            << ",\"size\":" << array.size()
            << ",\"stride\":" << array.stride()
            << ",\"dataSize\":" << array.dataSize()
            << ",\"storageSize\":" << array.storageTypeSize()
            << ",\"flags\":" << int(array.flags())
            << ",\"uniform\":" << array.isUniform()
            << ",\"inputs\":[";
        if (profile >= 2) {
            const auto& values = sets[
                profile == 2 ? 2 : profile == 5 ? 5 : profile == 6 ? 4 : 3];
            for (Index i = 0; i < array.dataSize(); ++i) {
                if (i) output << ',';
                output << values[size_t(i) % values.size()];
            }
        }
        output << "],\"indices\":[";
        for (Index i = 0; i < array.dataSize(); ++i) {
            if (i) output << ',';
            output << array.get(i);
        }
        output << "],\"storage\":";
        hex(
            array.constDataAsByteArray(),
            (array.isUniform() ? 1 : array.dataSize()) * array.storageTypeSize());

        for (int compressed = 0; compressed < 2; ++compressed) {
            std::ostringstream stream(std::ios::binary);
            io::setDataCompression(
                stream, compressed ? io::COMPRESS_BLOSC : io::COMPRESS_NONE);
            array.write(stream, true);
            const std::string bytes = stream.str();
            output << (compressed ? ",\"blosc\":" : ",\"raw\":");
            hex(bytes.data(), bytes.size());

            std::istringstream input(bytes, std::ios::binary);
            Array roundtrip;
            roundtrip.read(input);
            if (!input || !(roundtrip == array)) {
                throw std::runtime_error("Native string stream self-check failed");
            }
        }
        output << '}';
    }
}

static void handles()
{
    MetaMap metadata;
    StringAttributeArray array(4);
    StringAttributeWriteHandle handle(array, metadata);
    bool stale = false;
    metadata.insertMeta("string:0", StringMetadata("one"));
    metadata.insertMeta("string:1", StringMetadata("two"));
    try {
        handle.set(0, "one");
    } catch (const openvdb::Exception&) {
        stale = true;
    }

    handle.resetCache();
    handle.set(0, "one");
    handle.set(1, "two");
    handle.set(2, "one");
    handle.set(3, "");
    std::vector<Name> initial;
    for (Index i = 0; i < 4; ++i) initial.push_back(handle.get(i));
    const bool compact1 = handle.compact();
    handle.set(1, "one");
    handle.set(3, "one");
    const bool compact2 = handle.compact();
    handle.expand();
    handle.set(2, "two");
    handle.fill("two");
    handle.collapse("one");
    handle.expand();
    handle.set(3, "two");

    StringAttributeHandle live(array, metadata);
    metadata.removeMeta("string:1");
    bool missing = false;
    try {
        live.get(3);
    } catch (const openvdb::Exception&) {
        missing = true;
    }
    metadata.insertMeta("string:1", StringMetadata("changed"));
    const Name changed = live.get(3);

    if (!first) output << ',';
    first = false;
    output << "{\"kind\":\"handle\",\"stale\":" << stale << ",\"initial\":[";
    for (size_t i = 0; i < initial.size(); ++i) {
        if (i) output << ',';
        jsonString(initial[i]);
    }
    output << "],\"compact1\":" << compact1
        << ",\"compact2\":" << compact2
        << ",\"missing\":" << missing
        << ",\"changed\":";
    jsonString(changed);
    output << ",\"indices\":[";
    for (Index i = 0; i < 4; ++i) {
        if (i) output << ',';
        output << array.get(i);
    }
    output << "]}";
}

static void malformed()
{
    const std::vector<std::string> keys = {
        "string:",
        "string:abc",
        "string:999999999999999999999999"
    };
    for (const auto& key : keys) {
        MetaMap metadata;
        metadata.insertMeta(key, StringMetadata("bad"));
        bool error = false;
        try {
            StringMetaCache cache(metadata);
        } catch (const std::exception&) {
            error = true;
        }

        if (!first) output << ',';
        first = false;
        output << "{\"kind\":\"malformed\",\"key\":";
        jsonString(key);
        output << ",\"error\":" << error << '}';
    }
}

int main(int argc, char** argv)
{
    try {
        if (argc != 3) return 2;
        initialize();
        const std::string mode = argv[1];
        generating = mode == "generate";
        if (generating) {
            output.open(argv[2], std::ios::binary);
            output << "{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
            caches();
            inserter();
        } else if (mode == "verify") {
            managedDirectory = argv[2];
        } else {
            return 2;
        }

        streams<false>();
        streams<true>();

        if (generating) {
            handles();
            malformed();
            output << "]}\n";
            if (!output.good()) return 3;
            std::cout << "Generated 25 string attribute cases\n";
        } else {
            std::cout << "Verified " << verified << " managed string attribute streams\n";
        }
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
