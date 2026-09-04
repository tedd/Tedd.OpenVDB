// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
#include <openvdb/openvdb.h>
#include <openvdb/io/io.h>
#include <openvdb/points/AttributeSet.h>
#include <openvdb/points/AttributeGroup.h>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <stdexcept>
using namespace openvdb;
using namespace openvdb::points;

static std::ofstream output;
static bool first = true;
static void hex(const std::string& bytes)
{
    static constexpr char digits[] = "0123456789abcdef";
    output << '"';
    for (unsigned char value : bytes) output << digits[value >> 4] << digits[value & 15];
    output << '"';
}
static AttributeSet::Descriptor::Ptr descriptor(int profile)
{
    using P = TypedAttributeArray<Vec3s>;
    using F = TypedAttributeArray<float>;
    using I = TypedAttributeArray<int32_t>;
    auto result = AttributeSet::Descriptor::create(P::attributeType());
    if (profile & 1) result = result->duplicateAppend("density", F::attributeType());
    if (profile & 2) result = result->duplicateAppend("id", I::attributeType());
    const int groups = (profile >> 2) % 3;
    for (int i = 0; i < groups; ++i) result = result->duplicateAppend("__group" + std::to_string(i), GroupAttributeArray::attributeType());
    result->getMetadata().insertMeta("purpose", StringMetadata("profile-" + std::to_string(profile)));
    if (profile & 1) {
        FloatMetadata value(float(profile - 20) * 0.125f);
        result->setDefaultValue("density", value);
        if (profile & 16) result->rename("density", "mass");
    }
    if (profile & 2) {
        Int32Metadata value(profile * 3 - 17);
        result->setDefaultValue("id", value);
    }
    for (int i = 0; i < groups; ++i) result->setGroup("g" + std::to_string(i), size_t(i * 8 + profile % 8));
    if ((profile & 64) && result->find("id") != AttributeSet::INVALID_POS)
        result = result->duplicateDrop({result->find("id")});
    return result;
}
static void descriptorCases()
{
    for (int profile = 0; profile < 96; ++profile) {
        auto value = descriptor(profile);
        std::ostringstream stream(std::ios::binary);
        value->write(stream);
        if (!first) output << ',';
        first = false;
        output << "{\"kind\":\"descriptor\",\"profile\":" << profile << ",\"wire\":";
        hex(stream.str());
        output << '}';
    }
}
static void setCases()
{
    using P = TypedAttributeArray<Vec3s>;
    using I = TypedAttributeArray<int32_t>;
    for (int profile = 0; profile < 48; ++profile) {
        auto schema = AttributeSet::Descriptor::create(P::attributeType());
        TypedMetadata<Vec3s> pdefault(Vec3s(float(profile) * .01f, -.25f, .5f));
        schema->setDefaultValue("P", pdefault);
        const Index size = Index(1 + profile % 11);
        AttributeSet set(schema, size);
        auto& points = static_cast<P&>(*set.get("P"));
        if (profile & 1) {
            points.expand();
            for (Index i = 0; i < points.dataSize(); ++i)
                points.set(i, Vec3s(float(i) * .125f, float(profile - int(i)), float(i * i) * .03125f));
        }
        Int32Metadata idefault(profile - 7);
        auto idbase = set.appendAttribute("id", I::attributeType(), 1, true, &idefault);
        auto& ids = static_cast<I&>(*idbase);
        if (profile & 2) {
            ids.expand();
            for (Index i = 0; i < ids.dataSize(); ++i) ids.set(i, int32_t(profile * 100 + int(i) * 17));
        }
        if (profile & 4) {
            auto temporary = set.appendAttribute("temporary", TypedAttributeArray<float>::attributeType());
            temporary->setTransient(true);
            temporary->setHidden((profile & 8) != 0);
        }
        if (profile & 16) {
            auto group = set.appendAttribute("__group", GroupAttributeArray::attributeType());
            set.descriptor().setGroup("selected", size_t(profile % 8));
            auto& values = static_cast<GroupAttributeArray&>(*group);
            values.expand();
            for (Index i = 0; i < values.dataSize(); ++i) values.set(i, GroupType((i * 29 + profile) & 255));
        }
        points.setHidden((profile & 8) != 0);
        if (profile & 32) ids.setStreaming(true);
        if (!first) output << ',';
        first = false;
        output << "{\"kind\":\"set\",\"profile\":" << profile;
        for (int transient = 0; transient < 2; ++transient) {
            std::ostringstream stream(std::ios::binary);
            io::setDataCompression(stream, io::COMPRESS_NONE);
            set.write(stream, transient != 0);
            output << (transient ? ",\"all\":" : ",\"persistent\":");
            hex(stream.str());
            std::istringstream input(stream.str(), std::ios::binary);
            AttributeSet roundtrip;
            roundtrip.read(input);
            const bool equal = roundtrip == set;
            const bool expectedEqual = transient != 0 || (profile & 4) == 0;
            if (!input || equal != expectedEqual) throw std::runtime_error("Native AttributeSet self-check failed");
        }
        output << '}';
    }
}
int main(int argc, char** argv)
{
    try {
        if (argc != 2) return 2;
        initialize();
        output.open(argv[1], std::ios::binary);
        output << "{\"commit\":\"f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf\",\"cases\":[";
        descriptorCases();
        setCases();
        output << "]}\n";
        return output.good() ? 0 : 3;
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
