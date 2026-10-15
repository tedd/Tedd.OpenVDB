using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tedd.OpenVDB.Math;

// Inspect metadata/source only: never instantiate library objects, invoke their
// members, or evaluate attributes through their constructors.
var assembly = typeof(Coord).Assembly;
string? sourceRoot = null;
for (int i = 0; i < args.Length; ++i)
{
    if (args[i] != "--source-root" || i + 1 == args.Length)
        throw new ArgumentException("Usage: ApiInventory [--source-root <repository-or-library-directory>]");
    sourceRoot = Path.GetFullPath(args[++i]);
}
var source = SourceCatalog.Read(sourceRoot, assembly);
const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
var entries = new List<ApiEntry>();
var types = assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
var matchedSourceKeys = new HashSet<string>(StringComparer.Ordinal);
foreach (var type in types)
{
    var propertyAccessors = type.GetProperties(flags).SelectMany(p => p.GetAccessors()).Select(m => m.MetadataToken).ToHashSet();
    var eventAccessors = type.GetEvents(flags).SelectMany(e => new[] { e.AddMethod, e.RemoveMethod, e.RaiseMethod }.OfType<MethodInfo>()).Select(m => m.MetadataToken).ToHashSet();
    foreach (MethodBase method in type.GetConstructors(flags).Cast<MethodBase>().Concat(type.GetMethods(flags)))
    {
        string parameters = string.Join(", ", method.GetParameters().Select(p => Names.TypeName(p.ParameterType)));
        string generic = method.IsGenericMethod ? "<" + string.Join(",", method.GetGenericArguments().Select(Names.TypeName)) + ">" : "";
        string result = method is MethodInfo info ? Names.TypeName(info.ReturnType) : "";
        string signature = Names.TypeName(type) + "::" + method.Name + generic + "(" + parameters + ")" + (result.Length == 0 ? "" : " -> " + result);
        string key = Names.MetadataKey(method);
        var candidates = source.Methods.GetValueOrDefault(key) ?? [];
        IMethodSymbol? symbol = candidates.Count == 1 ? candidates[0] : null;
        if (symbol is not null) matchedSourceKeys.Add(key);
        bool generated = method.IsDefined(typeof(CompilerGeneratedAttribute), false);
        string kind = method.IsConstructor ? "constructor" : propertyAccessors.Contains(method.MetadataToken) ? "property-accessor"
            : eventAccessors.Contains(method.MetadataToken) ? "event-accessor" : method.IsSpecialName ? "operator-or-special" : "method";
        string sourceState = sourceRoot is null ? "source-not-requested" : candidates.Count > 1 ? "ambiguous-source-match" : symbol is null ? "metadata-only-unmapped"
            : symbol.IsImplicitlyDeclared ? "source-synthesized" : "source-declared";
        entries.Add(new ApiEntry(signature, type.FullName!, type.Namespace ?? "", method.Name, kind, generated,
            type.IsInterface, method.IsAbstract, type.IsGenericTypeDefinition || method.IsGenericMethodDefinition,
            method.MetadataToken, "unmapped", [], null, key, symbol?.GetDocumentationCommentId(), sourceState,
            symbol?.IsImplicitlyDeclared, SourceCatalog.Locations(symbol), SourceCatalog.ContainingLocations(symbol),
            method.GetParameters().Select(p => DescribeParameter(p, symbol is null ? null : symbol.Parameters[p.Position])).ToArray(), DescribeReturn(method, symbol),
            DescribeGenericParameters(type.IsGenericType ? type.GetGenericArguments() : [], symbol is null ? [] : SourceCatalog.TypeParameters(symbol.ContainingType).ToArray()),
            DescribeGenericParameters(method.IsGenericMethod ? method.GetGenericArguments() : [], symbol?.TypeParameters.ToArray() ?? []),
            method.IsStatic, method.IsVirtual, method.IsFinal, method.CallingConvention.ToString(),
            symbol?.MethodKind.ToString(), symbol?.IsExtensionMethod));
    }
}
entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Signature, b.Signature));
var sourceOnly = source.Methods.Where(pair => !matchedSourceKeys.Contains(pair.Key))
    .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
    {
        stableId = pair.Key,
        candidates = pair.Value.Select(s => new { documentationId = s.GetDocumentationCommentId(), display = s.ToDisplayString(),
            isImplicitlyDeclared = s.IsImplicitlyDeclared, methodKind = s.MethodKind.ToString(), locations = SourceCatalog.Locations(s) })
    }).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new
{
    assembly = assembly.GetName().Name, assemblyVersion = assembly.GetName().Version?.ToString(),
    moduleVersionId = assembly.ManifestModule.ModuleVersionId,
    runtime = RuntimeInformation.FrameworkDescription, generatedUtc = DateTimeOffset.UtcNow,
    boundary = "All public constructors and methods declared by externally visible managed types, including overloads, operators, property/event accessors, interface/abstract contracts and compiler-synthesized public record members. Inherited System.Object methods, fields and implicit value-type zero initialization are not separate library method definitions. Emitted metadata is authoritative; source mismatches remain explicit.",
    publicTypes = types.Length, callableDefinitions = entries.Count,
    methodsAndOverloads = entries.Count(e => e.Kind is "method" or "operator-or-special"),
    constructors = entries.Count(e => e.Kind == "constructor"),
    propertyAccessors = entries.Count(e => e.Kind == "property-accessor"),
    abstractOrInterfaceDefinitions = entries.Count(e => e.IsAbstract || e.IsInterface),
    compilerGeneratedDefinitions = entries.Count(e => e.CompilerGenerated),
    sourceCrossCheck = new
    {
        sourceRoot, librarySourceRoot = source.LibraryRoot, sourceFiles = source.FileCount,
        compilationModel = "SDK implicit global usings; preview C#; unsafe enabled; nullable enabled; Release; runtime trusted-platform references; NET/NETCOREAPP/NET11_0 and version-or-greater symbols. This is an inspection compilation, not an MSBuild evaluation or emitted assembly.",
        sourceCallableDefinitions = source.Methods.Sum(pair => pair.Value.Count),
        matchedMetadataDefinitions = entries.Count(e => e.SourceState is "source-declared" or "source-synthesized"),
        sourceDeclaredDefinitions = entries.Count(e => e.SourceState == "source-declared"),
        sourceSynthesizedDefinitions = entries.Count(e => e.SourceState == "source-synthesized"),
        unmatchedMetadataDefinitions = entries.Count(e => e.SourceState == "metadata-only-unmapped"),
        ambiguousMetadataDefinitions = entries.Count(e => e.SourceState == "ambiguous-source-match"),
        excludedImplicitValueTypeZeroConstructors = source.ExcludedZeroConstructors,
        sourceOnlyDefinitions = sourceOnly.Length, sourceOnly,
        errorCount = source.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
        warningCount = source.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning),
        diagnostics = source.Diagnostics.Take(100).Select(d => new { id = d.Id, severity = d.Severity.ToString(), message = d.GetMessage(CultureInfo.InvariantCulture), location = d.Location.ToString() }),
        diagnosticsTruncated = source.Diagnostics.Count > 100
    },
    namespaces = entries.GroupBy(e => e.Namespace).Select(g => new { name = g.Key, count = g.Count() }),
    entries
}, new JsonSerializerOptions { WriteIndented = true }));

