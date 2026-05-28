# Start the OpenSandbox server for DotCraft tool execution sandboxing.
# Requires: Python 3.10+, Docker, opensandbox-server on PATH.
#   Install: pip install opensandbox-server
#   Or:      uv pip install opensandbox-server --system
#
# The server port is read from .craft/config.json (Tools.Sandbox.Domain).
# A local sandbox config is generated at .craft/sandbox.toml on each run,
# inheriting all settings from the base config but overriding the port.
#
# Base config path: defaults to ~/.sandbox.toml
#   Generate: opensandbox-server init-config ~/.sandbox.toml --example docker
#   Override: set environment variable SANDBOX_CONFIG_PATH to a custom path
#
# If `opensandbox-server` is not on PATH, set environment variable
# OPENSANDBOX_SERVER_EXE to the full path of the executable.

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$craftDir    = Join-Path $scriptDir ".craft"
$dotcraftCfg = Join-Path $craftDir "config.json"
$localToml   = Join-Path $craftDir "sandbox.toml"

# --- Resolve sandbox executable ---
$sandboxExe = if ($env:OPENSANDBOX_SERVER_EXE) {
    $env:OPENSANDBOX_SERVER_EXE
} else {
    $fromPath = Get-Command "opensandbox-server" -ErrorAction SilentlyContinue
    if ($fromPath) {
        $fromPath.Source
    } else {
        $scriptsDir = python -c "import sysconfig; print(sysconfig.get_path('scripts'))" 2>$null
        if ($scriptsDir) { Join-Path $scriptsDir "opensandbox-server.exe" } else { "opensandbox-server" }
    }
}

if (-not (Get-Command $sandboxExe -ErrorAction SilentlyContinue) -and -not (Test-Path $sandboxExe)) {
    Write-Error "opensandbox-server not found. Install it with: pip install opensandbox-server"
    Write-Error "Or install it with: uv pip install opensandbox-server --system"
    Write-Error "Or set OPENSANDBOX_SERVER_EXE to the full path of the executable."
    exit 1
}

# --- Resolve base config ---
$baseToml = if ($env:SANDBOX_CONFIG_PATH) { $env:SANDBOX_CONFIG_PATH } else { "$HOME\.sandbox.toml" }

if (-not (Test-Path $baseToml)) {
    Write-Error "Base config not found at $baseToml. Generate it with: opensandbox-server init-config $baseToml --example docker"
    Write-Error "Or set SANDBOX_CONFIG_PATH to point at an existing config file."
    exit 1
}

# --- Read port and sandbox image from .craft/config.json (Tools.Sandbox) ---
$sandboxPort = 5880
$sandboxImage = "ubuntu:latest"
if (Test-Path $dotcraftCfg) {
    $dotcraftJson = Get-Content $dotcraftCfg -Raw | ConvertFrom-Json
    $domain = $dotcraftJson.Tools.Sandbox.Domain
    if ($domain -match ":(\d+)$") {
        $sandboxPort = [int]$Matches[1]
    }
    if ($dotcraftJson.Tools.Sandbox.PSObject.Properties['Image'] -and -not [string]::IsNullOrWhiteSpace($dotcraftJson.Tools.Sandbox.Image)) {
        $sandboxImage = $dotcraftJson.Tools.Sandbox.Image
    }
    Write-Host "Sandbox port from config.json (Tools.Sandbox.Domain): $sandboxPort"
} else {
    Write-Warning ".craft/config.json not found, using default port $sandboxPort"
}

# --- Generate local sandbox.toml with the resolved port ---
$tomlContent = Get-Content $baseToml -Raw
# Replace the port value in the [server] section
$tomlContent = $tomlContent -replace '(?m)^(port\s*=\s*)\d+', "`${1}$sandboxPort"
# Remove the entire [egress] section — egress container is not needed (NetworkPolicy = allow)
# The server validates egress.image as non-empty, so we must drop the block entirely
$tomlContent = $tomlContent -replace '(?ms)\[egress\][^[]*', ''
New-Item -ItemType Directory -Force -Path $craftDir | Out-Null
# Write without BOM — TOML parsers reject the UTF-8 BOM that Set-Content -Encoding UTF8 emits
[System.IO.File]::WriteAllText($localToml, $tomlContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated $localToml (port=$sandboxPort)"

# --- Pre-pull images to eliminate cold-start on first tool call ---
$dockerCmd = Get-Command "docker" -ErrorAction SilentlyContinue
if ($dockerCmd) {
    Write-Host "Pre-pulling sandbox images..."
    docker pull $sandboxImage 2>&1 | Out-Null
    docker pull "opensandbox/execd:v1.0.6" 2>&1 | Out-Null
    Write-Host "Pre-pull done."
} else {
    Write-Warning "docker not found; skipping pre-pull. First tool call may be slow."
}

# --- Start server ---
Write-Host "Starting OpenSandbox server (config: $localToml)..."
& $sandboxExe --config $localToml
