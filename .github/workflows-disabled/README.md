# Disabled workflows

GitHub Actions only registers workflow YAML files under `.github/workflows/`.
The SDK CI and NuGet publish workflows live here while the .NET SDK workflow
rollout is paused.

To enable one of these workflows, move it back to `.github/workflows/` and check
that its solution and project paths point at `sdk/dotnet/`.