static ParameterEntry DescribeParameter(ParameterInfo p, IParameterSymbol? symbol) => new(p.Position, p.Name, Names.TypeName(p.ParameterType),
    p.ParameterType.IsByRef ? p.IsOut ? "out" : p.IsIn ? "in" : "ref" : "value", p.Attributes.ToString(),
    p.IsOptional, p.HasDefaultValue, p.HasDefaultValue ? DescribeDefault(p.RawDefaultValue) : null,
    p.GetRequiredCustomModifiers().Select(Names.TypeName).ToArray(), p.GetOptionalCustomModifiers().Select(Names.TypeName).ToArray(),
    p.GetCustomAttributesData().Select(a => a.AttributeType.FullName ?? a.AttributeType.Name).ToArray(),
    symbol?.RefKind.ToString(), symbol?.IsParams, symbol?.ScopedKind.ToString(),
    symbol?.HasExplicitDefaultValue == true ? DescribeDefault(symbol.ExplicitDefaultValue) : null);

static DefaultEntry DescribeDefault(object? value) => value switch
{
    null => new("null", null, null),
    Missing => new("missing", null, typeof(Missing).FullName),
    DBNull => new("dbnull", null, typeof(DBNull).FullName),
    float number => new("constant", number.ToString("R", CultureInfo.InvariantCulture), typeof(float).FullName, BitConverter.SingleToUInt32Bits(number).ToString("X8", CultureInfo.InvariantCulture)),
    double number => new("constant", number.ToString("R", CultureInfo.InvariantCulture), typeof(double).FullName, BitConverter.DoubleToUInt64Bits(number).ToString("X16", CultureInfo.InvariantCulture)),
    decimal number => new("constant", number.ToString(CultureInfo.InvariantCulture), typeof(decimal).FullName, string.Join(" ", decimal.GetBits(number).Select(bits => bits.ToString("X8", CultureInfo.InvariantCulture)))),
    DateTime date => new("constant", date.ToString("O", CultureInfo.InvariantCulture), typeof(DateTime).FullName, date.Ticks.ToString(CultureInfo.InvariantCulture)),
    _ => new("constant", Convert.ToString(value, CultureInfo.InvariantCulture), value.GetType().FullName)
};

