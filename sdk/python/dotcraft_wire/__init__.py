"""
dotcraft_wire — compatibility alias for the DotCraft Python SDK.

The canonical package is now ``dotcraft``. This module re-exports the low-level
wire client, transports, channel adapter, models, and constants so existing
``from dotcraft_wire import ...`` imports keep working. New code should import
from ``dotcraft``.
"""

from dotcraft import __version__
from dotcraft.adapter import ChannelAdapter
from dotcraft.client import DotCraftClient, DotCraftError
from dotcraft.models import (
    DECISION_ACCEPT,
    DECISION_ACCEPT_ALWAYS,
    DECISION_ACCEPT_FOR_SESSION,
    DECISION_CANCEL,
    DECISION_DECLINE,
    ERR_APPROVAL_TIMEOUT,
    ERR_CHANNEL_REJECTED,
    ERR_THREAD_NOT_ACTIVE,
    ERR_THREAD_NOT_FOUND,
    ERR_TURN_IN_PROGRESS,
    InitializeResult,
    JsonRpcMessage,
    ServerCapabilities,
    ServerInfo,
    SessionIdentity,
    Thread,
    Turn,
    image_url_part,
    local_image_part,
    text_part,
)
from dotcraft.transport import (
    StdioTransport,
    Transport,
    TransportClosed,
    TransportError,
    WebSocketTransport,
)

# Preserve `dotcraft_wire.<submodule>` import paths and attributes used by existing code.
import sys as _sys  # noqa: E402
from dotcraft import adapter, client, models, transport, turn_reply  # noqa: E402,F401

for _alias, _module in {
    "dotcraft_wire.adapter": adapter,
    "dotcraft_wire.client": client,
    "dotcraft_wire.models": models,
    "dotcraft_wire.transport": transport,
    "dotcraft_wire.turn_reply": turn_reply,
}.items():
    _sys.modules.setdefault(_alias, _module)

__all__ = [
    "DotCraftClient",
    "DotCraftError",
    "ChannelAdapter",
    "Transport",
    "StdioTransport",
    "WebSocketTransport",
    "TransportError",
    "TransportClosed",
    "JsonRpcMessage",
    "SessionIdentity",
    "Thread",
    "Turn",
    "InitializeResult",
    "ServerInfo",
    "ServerCapabilities",
    "text_part",
    "image_url_part",
    "local_image_part",
    "DECISION_ACCEPT",
    "DECISION_ACCEPT_FOR_SESSION",
    "DECISION_ACCEPT_ALWAYS",
    "DECISION_DECLINE",
    "DECISION_CANCEL",
    "ERR_THREAD_NOT_FOUND",
    "ERR_THREAD_NOT_ACTIVE",
    "ERR_TURN_IN_PROGRESS",
    "ERR_APPROVAL_TIMEOUT",
    "ERR_CHANNEL_REJECTED",
]
