using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Protocol.Contracts;
using DotCraft.Protocol.Contracts.AppServer;

namespace DotCraft.ProtocolGen;

public static class ContractIrBuilder
{
    private const string DefaultModule = "core";
    private static readonly NullabilityInfoContext Nullability = new();

    public static ContractIr Build(string repositoryRoot)
    {
        var descriptors = AppServerRpcCatalog.All
            .OrderBy(static descriptor => descriptor.Name, StringComparer.Ordinal)
            .ThenBy(static descriptor => descriptor.Direction)
            .ThenBy(static descriptor => descriptor.Kind, StringComparer.Ordinal)
            .ToArray();
        ValidateDescriptors(descriptors, repositoryRoot);

        var runtimeTypes = typeof(AppServerRpc).Assembly.GetTypes()
            .Where(static type => type.IsPublic &&
                                  (type.Namespace == "DotCraft.Protocol.Contracts.AppServer" || type == typeof(RpcEmpty)))
            .Where(static type => type != typeof(AppServerRpc) && type != typeof(AppServerRpcCatalog))
            .Where(static type => !(type.IsAbstract && type.IsSealed))
            .Where(static type => !type.IsGenericTypeDefinition)
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .ToArray();

        var names = runtimeTypes.GroupBy(static type => type.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (names.Length > 0)
            throw new ProtocolGenerationException("DPG003", $"Schema name collision: {string.Join(", ", names)}.");

        var knownTypes = runtimeTypes.ToDictionary(static type => type, TypeId);
        foreach (var runtimeType in runtimeTypes)
        {
            if (AppServerContractJson.Options.GetTypeInfo(runtimeType) is null)
                throw new ProtocolGenerationException("DPG009", $"Type '{runtimeType.FullName}' is missing serializer metadata.");
        }
        var types = runtimeTypes.Select(type => BuildType(type, knownTypes)).ToArray();
        ValidateTypeGraph(types);

        var itemPayloads = SessionItemPayloadCatalog.All
            .OrderBy(static payload => payload.PayloadKind, StringComparer.Ordinal)
            .Select(payload => new ContractItemPayload(
                payload.PayloadKind,
                ResolveTypeId(payload.PayloadType, knownTypes)))
            .ToArray();

        var descriptorMembers = typeof(AppServerRpc).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => typeof(IRpcMethodDescriptor).IsAssignableFrom(field.FieldType))
            .Select(static field => (field.Name, Descriptor: (IRpcMethodDescriptor)field.GetValue(null)!))
            .ToArray();
        var methods = descriptors.Select(descriptor => new ContractMethod(
                descriptor.Name,
                descriptorMembers.Single(member => ReferenceEquals(member.Descriptor, descriptor)).Name,
                descriptor.Kind,
                DirectionName(descriptor.Direction),
                ResolveTypeId(descriptor.ParamsType, knownTypes),
                ResolveTypeId(descriptor.ResultType, knownTypes),
                descriptor.Module,
                descriptor.Capability,
                descriptor.Scope,
                descriptor.NotificationOptOut,
                descriptor.Errors.Order(StringComparer.Ordinal).ToArray(),
                descriptor.Since,
                descriptor.SpecRef.Replace('\\', '/'),
                descriptor.Stability.ToString().ToLowerInvariant()))
            .ToArray();
        ValidateReachability(types, methods, itemPayloads);

        var modules = descriptors.Select(static descriptor => descriptor.Module)
            .Concat(types.Select(static type => type.Module))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(static module => new ContractModule(module, "stable"))
            .ToArray();

        return new ContractIr(
            1,
            "0.1.0",
            "1",
            modules,
            types,
            itemPayloads,
            methods);
    }

