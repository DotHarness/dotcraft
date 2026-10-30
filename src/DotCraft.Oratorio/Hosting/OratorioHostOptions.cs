namespace DotCraft.Oratorio.Hosting;

public sealed class OratorioHostOptions
{
    public string? StateRoot { get; init; }
    public string? ListenUrl { get; init; }
    public string? ServiceId { get; init; }
    public string? ServiceToken { get; init; }
    public string? EnvironmentName { get; init; }
}
