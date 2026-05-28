using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// JSON serialization options used for persisting agent session files.
/// </summary>
public static class SessionPersistenceJsonOptions
{
    /// <summary>
    /// Canonical options for thread session save/load paths.
    /// </summary>
    public static readonly JsonSerializerOptions Default = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { ConfigureAiContentPolymorphism }
            }
        };
        return options;
    }

    private static void ConfigureAiContentPolymorphism(JsonTypeInfo jsonTypeInfo)
    {
        if (jsonTypeInfo.Type != typeof(AIContent))
            return;

        var polymorphismOptions = jsonTypeInfo.PolymorphismOptions ?? new JsonPolymorphismOptions();
        if (!polymorphismOptions.DerivedTypes.Any(static dt => dt.DerivedType == typeof(ToolCallArgumentsDeltaContent)))
        {
            polymorphismOptions.DerivedTypes.Add(
                new JsonDerivedType(typeof(ToolCallArgumentsDeltaContent), "tool_call_args_delta"));
        }

        polymorphismOptions.UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType;
        jsonTypeInfo.PolymorphismOptions = polymorphismOptions;
    }
}
