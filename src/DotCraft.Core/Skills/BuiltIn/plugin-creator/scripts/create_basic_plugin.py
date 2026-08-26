#!/usr/bin/env python3
"""Scaffold a DotCraft local plugin directory."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any


MAX_PLUGIN_ID_LENGTH = 64
DEFAULT_PLUGIN_PARENT = Path.cwd() / ".craft" / "plugins"
DEFAULT_DOTNET_PROJECT_PARENT = Path.cwd() / ".craft" / "plugin-projects"


def normalize_plugin_id(value: str) -> str:
    normalized = value.strip().lower()
    normalized = re.sub(r"[^a-z0-9]+", "-", normalized)
    normalized = re.sub(r"-{2,}", "-", normalized).strip("-")
    return normalized


def validate_plugin_id(plugin_id: str) -> None:
    if not plugin_id:
        raise ValueError("Plugin id must include at least one letter or digit.")
    if len(plugin_id) > MAX_PLUGIN_ID_LENGTH:
        raise ValueError(
            f"Plugin id '{plugin_id}' is too long ({len(plugin_id)} characters). "
            f"Maximum is {MAX_PLUGIN_ID_LENGTH} characters."
        )


def display_name_from_id(plugin_id: str) -> str:
    return " ".join(part.capitalize() for part in plugin_id.split("-") if part)


def dotnet_identifier_from_id(plugin_id: str) -> str:
    identifier = "".join(part.capitalize() for part in plugin_id.split("-") if part)
    return f"P{identifier}" if identifier[0].isdigit() else identifier


def build_dotnet_manifest(plugin_id: str, host_version: str) -> dict[str, Any]:
    display_name = display_name_from_id(plugin_id)
    identifier = dotnet_identifier_from_id(plugin_id)
    return {
        "schemaVersion": 1,
        "id": plugin_id,
        "version": "0.1.0",
        "displayName": display_name,
        "description": f"Managed tools for {display_name}.",
        "capabilities": ["dotnet"],
        "dotnet": {
            "minHostVersion": host_version,
            "entryAssembly": f"./lib/{identifier}.Plugin.dll",
            "entryType": f"DotCraft.Plugin.{identifier}.Plugin",
        },
    }


def build_dotnet_plugin_cs(plugin_id: str) -> str:
    display_name = display_name_from_id(plugin_id)
    identifier = dotnet_identifier_from_id(plugin_id)
    tool_name = plugin_id.replace("-", "_")
    return f"""using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Plugin.{identifier};

public sealed class Plugin : IDotCraftPlugin
{{
    public ValueTask ActivateAsync(
        IPluginActivationContext context,
        CancellationToken cancellationToken)
    {{
        context.Contributions.Add<IToolSource>(new PluginTool());
        return ValueTask.CompletedTask;
    }}
}}

internal sealed class PluginTool : AIFunctionToolSource
{{
    public override string SourceId => "{plugin_id}";

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
    {{
        yield return AIFunctionFactory.Create(
            () => "{display_name} is active.",
            name: "{tool_name}",
            description: "Reports whether {display_name} is active.");
    }}

    protected override ToolPolicyHints GetPolicyHints(
        AIFunction function,
        ToolPlanningContext context) => new(ReadOnly: true);
}}
"""


def build_manifest(
    plugin_id: str,
    skill_name: str | None,
    with_mcp: bool,
    with_hooks: bool,
    with_assets: bool,
    with_desktop_extension: bool,
) -> dict[str, Any]:
    display_name = display_name_from_id(plugin_id)
    capabilities = []
    interface_capabilities = []
    if skill_name:
        capabilities.append("skill")
        interface_capabilities.append("Skill")
    if with_mcp:
        capabilities.append("mcp")
        interface_capabilities.append("MCP")
    if with_hooks:
        capabilities.append("hooks")
        interface_capabilities.append("Hooks")
    if with_desktop_extension:
        capabilities.append("desktopExtension")
        interface_capabilities.append("Desktop")
    if not capabilities:
        capabilities.append("metadata")
        interface_capabilities.append("Metadata")

    manifest: dict[str, Any] = {
        "schemaVersion": 1,
        "id": plugin_id,
        "version": "0.1.0",
        "displayName": display_name,
        "description": "[TODO: Describe what this plugin contributes.]",
        "capabilities": capabilities,
        "interface": {
            "displayName": display_name,
            "shortDescription": "[TODO: One-line plugin summary.]",
            "longDescription": "[TODO: Concise plugin detail description.]",
            "developerName": "[TODO: Developer or team name]",
            "category": "Coding",
            "capabilities": interface_capabilities,
            "defaultPrompt": f"Use {display_name}.",
            "brandColor": "#2563EB",
        },
    }

    if skill_name:
        manifest["skills"] = "./skills/"
    if with_mcp:
        manifest["mcpServers"] = "./.mcp.json"
    if with_hooks:
        manifest["hooks"] = "./hooks/hooks.json"
    if with_desktop_extension:
        manifest["desktopExtensions"] = "./desktop-extensions.json"
    if with_assets:
        manifest["interface"]["composerIcon"] = "./assets/plugin-icon.svg"
        manifest["interface"]["logo"] = "./assets/plugin-logo.svg"

    return manifest


def build_mcp_config() -> dict[str, Any]:
    return {
        "mcpServers": {
            "demo": {
                "transport": "stdio",
                "command": "node",
                "arguments": ["./mcp-server/index.js"],
                "cwd": "./",
            }
        }
    }


def build_hooks_config() -> dict[str, Any]:
    return {
        "hooks": {
            "SessionStart": [
                {
                    "matcher": "",
                    "hooks": [
                        {
                            "type": "command",
                            "command": "node \"${DOTCRAFT_PLUGIN_ROOT}/hooks/session-start.js\"",
                            "timeout": 30,
                        }
                    ],
                }
            ]
        }
    }


def build_desktop_extensions(plugin_id: str) -> dict[str, Any]:
    display_name = display_name_from_id(plugin_id)
    return {
        "extensions": [
            {
                "id": "desktop",
                "displayName": f"{display_name} Desktop",
                "description": "[TODO: Describe the Desktop surface this plugin adds.]",
                "entry": "./desktop/main-view.mjs",
                "styles": [],
                "surfaces": [
                    {
                        "type": "mainView",
                        "viewId": "main",
                        "label": display_name,
                        "placement": "sidebar",
                        "order": 80,
                    },
                    {
                        "type": "pluginDetail",
                        "title": f"{display_name} Desktop",
                        "description": "Adds a Desktop surface for this plugin.",
                    },
                ],
                "requiredAppIds": [],
                "connectOrigins": [],
            }
        ]
    }


def write_json(path: Path, data: dict[str, Any], force: bool) -> None:
    if path.exists() and not force:
        raise FileExistsError(f"{path} already exists. Use --force to overwrite.")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def write_text(path: Path, content: str, force: bool) -> None:
    if path.exists() and not force:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def build_skill_md(skill_name: str) -> str:
    title = display_name_from_id(skill_name)
    return f"""---
