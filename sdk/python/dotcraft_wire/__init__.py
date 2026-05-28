"""
dotcraft_wire — Python SDK for the DotCraft AppServer Wire Protocol.

Public API::

    from dotcraft_wire import DotCraftClient, StdioTransport, WebSocketTransport, ChannelAdapter
"""

from .adapter import ChannelAdapter
from .client import DotCraftClient, DotCraftError
from .models import (
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
from .transport import (
    StdioTransport,
    Transport,
    TransportClosed,
    TransportError,
    WebSocketTransport,
)

__version__ = "0.1.0"

__all__ = [
    # Core classes
    "DotCraftClient",
    "DotCraftError",
    "ChannelAdapter",
    # Transports
    "Transport",
    "StdioTransport",
    "WebSocketTransport",
    "TransportError",
    "TransportClosed",
    # Models
    "JsonRpcMessage",
    "SessionIdentity",
    "Thread",
    "Turn",
    "InitializeResult",
    "ServerInfo",
    "ServerCapabilities",
    # Input part helpers
    "text_part",
    "image_url_part",
    "local_image_part",
    # Approval decisions
    "DECISION_ACCEPT",
    "DECISION_ACCEPT_FOR_SESSION",
    "DECISION_ACCEPT_ALWAYS",
    "DECISION_DECLINE",
    "DECISION_CANCEL",
    # Error codes
    "ERR_THREAD_NOT_FOUND",
    "ERR_THREAD_NOT_ACTIVE",
    "ERR_TURN_IN_PROGRESS",
    "ERR_APPROVAL_TIMEOUT",
    "ERR_CHANNEL_REJECTED",
]
