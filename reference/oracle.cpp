// Copyright (c) Tedd.OpenVDB contributors.
// SPDX-License-Identifier: Apache-2.0
// Executes actual upstream OpenVDB; expected values are never computed here.
#include <openvdb/openvdb.h>
#include <cstdint>
#include <iostream>
#include <limits>
#include <memory>
#include <string>
#include <vector>
using Coord = openvdb::Coord;
using Tree = openvdb::Int32Tree;
struct Operation {
    std::string kind;
    Coord at;
    int value = 0;
    bool active = false;
    int level = 0;
    Coord max;
};
Operation point(std::string kind, Coord at, int value = 0, bool active = false) {
    return {kind, at, value, active, 0, at};
}
Operation tile(int level, Coord at, int value, bool active) {
    return {"tile", at, value, active, level, at};
}
Operation fill(Coord min, Coord max, int value, bool active) {
    return {"fill", min, value, active, 0, max};
}
void coordinate(Coord c) { std::cout << '[' << c.x() << ',' << c.y() << ',' << c.z() << ']'; }
void apply(std::unique_ptr<Tree>& tree, std::unique_ptr<Tree>& other, const Operation& op) {
    if (op.kind.rfind("other-", 0) == 0) { auto nested = op; nested.kind = op.kind.substr(6); apply(other, tree, nested); return; }
    if (op.kind == "on") tree->setValueOn(op.at, op.value);
    else if (op.kind == "off") tree->setValueOff(op.at, op.value);
    else if (op.kind == "only") tree->setValueOnly(op.at, op.value);
    else if (op.kind == "onExisting") tree->setValueOn(op.at);
    else if (op.kind == "offExisting") tree->setValueOff(op.at);
    else if (op.kind == "active") tree->setActiveState(op.at, op.active);
    else if (op.kind == "tile") tree->addTile(op.level, op.at, op.value, op.active);
    else if (op.kind == "fill") tree->fill(openvdb::CoordBBox(op.at, op.max), op.value, op.active);
    else if (op.kind == "union") tree->topologyUnion(*other, op.active);
    else if (op.kind == "intersection") tree->topologyIntersection(*other);
    else if (op.kind == "difference") tree->topologyDifference(*other);
    else if (op.kind == "prune") tree->prune();
    else if (op.kind == "copy") tree = std::make_unique<Tree>(*tree);
    else if (op.kind == "clear") tree->clear();
    else if (op.kind != "noop") throw std::runtime_error("unknown operation");
}
void state(const Tree& tree, const std::vector<Coord>& probes) {
    std::cout << ",\"activeCount\":" << tree.activeVoxelCount() << ",\"leafCount\":" << tree.leafCount();
    openvdb::CoordBBox bounds;
    std::cout << ",\"bounds\":";
    if (tree.evalActiveVoxelBoundingBox(bounds)) {
        std::cout << '['; coordinate(bounds.min()); std::cout << ','; coordinate(bounds.max()); std::cout << ']';
    } else std::cout << "null";
    std::cout << ",\"samples\":[";
    bool first = true;
    for (const auto c : probes) {
        if (!first) std::cout << ',';
        first = false;
        std::cout << "{\"at\":"; coordinate(c);
        std::cout << ",\"value\":" << tree.getValue(c) << ",\"active\":" << (tree.isValueOn(c) ? "true" : "false")
                  << ",\"depth\":" << tree.getValueDepth(c) << '}';
    }
    std::cout << ']';
}
void scenario(const std::string& name, int background, const std::vector<Operation>& operations,
              std::vector<Coord> probes = {}) {
    static bool first = true;
    if (!first) std::cout << ",\n";
    first = false;
    for (const auto& op : operations) {
        if (op.kind != "prune" && op.kind != "copy" && op.kind != "clear" && op.kind != "noop") {
            probes.push_back(op.at);
            if (op.kind == "fill") probes.push_back(op.max);
        }
    }
    const int edges[] = {-4097, -4096, -4095, -129, -128, -127, -9, -8, -7, -1, 0, 1, 7, 8, 9, 127, 128, 129, 4095, 4096, 4097};
    for (int edge : edges) { probes.emplace_back(edge, 0, 0); probes.emplace_back(0, edge, 0); probes.emplace_back(0, 0, edge); }
    probes.emplace_back(std::numeric_limits<int>::min(), std::numeric_limits<int>::min(), std::numeric_limits<int>::min());
    probes.emplace_back(std::numeric_limits<int>::max(), std::numeric_limits<int>::max(), std::numeric_limits<int>::max());
    std::sort(probes.begin(), probes.end());
    probes.erase(std::unique(probes.begin(), probes.end()), probes.end());
    auto tree = std::make_unique<Tree>(background);
    auto other = std::make_unique<Tree>(background + 100);
    std::cout << "{\"name\":\"" << name << "\",\"background\":" << background << ",\"steps\":[";
    bool firstStep = true;
    for (const auto& op : operations) {
        if (!firstStep) std::cout << ',';
        firstStep = false;
        apply(tree, other, op);
        std::cout << "{\"op\":\"" << op.kind << "\",\"at\":"; coordinate(op.at);
        std::cout << ",\"max\":"; coordinate(op.max);
        std::cout << ",\"value\":" << op.value << ",\"active\":" << (op.active ? "true" : "false") << ",\"level\":" << op.level;
        std::cout << ",\"sameTopology\":" << (tree->hasSameTopology(*other) ? "true" : "false");
        state(*tree, probes);
        std::cout << '}';
    }
    std::cout << "]}";
}
int main() {
    const Coord zero(0, 0, 0), p(-1, -1, -1);
    std::cout << "{\"upstreamCommit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"upstreamVersion\":\""
              << OPENVDB_LIBRARY_VERSION_STRING << "\",\"tree\":\"Tree4<int32_t,5,4,3>\",\"cases\":[\n";
    scenario("empty-and-inactive-background-noops", 17, {
        point("noop", zero), point("active", p, 0, false), point("offExisting", p),
        point("only", p, 17), point("prune", zero), point("off", p, 17), point("prune", zero), point("clear", zero)});
    scenario("independent-activity-and-value", 19, {
        point("only", p, 42), point("active", p, 0, true), point("only", p, 43),
        point("offExisting", p), point("onExisting", p), point("off", p, 44),
        point("active", p, 0, false), point("on", p, 19), point("off", p, 19), point("prune", zero)});
    std::vector<Operation> boundaries;
    const int limits[] = {std::numeric_limits<int>::min(), -4097, -4096, -4095, -129, -128, -127, -9, -8, -7, -1, 0, 1, 7, 8, 9, 127, 128, 129, 4095, 4096, 4097, std::numeric_limits<int>::max()};
    int ordinal = 1;
    for (int x : limits) boundaries.push_back(point("on", Coord(x, x, x), ordinal++));
    boundaries.push_back(point("copy", zero));
    boundaries.push_back(point("clear", zero));
    scenario("all-coordinate-boundaries", -3, boundaries);
    for (int level = 0; level <= 3; ++level) {
        for (bool active : {false, true}) {
            const std::string name = "negative-tile-level-" + std::to_string(level) + (active ? "-active" : "-inactive");
            scenario(name, -7, {tile(level, p, 81, active), point("only", p, 81), point("active", p, 0, active),
                point(active ? "on" : "off", p, 81), point("copy", zero), point("only", p, 82),
                point("active", p, 0, !active), point("only", p, 81), point("active", p, 0, active),
                point("prune", zero), point("clear", zero)},
                {Coord(-4096,-4096,-4096), Coord(-128,-128,-128), Coord(-8,-8,-8), Coord(-2,-2,-2)});
        }
    }
    scenario("hierarchical-tile-replacement", 0, {
        tile(3, zero, 1, true), tile(2, zero, 2, false), tile(1, zero, 3, true), tile(0, zero, 4, false),
        tile(1, zero, 5, false), tile(2, zero, 6, true), tile(3, zero, 7, false), point("prune", zero),
        tile(3, zero, 0, false), point("prune", zero)});
    for (int edge : {8, 128, 4096}) {
        scenario("aligned-active-fill-" + std::to_string(edge), 0, {
            fill(zero, Coord(edge-1,edge-1,edge-1), 9, true), point("offExisting", zero),
            point("onExisting", zero), point("prune", zero), point("copy", zero), point("clear", zero)});
        scenario("aligned-negative-inactive-fill-" + std::to_string(edge), 0, {
            fill(Coord(-edge,-edge,-edge), p, 9, false), point("onExisting", p),
            point("offExisting", p), point("prune", zero)});
    }
    scenario("inclusive-overlapping-fill", -2, {
        fill(Coord(-9,-3,-2), Coord(8,4,3), 2, true), fill(Coord(-1,-1,-1), Coord(1,1,1), 7, false),
        fill(Coord(7,1,0), Coord(9,5,4), 8, true), fill(Coord(-9,-3,-2), Coord(9,5,4), -2, false), point("prune", zero)},
        {Coord(-10,-3,-2),Coord(9,4,3),Coord(1,1,1),Coord(2,1,1),Coord(8,4,3),Coord(9,5,4),Coord(10,5,4)});
    scenario("uniform-values-nonuniform-activity-prune", 0, {
        fill(zero, Coord(7,7,7), 11, true), point("offExisting", zero), point("prune", zero),
        point("onExisting", zero), point("prune", zero), fill(zero, Coord(7,7,7), 0, false), point("prune", zero)});
    scenario("single-voxel-fill-and-reversed-empty-box", 5, {
        fill(p, p, 7, false), fill(Coord(1,1,1), zero, 99, true), point("onExisting", p), point("prune", zero)});
    for (bool maximum : {false}) {
        const int edge = maximum ? std::numeric_limits<int>::max() - 7 : std::numeric_limits<int>::min();
        scenario(maximum ? "maximum-coordinate-fill" : "minimum-coordinate-fill", -1, {
            fill(Coord(edge,edge,edge), Coord(edge+7,edge+7,edge+7), 31, true),
            point("offExisting", Coord(edge+7,edge+7,edge+7)), point("onExisting", Coord(edge+7,edge+7,edge+7)), point("prune", zero)});
    }
    uint32_t rng = 0xC0FFEE;
    auto random = [&]() { rng = rng * 1664525u + 1013904223u; return rng; };
    for (int run = 0; run < 4; ++run) {
        std::vector<Operation> operations;
        for (int i = 0; i < 64; ++i) {
            const int x = static_cast<int>(random()%273) - 136;
            const int y = static_cast<int>(random()%33) - 16;
            const int z = static_cast<int>(random()%33) - 16;
            const int value = static_cast<int>(random()%19) - 9;
            const int choice = static_cast<int>(random()%8);
            const Coord at(x,y,z);
            if (choice == 0) operations.push_back(point("on", at, value));
            else if (choice == 1) operations.push_back(point("off", at, value));
            else if (choice == 2) operations.push_back(point("only", at, value));
            else if (choice == 3) operations.push_back(point("active", at, 0, (random()&1u) != 0));
            else if (choice == 4) operations.push_back(tile(1, at, value, (random()&1u) != 0));
            else if (choice == 5) operations.push_back(fill(at, at + Coord(2,2,2), value, (random()&1u) != 0));
            else if (choice == 6) operations.push_back(point("prune", zero));
            else operations.push_back(point("copy", zero));
        }
        operations.push_back(point("clear", zero));
        scenario("deterministic-mixed-operations-" + std::to_string(run), -3, operations);
    }
    const std::vector<Operation> shapes = {
        point("noop", zero), tile(3, zero, 13, false), tile(3, zero, 17, true),
        point("off", Coord(1,2,3), 23), point("on", Coord(1,2,3), 29),
        tile(1, zero, 31, false), tile(1, zero, 37, true)
    };
    for (size_t lhs = 0; lhs < shapes.size(); ++lhs) {
        for (size_t rhs = 0; rhs < shapes.size(); ++rhs) {
            auto otherShape = shapes[rhs];
            otherShape.kind = "other-" + otherShape.kind;
            otherShape.value += 1000;
            for (const std::string operation : {"union", "union-preserve", "intersection", "difference"}) {
                const auto join = point(operation == "union-preserve" ? "union" : operation, zero, 0, operation == "union-preserve");
                scenario("topology-" + std::to_string(lhs) + "-" + std::to_string(rhs) + "-" + operation, -11,
                    {shapes[lhs], otherShape, join, point("prune", zero)},
                    {Coord(1,2,3), Coord(1,2,4), Coord(6,6,6), Coord(10,10,10), Coord(200,200,200)});
            }
        }
    }
    std::cout << "\n]}\n";
}
