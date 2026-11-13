using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
var metadataPath = args.FirstOrDefault(value=>value.StartsWith("--metadata="))?[11..] ?? "C:/Windows/System32/WinMetadata/Windows.UI.winmd";
args=args.Where(value=>!value.StartsWith("--metadata=")).ToArray();
using var file = File.OpenRead(metadataPath);
using var pe = new PEReader(file);
var metadata = pe.GetMetadataReader();
foreach (var handle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(handle);
    var name = metadata.GetString(type.Name);
    var requested=args.Length>0?args:new[] { "ICompositor", "ICompositor3", "ISpriteVisual", "IVisual", "ICompositionTarget", "ICompositionBrush" };
    if (!requested.Contains(name)) continue;
    Console.WriteLine(metadata.GetString(type.Namespace) + "." + name);
    foreach(var implementationHandle in type.GetInterfaceImplementations())
    {
        var implementation=metadata.GetInterfaceImplementation(implementationHandle);
        var contract=implementation.Interface.Kind==HandleKind.TypeSpecification
            ? metadata.GetTypeSpecification((TypeSpecificationHandle)implementation.Interface).DecodeSignature(new TypeNames(),(object?)null)
            : implementation.Interface.Kind==HandleKind.TypeReference
                ? metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)implementation.Interface).Name)
                : metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)implementation.Interface).Name);
        Console.WriteLine("implements " + contract);
    }
    foreach (var attributeHandle in type.GetCustomAttributes())
    {
        var attribute = metadata.GetCustomAttribute(attributeHandle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
        var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
        var attributeType = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        if (metadata.GetString(attributeType.Name) != "GuidAttribute") continue;
        var reader = metadata.GetBlobReader(attribute.Value); reader.ReadUInt16();
        Console.WriteLine(new Guid(reader.ReadBytes(16)));
    }
    int slot = 6;
    foreach (var methodHandle in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(methodHandle);
        var signature = method.DecodeSignature(new TypeNames(), (object?)null);
        Console.WriteLine($"{slot++}: {metadata.GetString(method.Name)}({string.Join(", ", signature.ParameterTypes)}) -> {signature.ReturnType}");
    }
    foreach (var fieldHandle in type.GetFields())
    {
        var field = metadata.GetFieldDefinition(fieldHandle);
        var constantHandle = field.GetDefaultValue();
        Console.WriteLine($"field {metadata.GetString(field.Name)}: {field.DecodeSignature(new TypeNames(), (object?)null)}" +
            (constantHandle.IsNil ? "" : " = " + Convert.ToHexString(metadata.GetBlobBytes(metadata.GetConstant(constantHandle).Value))));
    }
}

sealed class TypeNames : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string element, ArrayShape shape) => element + "[]";
    public string GetByReferenceType(string element) => "ref " + element;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fn";
    public string GetGenericInstantiation(string type, System.Collections.Immutable.ImmutableArray<string> arguments) => type + "<" + string.Join(",", arguments) + ">";
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetModifiedType(string modifier, string type, bool required) => type;
    public string GetPinnedType(string type) => type;
    public string GetPointerType(string type) => type + "*";
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
    public string GetSZArrayType(string type) => type + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind) => reader.GetString(reader.GetTypeReference(handle).Name);
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
}
