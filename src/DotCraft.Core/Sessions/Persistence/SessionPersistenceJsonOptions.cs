using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

/// <summary>
/// JSON serialization options shared by model-history persistence and runtime history injection.
/// </summary>
public static class SessionPersistenceJsonOptions
{
    /// <summary>
    /// Canonical options for model-history codec and Framework StateBag history values.
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
        options.Converters.Add(new ReasoningEffortJsonConverter());
        options.Converters.Add(new ReasoningOutputJsonConverter());
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

        if (!polymorphismOptions.DerivedTypes.Any(static dt => dt.DerivedType == typeof(HostedImageGenerationContent)))
        {
            polymorphismOptions.DerivedTypes.Add(
                new JsonDerivedType(typeof(HostedImageGenerationContent), "hosted_image_generation"));
        }

        polymorphismOptions.UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType;
        jsonTypeInfo.PolymorphismOptions = polymorphismOptions;
    }
}
