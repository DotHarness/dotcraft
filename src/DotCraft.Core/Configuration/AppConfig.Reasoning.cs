using Microsoft.Extensions.AI;

namespace DotCraft.Configuration;

public sealed partial class AppConfig
{
    [ConfigSection("Reasoning", DisplayName = "Reasoning", Order = 10)]
    public sealed class ReasoningConfig
    {
        /// <summary>
        /// Whether to request provider reasoning support.
        /// Unsupported providers or models may ignore this setting.
        /// </summary>
        [ConfigField(Hint = "Request provider reasoning/thinking support when available.")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Requested reasoning effort level when reasoning is enabled.
        /// </summary>
        public ModelReasoningEffort Effort { get; set; } = ModelReasoningEffort.Medium;

        /// <summary>
        /// Controls how much reasoning content is exposed in responses.
        /// The default exposes full summary.
        /// </summary>
        [ConfigField(Hint = "Controls whether reasoning content is exposed in responses.")]
        public ReasoningOutput Output { get; set; } = ReasoningOutput.Full;

        public void ApplyTo(ChatOptions options) =>
            ProviderReasoningOptions.Apply(options, Enabled, Effort.ToProviderEffort(), Output);

        /// <summary>Returns MEAI-representable fields; use ApplyTo for complete requests including Max metadata.</summary>
        public ReasoningOptions? ToOptions()
        {
            if (!Enabled)
                return null;

            return new ReasoningOptions
            {
                Effort = Effort.ToProviderEffort().ToMeaiEffort(),
                Output = Output
            };
        }
    }
}
