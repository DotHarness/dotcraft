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


def build_manifest(
    plugin_id: str,
    skill_name: str | None,
    with_mcp: bool,
    with_assets: bool,
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
                "args": ["./mcp-server/index.js"],
                "cwd": "./",
            }
        }
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


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Create a DotCraft plugin scaffold.")
    parser.add_argument("plugin_id", help="Plugin title or id. It will be normalized to lowercase hyphen-case.")
    parser.add_argument(
        "--path",
        default=str(DEFAULT_PLUGIN_PARENT),
        help="Parent directory for plugin creation. Defaults to <cwd>/.craft/plugins.",
    )
    parser.add_argument(
        "--with-skill",
        dest="with_skill",
        action="store_true",
        default=True,
        help="Create a plugin-contained skill. This is enabled by default.",
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

    skill_name = normalize_plugin_id(args.skill_name or plugin_id) if args.with_skill else None
    if skill_name:
        validate_plugin_id(skill_name)

    parent = Path(args.path).expanduser().resolve()
    plugin_root = parent / plugin_id
    manifest_path = plugin_root / ".craft-plugin" / "plugin.json"

    write_json(
        manifest_path,
        build_manifest(plugin_id, skill_name, args.with_mcp, args.with_assets),
        args.force,
    )

    if skill_name:
        write_text(plugin_root / "skills" / skill_name / "SKILL.md", build_skill_md(skill_name), args.force)
    if args.with_mcp:
        write_json(plugin_root / ".mcp.json", build_mcp_config(), args.force)
    if args.with_assets:
        write_text(plugin_root / "assets" / "plugin-icon.svg", build_icon_svg(plugin_id, "#2563EB"), args.force)
        write_text(plugin_root / "assets" / "plugin-logo.svg", build_icon_svg(plugin_id, "#0F766E"), args.force)

    print(f"Created plugin scaffold: {plugin_root}")
    print(f"Plugin manifest: {manifest_path}")
    if skill_name:
        print(f"Plugin skill: {plugin_root / 'skills' / skill_name / 'SKILL.md'}")
    if args.with_mcp:
        print(f"Plugin MCP config: {plugin_root / '.mcp.json'}")


if __name__ == "__main__":
    main()