static ReturnEntry? DescribeReturn(MethodBase method, IMethodSymbol? symbol)
{
    if (method is not MethodInfo info) return null;
    var p = info.ReturnParameter;
    return new(Names.TypeName(info.ReturnType), info.ReturnType.IsByRef,
        p.GetRequiredCustomModifiers().Select(Names.TypeName).ToArray(), p.GetOptionalCustomModifiers().Select(Names.TypeName).ToArray(),
        p.Attributes.ToString(), p.GetCustomAttributesData().Select(a => a.AttributeType.FullName ?? a.AttributeType.Name).ToArray(), symbol?.ReturnsByRefReadonly);
}

static GenericEntry[] DescribeGenericParameters(Type[] arguments, ITypeParameterSymbol[] symbols) => arguments.Where(t => t.IsGenericParameter)
    .Select((t, i) => new GenericEntry(t.Name, t.GenericParameterPosition, t.DeclaringMethod is null ? "type" : "method",
        t.GenericParameterAttributes.ToString(), t.GetGenericParameterConstraints().Select(Names.TypeName).ToArray(),
        t.GetCustomAttributesData().Select(a => a.AttributeType.FullName ?? a.AttributeType.Name).ToArray(), i >= symbols.Length ? null :
        new SourceGenericConstraints(symbols[i].HasConstructorConstraint, symbols[i].HasReferenceTypeConstraint,
            symbols[i].ReferenceTypeConstraintNullableAnnotation.ToString(), symbols[i].HasValueTypeConstraint,
            symbols[i].HasUnmanagedTypeConstraint, symbols[i].HasNotNullConstraint,
            symbols[i].ConstraintTypes.Select(c => c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToArray()))).ToArray();

record ApiEntry(string Signature, string DeclaringType, string Namespace, string MethodName, string Kind,
    bool CompilerGenerated, bool IsInterface, bool IsAbstract, bool IsGenericDefinition, int MetadataToken,
    string CoverageState, string[] BenchmarkIds, string? NativeEquivalent, string StableId, string? DocumentationId,
    string SourceState, bool? SourceImplicitlyDeclared, SourceLocation[] SourceLocations, SourceLocation[] ContainingTypeLocations,
    ParameterEntry[] Parameters, ReturnEntry? Return, GenericEntry[] DeclaringTypeGenericParameters, GenericEntry[] MethodGenericParameters,
    bool IsStatic, bool IsVirtual, bool IsFinal, string CallingConvention, string? SourceMethodKind, bool? IsExtensionMethod);
record ParameterEntry(int Position, string? Name, string Type, string Modifier, string Attributes, bool Optional, bool HasDefault,
    DefaultEntry? Default, string[] RequiredCustomModifiers, string[] OptionalCustomModifiers, string[] CustomAttributeTypes,
    string? SourceRefKind, bool? SourceIsParams, string? SourceScopedKind, DefaultEntry? SourceExplicitDefault);
record DefaultEntry(string Kind, string? Value, string? Type, string? RepresentationBits = null);
record ReturnEntry(string Type, bool IsByRef, string[] RequiredCustomModifiers, string[] OptionalCustomModifiers, string Attributes, string[] CustomAttributeTypes, bool? SourceReturnsByRefReadonly);
record GenericEntry(string Name, int Position, string Owner, string Attributes, string[] TypeConstraints, string[] CustomAttributeTypes, SourceGenericConstraints? SourceConstraints);
record SourceGenericConstraints(bool Constructor, bool ReferenceType, string ReferenceTypeNullableAnnotation, bool ValueType, bool Unmanaged, bool NotNull, string[] Types);
record SourceLocation(string Path, int Line, int Column, int EndLine, int EndColumn);

static class Names
{
    public static string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()!) + "&";
        if (type.IsPointer) return TypeName(type.GetElementType()!) + "*";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return (type.DeclaringMethod is null ? "!" : "!!") + type.GenericParameterPosition + ":" + type.Name;
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        return (type.GetGenericTypeDefinition().FullName ?? type.Name) + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
    }
    public static string MetadataKey(MethodBase method) => TypeIdentity(method.DeclaringType!) + "::" + method.Name + "``" +
        (method.IsGenericMethod ? method.GetGenericArguments().Length : 0) + "(" + string.Join(",", method.GetParameters().Select(p => TypeIdentity(p.ParameterType))) + ")" +
        (method is MethodInfo info ? "->" + TypeIdentity(info.ReturnType) : "");
    public static string SourceKey(IMethodSymbol method) => TypeIdentity(method.ContainingType) + "::" + method.MetadataName + "``" + method.Arity +
        "(" + string.Join(",", method.Parameters.Select(p => TypeIdentity(p.Type) + (p.RefKind == RefKind.None ? "" : "&"))) + ")" +
        (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ? "" : "->" + TypeIdentity(method.ReturnType) + (method.ReturnsByRef || method.ReturnsByRefReadonly ? "&" : ""));
    private static string TypeIdentity(Type type)
    {
        if (type.IsGenericParameter) return (type.DeclaringMethod is null ? "!" : "!!") + type.GenericParameterPosition;
        if (type.IsByRef) return TypeIdentity(type.GetElementType()!) + "&";
        if (type.IsPointer) return TypeIdentity(type.GetElementType()!) + "*";
        if (type.IsArray) return TypeIdentity(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        return type.IsGenericType ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeIdentity)) + ">" : type.FullName ?? type.Name;
    }
    private static string TypeIdentity(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            int ordinal = parameter.Ordinal;
            if (parameter.TypeParameterKind == TypeParameterKind.Type)
                for (var owner = parameter.ContainingType?.ContainingType; owner is not null; owner = owner.ContainingType) ordinal += owner.Arity;
            return (parameter.TypeParameterKind == TypeParameterKind.Method ? "!!" : "!") + ordinal;
        }
        if (type is IArrayTypeSymbol array) return TypeIdentity(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
        if (type is IPointerTypeSymbol pointer) return TypeIdentity(pointer.PointedAtType) + "*";
        if (type.TypeKind == TypeKind.Dynamic) return "System.Object";
        if (type is not INamedTypeSymbol named) return type.ToDisplayString();
        named = named.TupleUnderlyingType ?? named;
        string definition = DefinitionName(named);
        var arguments = FlattenArguments(named).ToArray();
        return arguments.Length == 0 ? definition : definition + "<" + string.Join(",", arguments.Select(TypeIdentity)) + ">";
    }
    private static string DefinitionName(INamedTypeSymbol type) => type.ContainingType is { } parent ? DefinitionName(parent) + "+" + type.MetadataName
        : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;
    private static IEnumerable<ITypeSymbol> FlattenArguments(INamedTypeSymbol type) =>
        (type.ContainingType is { } parent ? FlattenArguments(parent) : []).Concat(type.TypeArguments);
}

