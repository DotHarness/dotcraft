# Published DotCraft NuGet packages in dependency order: every package follows the packages it depends on.
# See specs/sdk/nuget-packages.md for the catalog and its rules.
@(
    @{ Id = 'DotCraft.Imaging'; Project = 'src/DotCraft.Imaging/DotCraft.Imaging.csproj' }
    @{ Id = 'DotCraft.Agents'; Project = 'src/DotCraft.Agents/DotCraft.Agents.csproj' }
    @{ Id = 'DotCraft.Agents.OpenAI'; Project = 'src/DotCraft.Agents.OpenAI/DotCraft.Agents.OpenAI.csproj' }
    @{ Id = 'DotCraft.Agents.Anthropic'; Project = 'src/DotCraft.Agents.Anthropic/DotCraft.Agents.Anthropic.csproj' }
    @{ Id = 'DotCraft.Agents.OpenRouter'; Project = 'src/DotCraft.Agents.OpenRouter/DotCraft.Agents.OpenRouter.csproj' }
    @{ Id = 'DotCraft.Agents.Remote'; Project = 'src/DotCraft.Agents.Remote/DotCraft.Agents.Remote.csproj' }
    @{ Id = 'DotCraft.Protocol'; Project = 'src/DotCraft.Protocol/DotCraft.Protocol.csproj' }
    @{ Id = 'DotCraft.Core'; Project = 'src/DotCraft.Core/DotCraft.Core.csproj' }
    @{ Id = 'DotCraft.Runtime'; Project = 'src/DotCraft.Runtime/DotCraft.Runtime.csproj' }
    @{ Id = 'DotCraft.CSharp'; Project = 'src/DotCraft.CSharp/DotCraft.CSharp.csproj' }
    @{ Id = 'DotCraft.Scripting'; Project = 'src/DotCraft.Scripting/DotCraft.Scripting.csproj' }
    @{ Id = 'DotCraft.CodeMode'; Project = 'src/DotCraft.CodeMode/DotCraft.CodeMode.csproj' }
    @{ Id = 'DotCraft.SessionImport'; Project = 'src/DotCraft.SessionImport/DotCraft.SessionImport.csproj' }
    @{ Id = 'DotCraft.RemoteTools'; Project = 'src/DotCraft.RemoteTools/DotCraft.RemoteTools.csproj' }
    @{ Id = 'DotCraft.Harness'; Project = 'src/DotCraft.Harness/DotCraft.Harness.csproj' }
    @{ Id = 'DotCraft.ModelService'; Project = 'src/DotCraft.ModelService/DotCraft.ModelService.csproj' }
    @{ Id = 'DotCraft.Sdk'; Project = 'sdk/dotnet/src/DotCraft.Sdk/DotCraft.Sdk.csproj' }
)
