#include <openvdb/tree/Tree.h>
#include <openvdb/tree/ValueAccessor.h>
#include <windows.h>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <iomanip>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

using Tree = openvdb::tree::Tree4<std::uint16_t, 5, 4, 3>::Type;
using Accessor = openvdb::tree::ValueAccessor<Tree>;
using Coordinates = std::array<openvdb::Coord, 32768>;
using Clock = std::chrono::steady_clock;

__declspec(noinline) std::uint64_t read(Accessor& accessor, const Coordinates& coordinates, int count)
{
    std::uint64_t sum = 0;
    for (int i = 0; i < count; ++i) sum += accessor.getValue(coordinates[i & 32767]);
    return sum;
}
__declspec(noinline) void write(Accessor& accessor, const Coordinates& coordinates, int count, int phase)
{
    for (int i = 0; i < count; ++i)
        accessor.setValueOn(coordinates[i & 32767], std::uint16_t(1 + ((i + phase) & 7)));
}
void validate(Tree& tree, Accessor& accessor, const Coordinates& coordinates, int phase)
{
    std::uint64_t checksum = 0;
    for (int i = 0; i < 32768; ++i) {
        std::uint16_t actual;
        if (!accessor.probeValue(coordinates[i], actual) || actual != std::uint16_t(1 + ((i + phase) & 7)))
            throw std::runtime_error("Voxel value/activity mismatch.");
        checksum += actual;
    }
    if (checksum != 147456 || tree.activeVoxelCount() != 32768 || tree.leafCount() != 64)
        throw std::runtime_error("Tree topology or aggregate checksum mismatch.");
}
int main(int argc, char** argv)
try {
    auto option = [&](const std::string& key, int fallback) {
        for (int i = 1; i + 1 < argc; ++i) if (argv[i] == key) return std::stoi(argv[i + 1]);
        return fallback;
    };
    auto has = [&](const std::string& key) { for (int i = 1; i < argc; ++i) if (argv[i] == key) return true; return false; };
    const int count = option("--operations", 50'003'968), samples = option("--samples", 9);
    const int warmup = option("--warmup-ms", 2000), cpu = option("--cpu", 8);
    if (count < 32768 || count % 32768 || samples < 1 || warmup < 0 || cpu < 0 || cpu > 62)
        throw std::runtime_error("Operations must be a positive multiple of 32768; invalid arguments.");
    if (!SetProcessAffinityMask(GetCurrentProcess(), DWORD_PTR(1ULL << cpu))) throw std::runtime_error("Affinity setting failed.");
    Tree tree(std::uint16_t(0));
    Accessor accessor(tree);
    Coordinates coordinates;
    std::uint64_t hash = 14695981039346656037ULL;
    for (int i = 0; i < 32768; ++i) {
        coordinates[i] = openvdb::Coord((i >> 10) - 16, ((i >> 5) & 31) - 16, (i & 31) - 16);
        accessor.setValueOn(coordinates[i], std::uint16_t(1 + (i & 7)));
        for (int component : {coordinates[i].x(), coordinates[i].y(), coordinates[i].z()})
            hash = (hash ^ std::uint32_t(component)) * 1099511628211ULL;
    }
    validate(tree, accessor, coordinates, 0);
    std::uint64_t sink = 0;
    std::cout << std::setprecision(17) << "{\"implementation\":\"native\",\"valueType\":\"UInt16\",\"coordinates\":32768,\"coordinateHash\":\""
              << std::hex << std::setw(16) << std::setfill('0') << hash << std::dec
              << "\",\"checksum\":147456,\"activeVoxelCount\":32768,\"leafCount\":64,\"openvdbVersion\":\""
              << OPENVDB_LIBRARY_MAJOR_VERSION_NUMBER << '.' << OPENVDB_LIBRARY_MINOR_VERSION_NUMBER << '.' << OPENVDB_LIBRARY_PATCH_VERSION_NUMBER
              << "\",\"msvc\":" << _MSC_FULL_VER << ",\"cpu\":" << cpu << ",\"warmupMilliseconds\":" << warmup << ",\"results\":[";
    bool first = true;
    if (!has("--validate")) for (const std::string& workload : has("--write-first") ? std::vector<std::string>{"write", "read"} : std::vector<std::string>{"read", "write"}) {
        const auto warmupStart = Clock::now();
        do {
            if (workload == "read") sink = read(accessor, coordinates, 1'048'576);
            else write(accessor, coordinates, 1'048'576, 3);
        } while (std::chrono::duration<double, std::milli>(Clock::now() - warmupStart).count() < warmup);
        write(accessor, coordinates, 32768, 0);
        validate(tree, accessor, coordinates, 0);
        if (!first) std::cout << ',';
        first = false;
        std::cout << "{\"name\":\"accessor." << workload << ".coherent\",\"operations\":" << count << ",\"elapsedMilliseconds\":[";
        for (int sample = 0; sample < samples; ++sample) {
            std::atomic_signal_fence(std::memory_order_seq_cst);
            const auto start = Clock::now();
            if (workload == "read") sink = read(accessor, coordinates, count);
            else write(accessor, coordinates, count, sample + 1);
            std::atomic_signal_fence(std::memory_order_seq_cst);
            const auto end = Clock::now();
            if (workload == "read" && sink != std::uint64_t(count / 8) * 36) throw std::runtime_error("Read checksum mismatch.");
            validate(tree, accessor, coordinates, workload == "read" ? 0 : sample + 1);
            if (sample) std::cout << ',';
            std::cout << std::chrono::duration<double, std::milli>(end - start).count();
        }
        std::cout << "]}";
        write(accessor, coordinates, 32768, 0);
    }
    validate(tree, accessor, coordinates, 0);
    std::cout << "],\"sink\":" << sink << "}\n";
    return 0;
} catch (const std::exception& error) {
    std::cerr << error.what() << '\n';
    return 1;
}