sealed class SourceCatalog
{
    public string? LibraryRoot { get; private set; }
    public int FileCount { get; private set; }
    public int ExcludedZeroConstructors { get; private set; }
    public Dictionary<string, List<IMethodSymbol>> Methods { get; } = new(StringComparer.Ordinal);
    public List<Diagnostic> Diagnostics { get; } = [];

    public static SourceCatalog Read(string? root, Assembly assembly)
    {
        var catalog = new SourceCatalog();
        if (root is null) return catalog;
        string libraryRoot = Directory.Exists(Path.Combine(root, "src", "Tedd.OpenVDB")) ? Path.Combine(root, "src", "Tedd.OpenVDB") : root;
        if (!File.Exists(Path.Combine(libraryRoot, "Tedd.OpenVDB.csproj")))
            throw new ArgumentException("--source-root must identify the repository or its src/Tedd.OpenVDB directory.");
        catalog.LibraryRoot = libraryRoot;
        string[] files = Directory.EnumerateFiles(libraryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(libraryRoot, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" || part.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        catalog.FileCount = files.Length;
        string[] symbols = ["NET", "NETCOREAPP", "NET11_0", "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP1_1_OR_GREATER", "NETCOREAPP2_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP2_2_OR_GREATER", "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER", .. Enumerable.Range(5, 7).Select(n => $"NET{n}_0_OR_GREATER")];
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, preprocessorSymbols: symbols);
        var trees = files.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText("global using global::System; global using global::System.Collections.Generic; global using global::System.IO; global using global::System.Linq; global using global::System.Net.Http; global using global::System.Threading; global using global::System.Threading.Tasks;", parseOptions, "<inspection-sdk-implicit-usings>"));
        string[] referencePaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = referencePaths.Where(path => !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), Path.GetFullPath(assembly.Location)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(assembly.GetName().Name, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        catalog.Diagnostics.AddRange(compilation.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        VisitNamespace(compilation.Assembly.GlobalNamespace);
        return catalog;

        void VisitNamespace(INamespaceSymbol ns)
        {
            foreach (var child in ns.GetNamespaceMembers()) VisitNamespace(child);
            foreach (var type in ns.GetTypeMembers()) VisitType(type);
        }
        void VisitType(INamedTypeSymbol type)
        {
            if (type.DeclaredAccessibility != Accessibility.Public) return;
            foreach (var child in type.GetTypeMembers()) VisitType(child);
            var members = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var member in type.GetMembers())
            {
                if (member is IMethodSymbol method) members.Add(method);
                if (member is IPropertySymbol property)
                {
                    if (property.GetMethod is { } getter) members.Add(getter);
                    if (property.SetMethod is { } setter) members.Add(setter);
                }
                if (member is IEventSymbol evt)
                {
                    if (evt.AddMethod is { } adder) members.Add(adder);
                    if (evt.RemoveMethod is { } remover) members.Add(remover);
                    if (evt.RaiseMethod is { } raiser) members.Add(raiser);
                }
            }
            foreach (var method in members.Where(m => m.DeclaredAccessibility == Accessibility.Public))
            {
                if (type.IsValueType && method.MethodKind == MethodKind.Constructor && method.IsImplicitlyDeclared && method.Parameters.Length == 0)
                { ++catalog.ExcludedZeroConstructors; continue; }
                string key = Names.SourceKey(method);
                if (!catalog.Methods.TryGetValue(key, out var methods)) catalog.Methods.Add(key, methods = []);
                methods.Add(method);
            }
        }
    }

    public static SourceLocation[] Locations(IMethodSymbol? method) => method is null ? [] : LocationsOf(method);
    public static IEnumerable<ITypeParameterSymbol> TypeParameters(INamedTypeSymbol type) =>
        (type.ContainingType is { } parent ? TypeParameters(parent) : []).Concat(type.TypeParameters);
    public static SourceLocation[] ContainingLocations(IMethodSymbol? method) => method is null ? [] : LocationsOf(method.ContainingType);
    private static SourceLocation[] LocationsOf(ISymbol symbol) => symbol.Locations.Where(l => l.IsInSource).Select(location =>
    {
        var span = location.GetLineSpan();
        return new SourceLocation(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
    }).ToArray();
}