name: {skill_name}
description: "[TODO: Describe when DotCraft should use this plugin-provided skill.]"
---

# {title}

## Workflow

1. [TODO: Replace with the reusable plugin workflow.]

## Verification

- [TODO: Explain how to confirm this skill worked.]
"""


def build_icon_svg(label: str, color: str) -> str:
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" fill="none">
  <rect width="64" height="64" rx="12" fill="{color}"/>
  <path d="M18 19h28v26H18V19Z" fill="#F8FAFC"/>
  <path d="M24 27h16M24 34h10" stroke="{color}" stroke-width="4" stroke-linecap="round"/>
  <text x="32" y="53" text-anchor="middle" font-family="Arial, sans-serif" font-size="8" fill="#F8FAFC">{label[:6]}</text>
</svg>
"""


def build_desktop_main_view_js(plugin_id: str) -> str:
    display_name = display_name_from_id(plugin_id)
    return f"""export function activate(host) {{
  const React = host.react

  function MainView() {{
    return React.createElement('div', {{
      style: {{
        height: '100%',
        display: 'grid',
        placeItems: 'center',
        padding: 24,
        background: 'var(--bg-primary)',
        color: 'var(--text-secondary)'
      }}
    }}, React.createElement('section', {{
      style: {{
        width: 'min(720px, 100%)',
        border: '1px solid var(--border)',
        borderRadius: 8,
        padding: 20,
        display: 'grid',
        gap: 10,
        background: 'var(--bg-secondary)'
      }}
    }}, [
      React.createElement('h2', {{
        key: 'title',
        style: {{
          margin: 0,
          color: 'var(--text-primary)',
          fontSize: 'var(--type-title-size)',
          lineHeight: 'var(--type-title-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)'
        }}
      }}, '{display_name}'),
      React.createElement('p', {{
        key: 'body',
        style: {{
          margin: 0,
          fontSize: 'var(--type-body-size)',
          lineHeight: 'var(--type-body-line-height)'
        }}
      }}, 'Replace desktop/main-view.mjs with this plugin\\'s Desktop UI.')
    ]))
  }}

  return {{
    mainViews: {{
      main: MainView
    }}
  }}
}}
"""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Create a DotCraft plugin scaffold.")
    parser.add_argument("plugin_id", help="Plugin title or id. It will be normalized to lowercase hyphen-case.")
    parser.add_argument(
        "--dotnet",
        action="store_true",
        help="Create a source-based .NET plugin project for DotCraft authoring tools.",
    )
    parser.add_argument(
        "--path",
        help="Parent directory for an ordinary plugin bundle. Defaults to <cwd>/.craft/plugins.",
    )
    parser.add_argument(
        "--with-skill",
        dest="with_skill",
        action="store_true",
        default=None,
        help=(
            "Create a plugin-contained skill. Enabled by default for ordinary plugin bundles; "
            "not available with --dotnet."
        ),
    )
    parser.add_argument(
        "--without-skill",
        dest="with_skill",
        action="store_false",
        help="Do not create a plugin-contained skill.",
    )
    parser.add_argument("--skill-name", help="Skill directory/name. Defaults to the plugin id.")
    parser.add_argument(
        "--with-mcp",
        action="store_true",
        help="Create a plugin-bundled .mcp.json placeholder.",
    )
    parser.add_argument(
        "--with-hooks",
        action="store_true",
        help="Create a plugin-bundled hooks/hooks.json placeholder.",
    )
    parser.add_argument(
        "--with-desktop-extension",
        action="store_true",
        help="Create a trusted Desktop extension descriptor and placeholder main view.",
    )
    parser.add_argument("--with-assets", action="store_true", help="Create plugin-level SVG icon/logo placeholders.")
    parser.add_argument("--force", action="store_true", help="Overwrite generated files that already exist.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    raw_plugin_id = args.plugin_id
    plugin_id = normalize_plugin_id(raw_plugin_id)
    if plugin_id != raw_plugin_id:
        print(f"Note: Normalized plugin id from '{raw_plugin_id}' to '{plugin_id}'.")
    validate_plugin_id(plugin_id)

    if args.dotnet and args.path is not None:
        raise ValueError("--path is not valid with --dotnet; .NET projects use .craft/plugin-projects.")

    with_skill = args.with_skill if args.with_skill is not None else not args.dotnet
    skill_name = normalize_plugin_id(args.skill_name or plugin_id) if with_skill else None
    if skill_name:
        validate_plugin_id(skill_name)

    default_parent = DEFAULT_DOTNET_PROJECT_PARENT if args.dotnet else DEFAULT_PLUGIN_PARENT
    parent = Path(args.path or default_parent).expanduser().resolve()
    project_root = parent / plugin_id
    plugin_root = project_root / "plugin" if args.dotnet else project_root
    manifest_path = plugin_root / ".craft-plugin" / "plugin.json"

    if args.dotnet:
        if (
            args.skill_name
            or skill_name
            or args.with_mcp
            or args.with_hooks
            or args.with_assets
            or args.with_desktop_extension
        ):
            raise ValueError("The .NET authoring scaffold creates one minimal managed Tool plugin.")
        marker_path = Path(__file__).resolve().parent.parent / ".builtin"
        host_version = marker_path.read_text(encoding="utf-8").strip()
        write_json(manifest_path, build_dotnet_manifest(plugin_id, host_version), args.force)
        write_text(project_root / "src" / "Plugin.cs", build_dotnet_plugin_cs(plugin_id), args.force)
        (plugin_root / "lib").mkdir(parents=True, exist_ok=True)
        print(f"Created .NET plugin project: {project_root}")
        print(f"Plugin source: {project_root / 'src' / 'Plugin.cs'}")
        print(f"Plugin manifest: {manifest_path}")
        return

    write_json(
        manifest_path,
        build_manifest(
            plugin_id,
            skill_name,
            args.with_mcp,
            args.with_hooks,
            args.with_assets,
            args.with_desktop_extension,
        ),
        args.force,
    )

    if skill_name:
        write_text(plugin_root / "skills" / skill_name / "SKILL.md", build_skill_md(skill_name), args.force)
    if args.with_mcp:
        write_json(plugin_root / ".mcp.json", build_mcp_config(), args.force)
    if args.with_hooks:
        write_json(plugin_root / "hooks" / "hooks.json", build_hooks_config(), args.force)
        write_text(
            plugin_root / "hooks" / "session-start.js",
            "process.stdin.resume();\nprocess.stdin.on('end', () => process.exit(0));\n",
            args.force,
        )
    if args.with_desktop_extension:
        write_json(plugin_root / "desktop-extensions.json", build_desktop_extensions(plugin_id), args.force)
        write_text(plugin_root / "desktop" / "main-view.mjs", build_desktop_main_view_js(plugin_id), args.force)
    if args.with_assets:
        write_text(plugin_root / "assets" / "plugin-icon.svg", build_icon_svg(plugin_id, "#2563EB"), args.force)
        write_text(plugin_root / "assets" / "plugin-logo.svg", build_icon_svg(plugin_id, "#0F766E"), args.force)

    print(f"Created plugin scaffold: {plugin_root}")
    print(f"Plugin manifest: {manifest_path}")
    if skill_name:
        print(f"Plugin skill: {plugin_root / 'skills' / skill_name / 'SKILL.md'}")
    if args.with_mcp:
        print(f"Plugin MCP config: {plugin_root / '.mcp.json'}")
    if args.with_hooks:
        print(f"Plugin hooks config: {plugin_root / 'hooks' / 'hooks.json'}")
    if args.with_desktop_extension:
        print(f"Plugin Desktop extensions: {plugin_root / 'desktop-extensions.json'}")


if __name__ == "__main__":
    main()
