from __future__ import annotations

from pathlib import Path

import pytest

from dotcraft.hub import (
    HubError,
    HubClient,
    HubLockInfo,
    HubRuntimeToolsRequest,
    default_chat_workspace_path,
    ensure_default_chat_workspace,
)


def test_default_chat_workspace_helper_creates_skeleton_without_overwriting_config(tmp_path: Path) -> None:
    workspace = ensure_default_chat_workspace(tmp_path)
    craft = workspace / ".craft"
    config = craft / "config.json"

    assert workspace == default_chat_workspace_path(tmp_path)
    assert config.read_text(encoding="utf-8") == "{}\n"
    assert (craft / "memory").is_dir()
    assert (craft / "skills").is_dir()
    assert (craft / "security").is_dir()

    config.write_text('{"keep":true}\n', encoding="utf-8")
    ensure_default_chat_workspace(tmp_path)

    assert config.read_text(encoding="utf-8") == '{"keep":true}\n'


@pytest.mark.asyncio
async def test_ensure_default_chat_app_server_uses_existing_ensure_endpoint(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    client = HubClient(home_dir=tmp_path)
    captured: dict[str, object] = {}

    async def fake_live_hub() -> HubLockInfo:
        return HubLockInfo(pid=123, api_base_url="http://127.0.0.1:49126", token="hub-token")

    async def fake_post(lock: HubLockInfo, path: str, body: dict) -> dict:
        captured["lock"] = lock
        captured["path"] = path
        captured["body"] = body
        return {
            "workspacePath": body["workspacePath"],
            "canonicalWorkspacePath": body["workspacePath"],
            "state": "running",
            "pid": 123,
            "endpoints": {"appServerWebSocket": "ws://127.0.0.1:5000/ws?token=x"},
            "serviceStatus": {},
            "serverVersion": "0.1",
            "startedByHub": True,
        }

    monkeypatch.setattr(client, "try_get_live_hub", fake_live_hub)
    monkeypatch.setattr(client, "_post", fake_post)

    ensured = await client.ensure_default_chat_app_server(
        client_name="pytest",
        client_version="0.1",
        start_if_missing=False,
    )
    body = captured["body"]

    assert captured["path"] == "/v1/appservers/ensure"
    assert isinstance(body, dict)
    assert body["workspacePath"] == str(default_chat_workspace_path(tmp_path))
    assert body["startIfMissing"] is False
    assert ensured.ws_url == "ws://127.0.0.1:5000/ws?token=x"
    assert (default_chat_workspace_path(tmp_path) / ".craft" / "config.json").read_text(encoding="utf-8") == "{}\n"


@pytest.mark.asyncio
async def test_management_methods_share_models_and_runtime_tools(monkeypatch: pytest.MonkeyPatch) -> None:
    client = HubClient()
    lock = HubLockInfo(pid=123, api_base_url="http://127.0.0.1:49127", token="hub-token")
    captured: list[tuple[str, dict | None]] = []

    async def ensure_hub() -> HubLockInfo:
        return lock

    async def fake_get(_lock: HubLockInfo, path: str):
        captured.append((path, None))
        if path == "/v1/status":
            return {
                "hubVersion": "test", "pid": 123, "startedAt": "now", "statePath": "state",
                "apiBaseUrl": lock.api_base_url, "capabilities": {"appServerManagement": True},
            }
        return []

    async def fake_post(_lock: HubLockInfo, path: str, body: dict):
        captured.append((path, body))
        return {
            "workspacePath": "/repo", "canonicalWorkspacePath": "/repo", "state": "running",
            "endpoints": {}, "serviceStatus": {}, "startedByHub": True,
        }

    monkeypatch.setattr(client, "ensure_hub", ensure_hub)
    monkeypatch.setattr(client, "_get", fake_get)
    monkeypatch.setattr(client, "_post", fake_post)

    tools = HubRuntimeToolsRequest(
        ripgrep_path="/tools/rg",
        built_in_plugin_roots="/plugins",
        default_plugin_registry_url="https://plugins.example/index.json",
    )
    assert (await client.restart_app_server("/repo", tools)).state == "running"
    assert (await client.stop_app_server("/repo")).workspace_path == "/repo"
    assert await client.list_app_servers() == []
    assert (await client.get_status()).capabilities["appServerManagement"] is True
    restart_body = next(body for path, body in captured if path == "/v1/appservers/restart")
    assert restart_body is not None
    assert restart_body["runtimeTools"]["builtInPluginRoots"] == "/plugins"


@pytest.mark.asyncio
async def test_binary_mismatch_error_preserves_structured_details(monkeypatch: pytest.MonkeyPatch) -> None:
    client = HubClient(expected_executable="/new/dotcraft", binary_match_policy="errorIfMismatch")

    async def fake_live() -> HubLockInfo:
        return HubLockInfo(
            pid=123,
            api_base_url="http://127.0.0.1:49128",
            token="hub-token",
            binary_path="/old/dotcraft",
        )

    monkeypatch.setattr(client, "try_get_live_hub", fake_live)
    with pytest.raises(HubError) as captured:
        await client.ensure_hub()
    assert captured.value.code == "hubBinaryMismatch"
    assert Path(captured.value.details["actualExecutable"]).name == "dotcraft"
