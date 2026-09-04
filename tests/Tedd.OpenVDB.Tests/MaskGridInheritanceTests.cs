// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Xunit;
namespace Tedd.OpenVDB.Tests;

public sealed class MaskGridInheritanceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int level in new[] { 1, 2, 3 })
            foreach (bool value in new[] { false, true })
                foreach (bool active in new[] { false, true })
                    foreach (bool state in new[] { false, true }) yield return [level, value, active, state];
    }

    [Theory, MemberData(nameof(Cases))]
    public void MaterializedMaskLeafInheritsNativeValueOrActivity(int level, bool value, bool active, bool state)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gridtypes", "mask-inheritance.json");
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", fixture.RootElement.GetProperty("upstream").GetString());
        Assert.Equal("touchLeaf_then_setActiveState", fixture.RootElement.GetProperty("operation").GetString());
        JsonElement expected = Assert.Single(fixture.RootElement.GetProperty("cases").EnumerateArray(), item =>
            item.GetProperty("level").GetInt32() == level && item.GetProperty("value").GetBoolean() == value &&
            item.GetProperty("active").GetBoolean() == active && item.GetProperty("state").GetBoolean() == state);
        var position = new Coord(-1, 9, -9);
        var source = new Grid<bool>(); source.Tree.AddTile(level, position, value, active);
        MaskGrid mask = MaskGrid.FromBooleanGrid(source);
        // MaskGrid deliberately materializes a leaf. The native reference uses touchLeaf
        // before setting its bit, so this checks leaf inheritance rather than no-op allocation.
        if (state) mask.SetValueOn(position); else mask.SetValueOff(position);
        Assert.Equal(expected.GetProperty("activeCount").GetUInt64(), mask.ActiveVoxelCount);
        Assert.Equal(expected.GetProperty("leafCount").GetInt64(), mask.LeafCount);
        Assert.Equal(0, source.Tree.LeafCount);
        foreach (JsonElement sample in expected.GetProperty("samples").EnumerateArray())
        {
            JsonElement coordinate = sample.GetProperty("coord");
            var point = new Coord(coordinate[0].GetInt32(), coordinate[1].GetInt32(), coordinate[2].GetInt32());
            Assert.Equal(sample.GetProperty("value").GetBoolean(), mask.GetValue(point));
            Assert.Equal(sample.GetProperty("active").GetBoolean(), mask.IsValueOn(point));
        }
        // Writing checks the invariant: every materialized bit must be both the value and activity.
        using var bytes = new MemoryStream(); VdbFile.Write(bytes, mask); bytes.Position = 0;
        MaskGrid decoded = VdbFile.ReadMask(bytes);
        Assert.Equal(mask.ActiveVoxelCount, decoded.ActiveVoxelCount);
        Assert.Equal(mask.GetValue(position), decoded.GetValue(position));
    }
}
