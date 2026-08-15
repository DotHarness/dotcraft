using DotCraft.Workspaces;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Microsoft.Extensions.Logging;

namespace DotCraft.Lsp;

public class LspServerManager(
    AppConfig config,
    WorkspacePaths paths,
    ILogger<LspServerManager>? logger = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, LspServerInstance> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _extensionMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _openedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _fileVersions = new(StringComparer.OrdinalIgnoreCase);

    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await ShutdownInternalAsync();

            if (!config.Tools.Lsp.Enabled)
            {
                logger?.LogDebug("LSP tool is disabled in configuration.");
                return;
            }

            var serverConfigs = PluginLspServerResolver.LoadEffectiveServers(
                config,
                paths.WorkspacePath,
                paths.CraftPath,
                out var pluginDiagnostics);
            if (pluginDiagnostics.Count > 0)
                PluginDiagnosticsStore.Shared.Append(pluginDiagnostics);

            foreach (var serverConfig in serverConfigs.Where(s => s.Enabled))
            {
                if (string.IsNullOrWhiteSpace(serverConfig.Name))
                {
                    logger?.LogWarning("Skipping unnamed LSP server entry.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(serverConfig.Command))
                {
                    logger?.LogWarning("Skipping LSP server {Server}: Command is empty.", serverConfig.Name);
                    continue;
                }

                if (serverConfig.ExtensionToLanguage.Count == 0)
                {
                    logger?.LogWarning("Skipping LSP server {Server}: ExtensionToLanguage is empty.", serverConfig.Name);
                    continue;
                }

                if (!serverConfig.Transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
                {
                    logger?.LogWarning(
                        "Skipping LSP server {Server}: transport '{Transport}' is not supported yet.",
                        serverConfig.Name,
                        serverConfig.Transport);
                    continue;
                }

                var normalizedConfig = serverConfig.Clone();
                if (!string.IsNullOrWhiteSpace(normalizedConfig.WorkspaceFolder)
                    && !Path.IsPathRooted(normalizedConfig.WorkspaceFolder))
                {
                    normalizedConfig.WorkspaceFolder = Path.GetFullPath(
                        Path.Combine(paths.WorkspacePath, normalizedConfig.WorkspaceFolder));
                }

                var instance = new LspServerInstance(
                    normalizedConfig.Name,
                    normalizedConfig,
                    paths.WorkspacePath,
                    logger);

                instance.OnRequest(
                    "workspace/configuration",
                    paramsElement =>
                    {
                        if (paramsElement.ValueKind != JsonValueKind.Object
                            || !paramsElement.TryGetProperty("items", out var itemsElement)
                            || itemsElement.ValueKind != JsonValueKind.Array)
                        {
                            return Task.FromResult<object?>(Array.Empty<object?>());
                        }

                        var count = itemsElement.GetArrayLength();
                        var result = new object?[count];
                        return Task.FromResult<object?>(result);
                    });

                lock (_stateLock)
                {
                    _servers[normalizedConfig.Name] = instance;
                    foreach (var ext in normalizedConfig.ExtensionToLanguage.Keys)
                    {
                        var normalizedExt = NormalizeExtension(ext);
                        if (!_extensionMap.TryGetValue(normalizedExt, out var list))
                        {
                            list = [];
                            _extensionMap[normalizedExt] = list;
                        }

                        list.Add(normalizedConfig.Name);
                    }
                }
            }

            logger?.LogInformation("LSP server manager initialized with {Count} server(s).", _servers.Count);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public virtual IReadOnlyDictionary<string, LspServerInstance> GetAllServers()
    {
        lock (_stateLock)
        {
            return new Dictionary<string, LspServerInstance>(_servers, StringComparer.OrdinalIgnoreCase);
        }
    }

    public virtual LspServerInstance? GetServerForFile(string filePath)
    {
        var ext = NormalizeExtension(Path.GetExtension(filePath));
        if (string.IsNullOrEmpty(ext))
            return null;

        lock (_stateLock)
        {
            if (!_extensionMap.TryGetValue(ext, out var serverNames) || serverNames.Count == 0)
                return null;

            var serverName = serverNames[0];
            return serverName != null && _servers.TryGetValue(serverName, out var server)
                ? server
                : null;
        }
    }

    public virtual async Task<LspServerInstance?> EnsureServerStartedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var server = GetServerForFile(filePath);
        if (server == null)
            return null;

        if (server.State is LspServerState.Stopped or LspServerState.Error)
            await server.StartAsync(cancellationToken);

        return server;
    }

    public virtual async Task<JsonElement?> SendRequestAsync(
        string filePath,
        string method,
        object? @params,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken);
        if (server == null)
            return null;

        return await server.SendRequestAsync(method, @params, timeout, cancellationToken);
    }

    public virtual async Task OpenFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken);
        if (server == null)
            return;

        var uri = LspUriHelpers.ToFileUri(filePath);

        lock (_stateLock)
        {
            if (_openedFiles.TryGetValue(uri, out var openedOn)
                && string.Equals(openedOn, server.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _openedFiles[uri] = server.Name;
            _fileVersions[uri] = 1;
        }

        var extension = NormalizeExtension(Path.GetExtension(filePath));
        var languageId = server.Config.ExtensionToLanguage.TryGetValue(extension, out var lang)
            ? lang
            : "plaintext";

        await server.SendNotificationAsync(
            "textDocument/didOpen",
            new
            {
                textDocument = new
                {
                    uri,
                    languageId,
                    version = 1,
                    text = content
                }
            },
            cancellationToken);
    }

    public virtual async Task ChangeFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var server = await EnsureServerStartedAsync(filePath, cancellationToken);
        if (server == null)
            return;

        var uri = LspUriHelpers.ToFileUri(filePath);
        var shouldOpen = false;
        int version;
        lock (_stateLock)
        {
            shouldOpen = !_openedFiles.TryGetValue(uri, out var openedOn)
                         || !string.Equals(openedOn, server.Name, StringComparison.OrdinalIgnoreCase);

            if (shouldOpen)
            {
                version = 1;
            }
            else
            {
                version = (_fileVersions.TryGetValue(uri, out var current) ? current : 1) + 1;
                _fileVersions[uri] = version;
            }
        }

        if (shouldOpen)
        {
            await OpenFileAsync(filePath, content, cancellationToken);
            return;
        }

        await server.SendNotificationAsync(
            "textDocument/didChange",
            new
            {
                textDocument = new
                {
                    uri,
                    version
                },
                contentChanges = new[]
                {
                    new
                    {
                        text = content
                    }
                }
            },
            cancellationToken);
    }

    public virtual async Task SaveFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var server = GetServerForFile(filePath);
        if (server == null || !server.IsHealthy())
            return;

        await server.SendNotificationAsync(
            "textDocument/didSave",
            new
            {
                textDocument = new
                {
                    uri = LspUriHelpers.ToFileUri(filePath)
                }
            },
            cancellationToken);
    }

    public virtual async Task CloseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var server = GetServerForFile(filePath);
        if (server == null || !server.IsHealthy())
            return;

        var uri = LspUriHelpers.ToFileUri(filePath);
        await server.SendNotificationAsync(
            "textDocument/didClose",
            new
            {
                textDocument = new
                {
                    uri
                }
            },
            cancellationToken);

        lock (_stateLock)
        {
            _openedFiles.Remove(uri);
            _fileVersions.Remove(uri);
        }
    }

    public virtual bool IsFileOpen(string filePath)
    {
        var uri = LspUriHelpers.ToFileUri(filePath);
        lock (_stateLock)
        {
            return _openedFiles.ContainsKey(uri);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await ShutdownInternalAsync();
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private async Task ShutdownInternalAsync()
    {
        List<LspServerInstance> servers;
        lock (_stateLock)
        {
            servers = [.. _servers.Values];
            _servers.Clear();
            _extensionMap.Clear();
            _openedFiles.Clear();
            _fileVersions.Clear();
        }

        foreach (var server in servers)
        {
            try
            {
                await server.StopAsync();
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Failed to stop LSP server {Server}", server.Name);
            }
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }
}
