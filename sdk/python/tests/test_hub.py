from __future__ import annotations

from pathlib import Path

import pytest

from dotcraft.hub import (
    HubClient,
    HubLockInfo,
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

    ensured = await client.ensure_default_chat_app_server(client_name="pytest", client_version="0.1")
    body = captured["body"]

    assert captured["path"] == "/v1/appservers/ensure"
    assert isinstance(body, dict)
    assert body["workspacePath"] == str(default_chat_workspace_path(tmp_path))
    assert ensured.ws_url == "ws://127.0.0.1:5000/ws?token=x"
    assert (default_chat_workspace_path(tmp_path) / ".craft" / "config.json").read_text(encoding="utf-8") == "{}\n"
