// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/AttributeSet.h and points/AttributeSet.cc.
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using Tedd.OpenVDB.IO;

namespace Tedd.OpenVDB.Points;

public readonly record struct AttributeNameAndType(string Name, AttributeType Type, int Stride = 1);

/// <summary>Ordered point-attribute schema with native group-bit and default-metadata semantics.</summary>
/// <remarks>Attribute types and positions are fixed per descriptor instance. Names, groups and metadata are mutable, as in OpenVDB.</remarks>
public sealed class AttributeDescriptor : IEquatable<AttributeDescriptor>
{
    public const int InvalidPosition = -1;
    public const int GroupBits = 8;

    private readonly SortedDictionary<string, int> _nameMap = new(StringComparer.Ordinal);
    private readonly List<AttributeType> _types = [];
    private readonly SortedDictionary<string, int> _groupMap = new(StringComparer.Ordinal);
    private readonly MetaMap _metadata = new();

    public AttributeDescriptor() { }

    public AttributeDescriptor(AttributeDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var pair in source._nameMap) _nameMap.Add(pair.Key, pair.Value);
        _types.AddRange(source._types);
        foreach (var pair in source._groupMap) _groupMap.Add(pair.Key, pair.Value);
        _metadata.Assign(source._metadata);
    }

    public int Size => _types.Count;
    public IReadOnlyDictionary<string, int> Map => new ReadOnlyDictionary<string, int>(_nameMap);
    public IReadOnlyDictionary<string, int> GroupMap => new ReadOnlyDictionary<string, int>(_groupMap);
    public MetaMap Metadata => _metadata;

    public static AttributeDescriptor Create(AttributeType positionType) =>
        Create([new AttributeNameAndType("P", positionType)]);

    public static AttributeDescriptor Create(
        IEnumerable<AttributeNameAndType> attributes,
        IReadOnlyDictionary<string, int>? groups = null,
        IReadOnlyDictionary<string, MetadataValue>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var descriptor = new AttributeDescriptor();
        foreach (var attribute in attributes) descriptor.Insert(attribute.Name, attribute.Type);
        if (groups is not null)
            foreach (var pair in groups.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                descriptor.SetGroup(pair.Key, pair.Value);
        if (metadata is not null) descriptor._metadata.InsertMeta(metadata);
        return descriptor;
    }

    public int Insert(string name, AttributeType type)
    {
        ValidateName(name, "Attribute");
        if (_nameMap.TryGetValue(name, out int existing))
        {
            if (_types[existing] != type)
                throw new ArgumentException("Cannot insert a duplicate attribute name with a different type.", nameof(type));
            return existing;
        }

        int position = _types.Count;
        _types.Add(type);
        _nameMap.Add(name, position);
        return position;
    }

    public int Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _nameMap.GetValueOrDefault(name, InvalidPosition);
    }

    public AttributeType Type(int position)
    {
        if ((uint)position >= (uint)_types.Count) throw new ArgumentOutOfRangeException(nameof(position));
        return _types[position];
    }

    public string ValueType(int position) => Type(position).ValueType;
    public int Count(AttributeType type) => _types.Count(value => value == type);

    public IReadOnlyList<AttributeNameAndType> Entries()
    {
        var entries = new AttributeNameAndType[_types.Count];
        foreach (var pair in _nameMap) entries[pair.Value] = new(pair.Key, _types[pair.Value]);
        return entries;
    }

    public AttributeDescriptor DuplicateAppend(string name, AttributeType type)
    {
        var result = Create(Entries(), _groupMap, _metadata);
        result.Insert(name, type);
        return result;
    }

    public AttributeDescriptor DuplicateDrop(IEnumerable<int> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        int[] dropped = positions.Distinct().Order().ToArray();
        foreach (int position in dropped)
            if ((uint)position >= (uint)_types.Count) throw new ArgumentOutOfRangeException(nameof(positions));

        var droppedSet = dropped.ToHashSet();
        var entries = Entries();
        int[] groupPositions = Enumerable.Range(0, _types.Count).Where(position => _types[position] == AttributeTypes.Group).ToArray();
        var adjustedGroups = new SortedDictionary<string, int>(_groupMap, StringComparer.Ordinal);
        int[] droppedGroupPositions = groupPositions.Where(droppedSet.Contains).ToArray();

        if (droppedGroupPositions.Length != 0)
        {
            foreach (var pair in _groupMap)
            {
                int relativeArray = pair.Value / GroupBits;
                if ((uint)relativeArray >= (uint)groupPositions.Length)
                {
                    adjustedGroups.Remove(pair.Key);
                    continue;
                }

                int attributePosition = groupPositions[relativeArray];
                if (droppedSet.Contains(attributePosition))
                {
                    adjustedGroups.Remove(pair.Key);
                    continue;
                }

                int precedingDroppedGroups = droppedGroupPositions.Count(position => position < attributePosition);
                adjustedGroups[pair.Key] = pair.Value - precedingDroppedGroups * GroupBits;
            }
        }

        var result = Create(entries.Where((_, position) => !droppedSet.Contains(position)), adjustedGroups, _metadata);
        result.PruneUnusedDefaultValues();
        return result;
    }

    public int Rename(string fromName, string toName)
    {
        ValidateName(toName, "Attribute");
        if (_nameMap.ContainsKey(toName) || !_nameMap.Remove(fromName, out int position)) return InvalidPosition;
        _nameMap.Add(toName, position);
        string oldDefault = "default:" + fromName;
        if (_metadata.TryGetValue(oldDefault, out MetadataValue? value))
        {
            _metadata.RemoveMeta(oldDefault);
            _metadata.InsertMeta("default:" + toName, value);
        }
        return position;
    }

    public int RenameGroup(string fromName, string toName)
    {
        ValidateName(toName, "Group");
        if (_groupMap.ContainsKey(toName) || !_groupMap.Remove(fromName, out int offset)) return InvalidPosition;
        _groupMap.Add(toName, offset);
        return offset;
    }

    public bool HasDefaultValue(string name) => _metadata.ContainsKey("default:" + name);

    public T GetDefaultValue<T>(string name)
    {
        if (Find(name) == InvalidPosition) throw new KeyNotFoundException($"Attribute '{name}' was not found.");
        return _metadata.TryGetValue("default:" + name, out var value) ? value.GetValue<T>() : default!;
    }

    public void SetDefaultValue(string name, MetadataValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int position = Find(name);
        if (position == InvalidPosition) throw new KeyNotFoundException($"Attribute '{name}' was not found.");
        if (ValueType(position) != value.TypeName)
            throw new InvalidCastException($"Default metadata type '{value.TypeName}' does not match attribute type '{ValueType(position)}'.");
        _metadata.InsertMeta("default:" + name, value);
    }

    public void RemoveDefaultValue(string name) => _metadata.RemoveMeta("default:" + name);

    public void PruneUnusedDefaultValues()
    {
        string[] unused = _metadata.Keys
            .Where(static name => name.StartsWith("default:", StringComparison.Ordinal))
            .Where(name => !_nameMap.ContainsKey(name[8..]))
            .ToArray();
        foreach (string name in unused) _metadata.RemoveMeta(name);
    }

    public bool HasSameAttributes(AttributeDescriptor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_types.Count != other._types.Count || _nameMap.Count != other._nameMap.Count || _groupMap.Count != other._groupMap.Count)
            return false;
        foreach (var pair in _nameMap)
        {
            int otherPosition = other.Find(pair.Key);
            if (otherPosition == InvalidPosition || _types[pair.Value] != other._types[otherPosition]) return false;
        }
        return OrderedEqual(_groupMap, other._groupMap);
    }

    public bool HasGroup(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return _groupMap.ContainsKey(group);
    }

    public void SetGroup(string group, int offset, bool checkValidOffset = false)
    {
        ValidateName(group, "Group");
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (checkValidOffset)
        {
            if (offset >= AvailableGroups) throw new ArgumentOutOfRangeException(nameof(offset), "Group offset is outside the group arrays.");
            if (_groupMap.Any(pair => pair.Value == offset)) throw new ArgumentException("Group offset is already in use.", nameof(offset));
        }
        _groupMap[group] = offset;
    }

    public bool DropGroup(string group) => _groupMap.Remove(group);
    public void ClearGroups() => _groupMap.Clear();

    public string UniqueName(string name) => Unique(name, _nameMap);
    public string UniqueGroupName(string name) => Unique(name, _groupMap);

    public int GroupOffset(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return _groupMap.GetValueOrDefault(group, InvalidPosition);
    }

    public int GroupOffset(GroupIndex index)
    {
        if ((uint)index.AttributeIndex >= (uint)_types.Count) throw new KeyNotFoundException("Group index is out of range.");
        if (_types[index.AttributeIndex] != AttributeTypes.Group) throw new KeyNotFoundException("Group index does not reference a group attribute.");

        int relative = 0;
        for (int position = 0; position < index.AttributeIndex; position++)
            if (_types[position] == AttributeTypes.Group) relative++;
        return relative * GroupBits + index.BitOffset;
    }

    public GroupIndex GroupIndex(string group)
    {
        int offset = GroupOffset(group);
        if (offset == InvalidPosition) throw new KeyNotFoundException($"Group '{group}' was not found.");
        return GroupIndex(offset);
    }

    public GroupIndex GroupIndex(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        int[] positions = Enumerable.Range(0, _types.Count).Where(position => _types[position] == AttributeTypes.Group).ToArray();
        if (offset >= positions.Length * GroupBits) throw new KeyNotFoundException("Group offset is out of range.");
        return new(positions[offset / GroupBits], checked((byte)(offset % GroupBits)));
    }

    public int AvailableGroups => Count(AttributeTypes.Group) * GroupBits;
    public int UnusedGroups => System.Math.Max(0, AvailableGroups - _groupMap.Count);
    public bool CanCompactGroups => UnusedGroups >= GroupBits;

    public int UnusedGroupOffset(int hint = InvalidPosition)
    {
        if (UnusedGroups == 0) return InvalidPosition;
        HashSet<int> used = _groupMap.Values.ToHashSet();
        if (hint >= 0 && hint < AvailableGroups && !used.Contains(hint)) return hint;
        for (int offset = 0; offset < AvailableGroups; offset++) if (!used.Contains(offset)) return offset;
        return InvalidPosition;
    }

    public bool TryGetRequiredGroupMove(out string sourceName, out int sourceOffset, out int targetOffset)
    {
        targetOffset = UnusedGroupOffset();
        if (targetOffset != InvalidPosition)
            foreach (var pair in _groupMap)
                if (pair.Value >= targetOffset)
                {
                    sourceName = pair.Key; sourceOffset = pair.Value; return true;
                }
        sourceName = string.Empty; sourceOffset = InvalidPosition; return false;
    }

    public bool GroupIndexCollision(AttributeDescriptor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var pair in _groupMap)
            if (other._groupMap.TryGetValue(pair.Key, out int offset) && offset != pair.Value) return true;
        return false;
    }

    public static bool ValidName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (char value in name)
            if (!((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') ||
                  (value >= '0' && value <= '9') || value is '_' or '|' or ':')) return false;
        return true;
    }

    public static (IReadOnlyList<string> Include, IReadOnlyList<string> Exclude, bool IncludeAll) ParseNames(string names)
    {
        List<string> include = [], exclude = [];
        ParseNames(include, exclude, out bool includeAll, names);
        return (include, exclude, includeAll);
    }

    public static void ParseNames(ICollection<string> include, ICollection<string> exclude, out bool includeAll, string names)
    {
        ArgumentNullException.ThrowIfNull(include); ArgumentNullException.ThrowIfNull(exclude); ArgumentNullException.ThrowIfNull(names);
        includeAll = false;
        foreach (string original in names.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            bool negate = original[0] is '^' or '!';
            if (negate)
            {
                if (original.Length < 2) throw new ArgumentException("A negate character must prefix a name.", nameof(names));
                string name = original[1..];
                ValidateName(name, "Name"); exclude.Add(name);
            }
            else if (!includeAll)
            {
                if (original == "*") { includeAll = true; include.Clear(); }
                else { ValidateName(original, "Name"); include.Add(original); }
            }
        }
    }

    public long EstimateMemoryBytes() =>
        128L + _types.Count * 32L + _nameMap.Sum(static pair => 16L + Encoding.UTF8.GetByteCount(pair.Key)) +
        _types.Sum(static type => 16L + Encoding.UTF8.GetByteCount(type.ValueType) + Encoding.UTF8.GetByteCount(type.Codec));

    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite) throw new ArgumentException("Stream is not writable.", nameof(stream));
        Span<byte> integer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(integer, checked((ulong)_types.Count)); stream.Write(integer);
        foreach (AttributeType type in _types) { WriteString(stream, type.ValueType); WriteString(stream, type.Codec); }
        foreach (var pair in _nameMap)
        {
            WriteString(stream, pair.Key); BinaryPrimitives.WriteUInt64LittleEndian(integer, checked((ulong)pair.Value)); stream.Write(integer);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(integer, checked((ulong)_groupMap.Count)); stream.Write(integer);
        foreach (var pair in _groupMap)
        {
            WriteString(stream, pair.Key); BinaryPrimitives.WriteUInt64LittleEndian(integer, checked((ulong)pair.Value)); stream.Write(integer);
        }
        _metadata.WriteMeta(stream);
    }

    public static AttributeDescriptor Read(Stream stream, VdbReadOptions? options = null, int maximumAttributes = 1_000_000, int maximumNameBytes = 1_048_576)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("Stream is not readable.", nameof(stream));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAttributes); ArgumentOutOfRangeException.ThrowIfNegative(maximumNameBytes);
        ulong count64 = ReadUInt64(stream);
        if (count64 > (ulong)maximumAttributes || count64 > int.MaxValue) throw new InvalidDataException("Attribute count exceeds its budget.");
        int count = (int)count64;
        var types = new AttributeType[count];
        for (int i = 0; i < count; i++) types[i] = new(ReadString(stream, maximumNameBytes), ReadString(stream, maximumNameBytes));
        var names = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var positions = new bool[count];
        for (int i = 0; i < count; i++)
        {
            string name = ReadString(stream, maximumNameBytes); ValidateDecodedName(name, "Attribute");
            ulong position64 = ReadUInt64(stream);
            if (position64 >= (ulong)count) throw new InvalidDataException("Attribute position is out of range.");
            int position = (int)position64;
            if (!names.TryAdd(name, position) || positions[position]) throw new InvalidDataException("Attribute descriptor contains duplicate names or positions.");
            positions[position] = true;
        }
        ulong groupCount64 = ReadUInt64(stream);
        if (groupCount64 > (ulong)maximumAttributes * GroupBits || groupCount64 > int.MaxValue)
            throw new InvalidDataException("Group count exceeds its budget.");
        var groups = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < (int)groupCount64; i++)
        {
            string name = ReadString(stream, maximumNameBytes); ValidateDecodedName(name, "Group");
            ulong offset64 = ReadUInt64(stream);
            if (offset64 > int.MaxValue || !groups.TryAdd(name, (int)offset64)) throw new InvalidDataException("Group descriptor is invalid.");
        }
        var metadata = new MetaMap(); metadata.ReadMeta(stream, options: options);
        var result = new AttributeDescriptor();
        result._types.AddRange(types);
        foreach (var pair in names) result._nameMap.Add(pair.Key, pair.Value);
        foreach (var pair in groups) result._groupMap.Add(pair.Key, pair.Value);
        result._metadata.Assign(metadata);
        return result;
    }

    public bool Equals(AttributeDescriptor? other) =>
        other is not null && _types.SequenceEqual(other._types) && OrderedEqual(_nameMap, other._nameMap) &&
        OrderedEqual(_groupMap, other._groupMap) && _metadata.Equals(other._metadata);

    public override bool Equals(object? obj) => obj is AttributeDescriptor other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var type in _types) hash.Add(type);
        foreach (var pair in _nameMap) { hash.Add(pair.Key, StringComparer.Ordinal); hash.Add(pair.Value); }
        foreach (var pair in _groupMap) { hash.Add(pair.Key, StringComparer.Ordinal); hash.Add(pair.Value); }
        hash.Add(_metadata);
        return hash.ToHashCode();
    }

    private static string Unique(string name, SortedDictionary<string, int> map)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!map.ContainsKey(name)) return name;
        for (int suffix = 0; ; suffix++)
        {
            string candidate = name + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!map.ContainsKey(candidate)) return candidate;
        }
    }

    private static bool OrderedEqual(SortedDictionary<string, int> left, SortedDictionary<string, int> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    private static void ValidateName(string? name, string kind)
    {
        if (!ValidName(name)) throw new ArgumentException($"{kind} name contains invalid characters: {name}", nameof(name));
    }

    private static void ValidateDecodedName(string name, string kind)
    {
        if (!ValidName(name)) throw new InvalidDataException($"{kind} name contains invalid characters: {name}");
    }

    private static void WriteString(Stream stream, string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(length, checked((uint)count)); stream.Write(length);
        if (count == 0) return;
        byte[] bytes = Encoding.UTF8.GetBytes(value); stream.Write(bytes);
    }

    private static string ReadString(Stream stream, int maximumBytes)
    {
        Span<byte> length = stackalloc byte[4]; ReadExactly(stream, length);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(length);
        if (count > maximumBytes || count > Array.MaxLength) throw new InvalidDataException("String length exceeds its budget.");
        if (count == 0) return string.Empty;
        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)count); ReadExactly(stream, bytes);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("String is not valid UTF-8.", exception); }
    }

    private static ulong ReadUInt64(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[8]; ReadExactly(stream, bytes); return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            int read = stream.Read(destination);
            if (read == 0) throw new EndOfStreamException();
            destination = destination[read..];
        }
    }
}

public static class AttributeTypes
{
    public static readonly AttributeType Group = new("uint8", "grp");
}
