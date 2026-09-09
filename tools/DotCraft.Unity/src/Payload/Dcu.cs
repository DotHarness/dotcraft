using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UComponent = UnityEngine.Component;
using UObject = UnityEngine.Object;

namespace DotCraft.Unity
{
    /// <summary>
    /// Small helper surface for Unity Editor snippets.
    /// </summary>
    public static class Dcu
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Finds a loaded type by assembly-qualified name, full name, or unique short name.
        /// </summary>
        public static Type Type(string typeName, bool throwIfMissing = true)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return throwIfMissing ? throw new InvalidOperationException("Type name is empty.") : null;

            var trimmed = typeName.Trim();
            var direct = System.Type.GetType(trimmed, false);
            if (direct != null)
                return direct;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
            {
                Type exact;
                try
                {
                    exact = assembly.GetType(trimmed, false);
                }
                catch
                {
                    continue;
                }

                if (exact != null)
                    return exact;
            }

            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(GetLoadableTypes)
                .Where(t => string.Equals(t.FullName, trimmed, StringComparison.Ordinal)
                            || string.Equals(t.Name, trimmed, StringComparison.Ordinal))
                .Distinct()
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Take(4)
                .ToArray();

            if (matches.Length == 1)
                return matches[0];

            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Type '{trimmed}' is ambiguous: {string.Join(", ", matches.Select(t => t.FullName))}");
            }

            if (throwIfMissing)
                throw new InvalidOperationException($"Type not found: {trimmed}");

            return null;
        }

        /// <summary>
        /// Finds scene components of a loaded component type by type name.
        /// </summary>
        public static UComponent[] Components(string typeName, bool includeInactive = true)
        {
            return Components(Type(typeName), includeInactive);
        }

        /// <summary>
        /// Finds scene components of a loaded component type.
        /// </summary>
        public static UComponent[] Components(Type componentType, bool includeInactive = true)
        {
            if (componentType == null)
                throw new InvalidOperationException("Component type is null.");
            if (!typeof(UComponent).IsAssignableFrom(componentType))
                throw new InvalidOperationException($"{componentType.FullName} is not a Unity Component type.");

            return FindComponentObjects(componentType, includeInactive)
                .OfType<UComponent>()
                .Where(c => c != null && !EditorUtility.IsPersistent(c) && c.gameObject.scene.IsValid())
                .OrderBy(c => c.gameObject.scene.path, StringComparer.Ordinal)
                .ThenBy(c => TransformPath(c.transform), StringComparer.Ordinal)
                .ThenBy(c => c.GetInstanceID())
                .ToArray();
        }

        private static UObject[] FindComponentObjects(Type componentType, bool includeInactive)
        {
#if UNITY_6000_0_OR_NEWER
            return UObject.FindObjectsByType(
                componentType,
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            return includeInactive
                ? Resources.FindObjectsOfTypeAll(componentType)
                : UObject.FindObjectsOfType(componentType);
#endif
        }

        /// <summary>
        /// Reads a field or property from an object instance or a static type.
        /// </summary>
        public static object Get(object targetOrType, string memberName)
        {
            var type = ResolveTarget(targetOrType, out var target, out var flags);
            var field = FindField(type, memberName, flags);
            if (field != null)
                return field.GetValue(target);

            var property = FindProperty(type, memberName, flags);
            if (property != null)
                return property.GetValue(target, null);

            throw MissingMember(type, memberName);
        }

        /// <summary>
        /// Writes a field or property on an object instance or a static type.
        /// </summary>
        public static void Set(object targetOrType, string memberName, object value)
        {
            var type = ResolveTarget(targetOrType, out var target, out var flags);
            var field = FindField(type, memberName, flags);
            if (field != null)
            {
                field.SetValue(target, ConvertValue(value, field.FieldType));
                return;
            }

            var property = FindProperty(type, memberName, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, ConvertValue(value, property.PropertyType), null);
                return;
            }

            throw MissingMember(type, memberName);
        }

        /// <summary>
        /// Calls a method on an object instance or a static type.
        /// </summary>
        public static object Call(object targetOrType, string methodName, params object[] args)
        {
            var type = ResolveTarget(targetOrType, out var target, out var flags);
            var arguments = args ?? Array.Empty<object>();
            var candidates = EnumerateMethods(type, flags)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal)
                            && !m.ContainsGenericParameters)
                .Select(m => TryBuildInvocation(m, arguments, out var converted, out var score)
                    ? new MethodMatch(m, converted, score)
                    : null)
                .Where(m => m != null)
                .OrderBy(m => m.Score)
                .ToArray();

            if (candidates.Length == 0)
                throw MissingMember(type, methodName);

            var best = candidates[0];
            if (candidates.Length > 1 && candidates[1].Score == best.Score)
                throw new InvalidOperationException($"Method '{methodName}' is ambiguous on {type.FullName}.");

            try
            {
                return best.Method.Invoke(target, best.Arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"{methodName} threw {ex.InnerException.GetType().Name}: {ex.InnerException.Message}",
                    ex.InnerException);
            }
        }

        /// <summary>
        /// Lists fields, properties, and methods on a loaded type.
        /// </summary>
        public static DcuMember[] Members(
            string typeName,
            string filter = null,
            bool includeNonPublic = true,
            int limit = 80)
        {
            return Members(Type(typeName), filter, includeNonPublic, limit);
        }

        /// <summary>
        /// Lists fields, properties, and methods on an object instance or a static type.
        /// </summary>
        public static DcuMember[] Members(
            object targetOrType,
            string filter = null,
            bool includeNonPublic = true,
            int limit = 80)
        {
            var type = ResolveTarget(targetOrType, out _, out _);
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            if (includeNonPublic)
                flags |= BindingFlags.NonPublic;

            var loweredFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
            var max = Math.Max(1, limit);
            var members = new List<DcuMember>();

            foreach (var field in EnumerateFields(type, flags))
            {
                if (!NameMatches(field.Name, loweredFilter))
                    continue;
                members.Add(new DcuMember("field", field.Name, FormatType(field.FieldType), field.IsStatic));
            }

            foreach (var property in EnumerateProperties(type, flags))
            {
                if (!NameMatches(property.Name, loweredFilter))
                    continue;
                var accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
                members.Add(new DcuMember(
                    "property",
                    property.Name,
                    FormatType(property.PropertyType),
                    accessor != null && accessor.IsStatic,
                    property.CanRead,
                    property.CanWrite));
            }

            foreach (var method in EnumerateMethods(type, flags).Where(m => !m.IsSpecialName))
            {
                if (!NameMatches(method.Name, loweredFilter))
                    continue;
                members.Add(new DcuMember(
                    "method",
                    method.Name,
                    FormatType(method.ReturnType),
                    method.IsStatic,
                    parameters: string.Join(", ", method.GetParameters().Select(FormatParameter))));
            }

            return members
                .OrderBy(m => m.Kind, StringComparer.Ordinal)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .Take(max)
                .ToArray();
        }

        private static Type ResolveTarget(object targetOrType, out object target, out BindingFlags flags)
        {
            if (targetOrType == null)
                throw new InvalidOperationException("Target is null.");

            if (targetOrType is Type type)
            {
                target = null;
                flags = AnyStatic;
                return type;
            }

            target = targetOrType;
            flags = AnyInstance;
            return targetOrType.GetType();
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            return EnumerateFields(type, flags)
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
        }

        private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            return EnumerateProperties(type, flags)
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)
                                     && p.GetIndexParameters().Length == 0
                                     && p.CanRead);
        }

        private static IEnumerable<FieldInfo> EnumerateFields(Type type, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var field in current.GetFields(flags | BindingFlags.DeclaredOnly))
                    yield return field;
            }
        }

        private static IEnumerable<PropertyInfo> EnumerateProperties(Type type, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var property in current.GetProperties(flags | BindingFlags.DeclaredOnly))
                    yield return property;
            }
        }

        private static IEnumerable<MethodInfo> EnumerateMethods(Type type, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var method in current.GetMethods(flags | BindingFlags.DeclaredOnly))
                    yield return method;
            }
        }

        private static bool TryBuildInvocation(
            MethodInfo method,
            object[] args,
            out object[] converted,
            out int score)
        {
            var parameters = method.GetParameters();
            converted = null;
            score = 0;

            if (parameters.Length != args.Length)
                return false;

            var values = new object[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                if (!TryConvertValue(args[i], parameters[i].ParameterType, out values[i], out var itemScore))
                    return false;
                score += itemScore;
            }

            converted = values;
            return true;
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (TryConvertValue(value, targetType, out var converted, out _))
                return converted;

            throw new InvalidOperationException($"Cannot convert value to {FormatType(targetType)}.");
        }

        private static bool TryConvertValue(object value, Type targetType, out object converted, out int score)
        {
            converted = null;
            score = 0;
            var nullableType = Nullable.GetUnderlyingType(targetType);
            var actualType = nullableType ?? targetType;

            if (value == null)
            {
                if (!targetType.IsValueType || nullableType != null)
                    return true;
                return false;
            }

            if (actualType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            try
            {
                if (actualType.IsEnum)
                {
                    converted = value is string text
                        ? Enum.Parse(actualType, text)
                        : Enum.ToObject(actualType, value);
                    score = 2;
                    return true;
                }

                if (actualType == typeof(string))
                {
                    converted = value.ToString();
                    score = 2;
                    return true;
                }

                if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(actualType))
                {
                    converted = Convert.ChangeType(value, actualType);
                    score = 2;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static InvalidOperationException MissingMember(Type type, string memberName)
        {
            return new InvalidOperationException($"Member not found on {type.FullName}: {memberName}");
        }

        private static bool NameMatches(string name, string filter)
        {
            return filter == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);

            return "/" + string.Join("/", names);
        }

        private static string FormatType(Type type)
        {
            return type == null ? string.Empty : type.FullName ?? type.Name;
        }

        private static string FormatParameter(ParameterInfo parameter)
        {
            return $"{FormatType(parameter.ParameterType)} {parameter.Name}";
        }

        private sealed class MethodMatch
        {
            public MethodMatch(MethodInfo method, object[] arguments, int score)
            {
                Method = method;
                Arguments = arguments;
                Score = score;
            }

            public MethodInfo Method { get; }

            public object[] Arguments { get; }

            public int Score { get; }
        }
    }

    /// <summary>
    /// Describes a reflected member returned by <see cref="Dcu.Members(object, string, bool, int)"/>.
    /// </summary>
    public sealed class DcuMember
    {
        /// <summary>
        /// Creates a reflected member description.
        /// </summary>
        public DcuMember(
            string kind,
            string name,
            string typeName,
            bool isStatic,
            bool canRead = true,
            bool canWrite = false,
            string parameters = "")
        {
            Kind = kind;
            Name = name;
            TypeName = typeName;
            IsStatic = isStatic;
            CanRead = canRead;
            CanWrite = canWrite;
            Parameters = parameters ?? string.Empty;
        }

        /// <summary>
        /// Member kind: field, property, or method.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Member name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Field, property, or return type name.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// Whether the member is static.
        /// </summary>
        public bool IsStatic { get; }

        /// <summary>
        /// Whether the member can be read.
        /// </summary>
        public bool CanRead { get; }

        /// <summary>
        /// Whether the member can be written.
        /// </summary>
        public bool CanWrite { get; }

        /// <summary>
        /// Method parameter list, or empty for fields and properties.
        /// </summary>
        public string Parameters { get; }
    }
}