    public static ContractIr SelectModules(ContractIr source, IReadOnlyCollection<string> requestedModules)
    {
        if (requestedModules.Count == 0)
            return source;

        var requested = requestedModules.ToHashSet(StringComparer.Ordinal);
        var available = source.Modules.Select(static module => module.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = requested.Where(module => !available.Contains(module)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new ProtocolGenerationException("DPG012", $"Unknown contract module: {string.Join(", ", unknown)}.");

        var methods = source.Methods.Where(method => requested.Contains(method.Module)).ToArray();
        return SelectMethods(source, methods);
    }

    public static ContractIr SelectProfile(ContractIr source, ContractArtifactProfile profile)
    {
        if (profile == ContractArtifactProfile.Experimental)
            return source;
        var methods = source.Methods.Where(static method => method.Stability == "stable").ToArray();
        if (methods.Length == source.Methods.Count)
            return source;

        var methodTypes = CollectReferencedTypeIds(source, source.Methods, []);
        var globalTypes = source.Types.Where(type => !methodTypes.Contains(type.Id)).Select(static type => type.Id);
        return SelectMethods(source, methods, globalTypes);
    }

    private static ContractIr SelectMethods(
        ContractIr source,
        IReadOnlyList<ContractMethod> methods,
        IEnumerable<string>? additionalTypeIds = null)
    {
        var retained = CollectReferencedTypeIds(source, methods, additionalTypeIds ?? []);
        var types = source.Types.Where(type => retained.Contains(type.Id)).ToArray();
        var retainedModules = methods.Select(static method => method.Module)
            .Concat(types.Select(static type => type.Module))
            .ToHashSet(StringComparer.Ordinal);
        var modules = source.Modules.Where(module => retainedModules.Contains(module.Name)).ToArray();
        return source with { Modules = modules, Types = types, Methods = methods };
    }

    private static HashSet<string> CollectReferencedTypeIds(
        ContractIr source,
        IReadOnlyList<ContractMethod> methods,
        IEnumerable<string> additionalTypeIds)
    {
        var typesById = source.Types.ToDictionary(static type => type.Id, StringComparer.Ordinal);
        var retained = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(methods
            .SelectMany(static method => new[] { method.ParamsType, method.ResultType })
            .Concat(source.ItemPayloads.Select(static payload => payload.TypeId))
            .Concat(additionalTypeIds));
        while (pending.TryDequeue(out var typeId))
        {
            if (!retained.Add(typeId) || !typesById.TryGetValue(typeId, out var type))
                continue;
            foreach (var referencedType in ReferencedTypes(type))
                pending.Enqueue(referencedType);
        }
        return retained;
    }

    private static IEnumerable<string> ReferencedTypes(ContractType type)
    {
        foreach (var field in type.Fields)
        foreach (var typeId in ReferencedTypes(field.Type))
            yield return typeId;
        foreach (var variant in type.Variants)
            yield return variant.TypeId;
    }

    private static IEnumerable<string> ReferencedTypes(ContractTypeRef type)
    {
        if (type.TypeId is not null)
            yield return type.TypeId;
        if (type.ElementType is not null)
        foreach (var typeId in ReferencedTypes(type.ElementType))
            yield return typeId;
    }

    private static ContractType BuildType(Type type, IReadOnlyDictionary<Type, string> knownTypes)
    {
        var polymorphic = type.GetCustomAttribute<JsonPolymorphicAttribute>();
        if (type.IsAbstract)
        {
            if (polymorphic is null)
                throw new ProtocolGenerationException("DPG007", $"Union '{type.FullName}' has no discriminator.");

            var variants = type.GetCustomAttributes<JsonDerivedTypeAttribute>()
                .Select(attribute => new ContractUnionVariant(
                    attribute.TypeDiscriminator?.ToString() ?? throw new ProtocolGenerationException("DPG007", $"Union '{type.FullName}' has an unnamed variant."),
                    ResolveTypeId(attribute.DerivedType, knownTypes)))
                .OrderBy(static variant => variant.Discriminator, StringComparer.Ordinal)
                .ToArray();
            if (variants.Length == 0)
                throw new ProtocolGenerationException("DPG007", $"Union '{type.FullName}' has no variants.");

            return NewType(type, ContractTypeKind.Union, [], variants);
        }

        if (type.IsEnum)
            return NewType(type, ContractTypeKind.Enum, [], []);

        var fields = new List<ContractField>();
        var basePolymorphic = FindPolymorphicBase(type);
        if (basePolymorphic is not null)
        {
            var derived = basePolymorphic.GetCustomAttributes<JsonDerivedTypeAttribute>()
                .Single(attribute => attribute.DerivedType == type);
            var discriminatorName = basePolymorphic.GetCustomAttribute<JsonPolymorphicAttribute>()!.TypeDiscriminatorPropertyName
                ?? throw new ProtocolGenerationException("DPG007", $"Union '{basePolymorphic.FullName}' has no discriminator name.");
            fields.Add(new ContractField(discriminatorName, StringRef(), true, false, derived.TypeDiscriminator?.ToString()));
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(static property => property.GetMethod?.IsPublic == true)
                     .OrderBy(WireName, StringComparer.Ordinal))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always ||
                property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null)
                continue;

            var nullability = Nullability.Create(property);
            var propertyType = property.PropertyType;
            var isOptionalWrapper = propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Optional<>);
            var valueNullability = nullability;
            if (isOptionalWrapper)
            {
                propertyType = propertyType.GetGenericArguments()[0];
                valueNullability = nullability.GenericTypeArguments[0];
            }

            var nullable = IsNullable(propertyType, valueNullability);
            var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition;
            var requiredMember = property.GetCustomAttribute<RequiredMemberAttribute>() is not null ||
                                 property.GetCustomAttribute<JsonRequiredAttribute>() is not null;
            var required = !isOptionalWrapper && (requiredMember ||
                (ignore is not JsonIgnoreCondition.WhenWritingNull and not JsonIgnoreCondition.WhenWritingDefault && !nullable));
            var allowsSafeInteger = property.GetCustomAttribute<JsonSafeIntegerAttribute>() is not null;
            if (allowsSafeInteger && !IsSafeIntegerType(propertyType))
                throw new ProtocolGenerationException("DPG005", $"JsonSafeIntegerAttribute on '{type.FullName}.{property.Name}' requires a long, ulong, or nullable equivalent.");

            fields.Add(new ContractField(
                WireName(property),
                BuildTypeRef(
                    propertyType,
                    knownTypes,
                    allowsSafeInteger),
                required,
                nullable));
        }

        return NewType(type, ContractTypeKind.Object, fields, []);

        ContractType NewType(
            Type runtimeType,
            ContractTypeKind kind,
            IReadOnlyList<ContractField> contractFields,
            IReadOnlyList<ContractUnionVariant> variants) =>
            new(
                TypeId(runtimeType),
                runtimeType.Name,
                ContractModuleName(runtimeType),
                kind,
                $"schemas/{ContractModuleName(runtimeType)}/{runtimeType.Name}.schema.json",
                contractFields,
                variants,
                AllowsAdditionalProperties: true,
                runtimeType);
    }

    private static ContractTypeRef BuildTypeRef(
        Type type,
        IReadOnlyDictionary<Type, string> knownTypes,
        bool allowsSafeInteger = false)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string) || type == typeof(Uri) || type == typeof(Guid))
            return StringRef();
        if (type == typeof(bool))
            return new(ContractTypeRefKind.Boolean, "boolean");
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint))
            return new(ContractTypeRefKind.Integer, "integer");
        if (type == typeof(long))
        {
            if (!allowsSafeInteger)
                throw new ProtocolGenerationException("DPG005", $"64-bit contract type '{type.FullName}' requires JsonSafeIntegerAttribute.");
            return new(
                ContractTypeRefKind.Integer,
                "integer",
                Minimum: JsonSafeIntegerAttribute.Minimum,
                Maximum: JsonSafeIntegerAttribute.Maximum);
        }
        if (type == typeof(ulong))
        {
            if (!allowsSafeInteger)
                throw new ProtocolGenerationException("DPG005", $"64-bit contract type '{type.FullName}' requires JsonSafeIntegerAttribute.");
            return new(
                ContractTypeRefKind.Integer,
                "integer",
                Minimum: 0,
                Maximum: JsonSafeIntegerAttribute.Maximum);
        }
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new(ContractTypeRefKind.Number, "number");
        if (type == typeof(DateTimeOffset) || type == typeof(DateTime))
            return new(ContractTypeRefKind.DateTime, "date-time");
        if (type == typeof(JsonElement) || type == typeof(object))
            return new(ContractTypeRefKind.AnyJson, "json");
        if (knownTypes.TryGetValue(type, out var typeId))
            return new(ContractTypeRefKind.Named, typeId, typeId);

        if (TryGetGeneric(type, typeof(IReadOnlyList<>), typeof(IList<>), typeof(List<>), out var itemType) || type.IsArray && (itemType = type.GetElementType()) is not null)
            return new(ContractTypeRefKind.Array, $"array<{BuildTypeRef(itemType!, knownTypes, allowsSafeInteger).DisplayName}>", ElementType: BuildTypeRef(itemType!, knownTypes, allowsSafeInteger));
        if (TryGetDictionaryValue(type, out var valueType))
            return new(ContractTypeRefKind.Map, $"map<{BuildTypeRef(valueType!, knownTypes, allowsSafeInteger).DisplayName}>", ElementType: BuildTypeRef(valueType!, knownTypes, allowsSafeInteger));

        throw new ProtocolGenerationException("DPG005", $"Unsupported contract type '{type.FullName}'.");
    }

    private static bool IsSafeIntegerType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(long) || type == typeof(ulong);
    }

    private static void ValidateDescriptors(IReadOnlyList<IRpcMethodDescriptor> descriptors, string repositoryRoot)
    {
        var duplicates = descriptors.GroupBy(static descriptor => (descriptor.Name, descriptor.Direction, descriptor.Kind))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicates is not null)
            throw new ProtocolGenerationException("DPG001", $"Duplicate method '{duplicates.Key.Name}'.");

        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Name) || string.IsNullOrWhiteSpace(descriptor.Module) ||
                string.IsNullOrWhiteSpace(descriptor.Scope) || string.IsNullOrWhiteSpace(descriptor.Since))
                throw new ProtocolGenerationException("DPG002", $"Method '{descriptor.Name}' has incomplete metadata.");
            var specPath = Path.GetFullPath(Path.Combine(repositoryRoot, descriptor.SpecRef));
            if (!File.Exists(specPath) || !specPath.StartsWith(Path.GetFullPath(repositoryRoot), StringComparison.OrdinalIgnoreCase))
                throw new ProtocolGenerationException("DPG004", $"Method '{descriptor.Name}' has unresolved SpecRef '{descriptor.SpecRef}'.");
        }
    }

    private static void ValidateTypeGraph(IReadOnlyList<ContractType> types)
    {
        var ids = types.Select(static type => type.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var type in types)
        {
            foreach (var reference in type.Fields.SelectMany(static field => Flatten(field.Type)))
            {
                if (reference.Kind == ContractTypeRefKind.Named && !ids.Contains(reference.TypeId!))
                    throw new ProtocolGenerationException("DPG006", $"Type '{type.Id}' references missing type '{reference.TypeId}'.");
            }
            foreach (var variant in type.Variants)
            {
                if (!ids.Contains(variant.TypeId))
                    throw new ProtocolGenerationException("DPG006", $"Union '{type.Id}' references missing variant '{variant.TypeId}'.");
            }
        }
    }

    private static void ValidateReachability(
        IReadOnlyList<ContractType> types,
        IReadOnlyList<ContractMethod> methods,
        IReadOnlyList<ContractItemPayload> itemPayloads)
    {
        var typesById = types.ToDictionary(static type => type.Id, StringComparer.Ordinal);
        var pending = new Queue<string>(methods.SelectMany(static method => new[] { method.ParamsType, method.ResultType })
            .Concat(itemPayloads.Select(static payload => payload.TypeId))
            .Append(TypeId(typeof(RpcError)))
            .Distinct(StringComparer.Ordinal));
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var typeId))
        {
            if (!reachable.Add(typeId) || !typesById.TryGetValue(typeId, out var type))
                continue;
            foreach (var reference in type.Fields.SelectMany(static field => Flatten(field.Type)))
            {
                if (reference.Kind == ContractTypeRefKind.Named)
                    pending.Enqueue(reference.TypeId!);
            }
            foreach (var variant in type.Variants)
                pending.Enqueue(variant.TypeId);
        }

        var orphaned = types.Select(static type => type.Id).Where(id => !reachable.Contains(id)).ToArray();
        if (orphaned.Length > 0)
            throw new ProtocolGenerationException("DPG008", $"Unreachable public contract types: {string.Join(", ", orphaned)}.");
    }

    private static IEnumerable<ContractTypeRef> Flatten(ContractTypeRef reference)
    {
        yield return reference;
        if (reference.ElementType is not null)
        {
            foreach (var nested in Flatten(reference.ElementType))
                yield return nested;
        }
    }

    private static Type? FindPolymorphicBase(Type type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.GetCustomAttribute<JsonPolymorphicAttribute>() is not null)
                return current;
            current = current.BaseType;
        }
        return null;
    }

    private static bool IsNullable(Type type, NullabilityInfo nullability) =>
        Nullable.GetUnderlyingType(type) is not null || nullability.WriteState == NullabilityState.Nullable;

    private static bool TryGetGeneric(Type type, Type first, Type second, Type third, out Type? argument)
    {
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == first || type.GetGenericTypeDefinition() == second || type.GetGenericTypeDefinition() == third))
        {
            argument = type.GetGenericArguments()[0];
            return true;
        }
        argument = null;
        return false;
    }

    private static bool TryGetDictionaryValue(Type type, out Type? valueType)
    {
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) ||
                                   type.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                                   type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) &&
            type.GetGenericArguments()[0] == typeof(string))
        {
            valueType = type.GetGenericArguments()[1];
            return true;
        }
        valueType = null;
        return false;
    }

    private static string WireName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);

    private static string ResolveTypeId(Type type, IReadOnlyDictionary<Type, string> knownTypes) =>
        knownTypes.TryGetValue(type, out var id)
            ? id
            : throw new ProtocolGenerationException("DPG006", $"Descriptor references missing type '{type.FullName}'.");

    private static string TypeId(Type type) => $"{ContractModuleName(type)}.{type.Name}";

    private static string ContractModuleName(MemberInfo type) =>
        type.GetCustomAttribute<ContractModuleAttribute>()?.Name ?? DefaultModule;

    private static string DirectionName(RpcDirection direction) => direction switch
    {
        RpcDirection.ClientToServer => "clientToServer",
        RpcDirection.ServerToClient => "serverToClient",
        _ => throw new ProtocolGenerationException("DPG002", $"Unsupported RPC direction '{direction}'.")
    };

    private static ContractTypeRef StringRef() => new(ContractTypeRefKind.String, "string");
}
