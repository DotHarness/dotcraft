"""
dotcraft — Python SDK for DotCraft.

High-level usage::

    from dotcraft import DotCraft, LocalOptions

    dotcraft = await DotCraft.connect_local(LocalOptions(workspace_path="/path/to/ws"))
    thread = await dotcraft.threads.get_or_create(user_id="me")
    result = await thread.run("Summarize this project.")
    print(result.text)

The low-level wire client, transports, and channel adapter remain available for
advanced clients. ``dotcraft_wire`` is a compatibility alias that re-exports the
wire/adapter surface from this package.
"""

from .adapter import ChannelAdapter
from .app_binding import (
    APP_BINDING_ERROR_CODES,
    AppBindingAcceptResult,
    AppBindingAttachToolsResult,
    AppBindingHandoff,
    AppBindingManager,
    AppBindingRequestCreateResult,
    AppBindingRequestInfo,
    AppConnectionStartResult,
    AppConnectionStatus,
    AppInfo,
    AppScopeDescriptor,
    AppToolCatalogEntry,
    ThreadAppBinding,
    app_binding_tool_error,
)
from .client import DotCraftClient, DotCraftError
from .errors import (
    ApprovalTimeoutError,
    InitializationError,
    ThreadNotActiveError,
    ThreadNotFoundError,
    TurnCancelledError,
    TurnFailedError,
    TurnInProgressError,
)
from .events import RunEvent
from .highlevel import (
    DotCraft,
    LocalOptions,
    RemoteOptions,
    RunResult,
    Thread,
    ThreadManager,
)
from .hub import HubClient, HubError, HubLockInfo
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
    ModelInfo,
    ServerCapabilities,
    ServerInfo,
    SessionIdentity,
    command_ref_part,
    file_ref_part,
    image_url_part,
    local_image_part,
    skill_ref_part,
    text_part,
)
from .models import Thread as ThreadInfo
from .models import Turn as TurnInfo
from .transport import (
    StdioTransport,
    Transport,
    TransportClosed,
    TransportError,
    WebSocketTransport,
)

__version__ = "0.2.1"
sdk_contract_version = "1.0.0"

__all__ = [
    # High-level client
    "DotCraft",
    "Thread",
    "ThreadManager",
    "RunResult",
    "RunEvent",
    "LocalOptions",
    "RemoteOptions",
    # Wire client
    "DotCraftClient",
    "DotCraftError",
    "JsonRpcMessage",
    # Transports
    "Transport",
    "StdioTransport",
    "WebSocketTransport",
    "TransportError",
    "TransportClosed",
    # Hub
    "HubClient",
    "HubLockInfo",
    "HubError",
    # App Binding
    "AppBindingManager",
    "AppBindingHandoff",
    "app_binding_tool_error",
    "APP_BINDING_ERROR_CODES",
    "AppInfo",
    "AppScopeDescriptor",
    "AppToolCatalogEntry",
    "AppConnectionStatus",
    "AppConnectionStartResult",
    "ThreadAppBinding",
    "AppBindingRequestInfo",
    "AppBindingRequestCreateResult",
    "AppBindingAcceptResult",
    "AppBindingAttachToolsResult",
    # Channel adapter
    "ChannelAdapter",
    # Models
    "SessionIdentity",
    "ThreadInfo",
    "TurnInfo",
    "ModelInfo",
    "InitializeResult",
    "ServerInfo",
    "ServerCapabilities",
    # Input part helpers
    "text_part",
    "image_url_part",
    "local_image_part",
    "skill_ref_part",
    "command_ref_part",
    "file_ref_part",
    # Approval decisions
    "DECISION_ACCEPT",
    "DECISION_ACCEPT_FOR_SESSION",
    "DECISION_ACCEPT_ALWAYS",
    "DECISION_DECLINE",
    "DECISION_CANCEL",
    # Typed errors
    "InitializationError",
    "TurnInProgressError",
    "ThreadNotFoundError",
    "ThreadNotActiveError",
    "TurnFailedError",
    "TurnCancelledError",
    "ApprovalTimeoutError",
    # Error codes
    "ERR_THREAD_NOT_FOUND",
    "ERR_THREAD_NOT_ACTIVE",
    "ERR_TURN_IN_PROGRESS",
    "ERR_APPROVAL_TIMEOUT",
    "ERR_CHANNEL_REJECTED",
    # Version
    "__version__",
    "sdk_contract_version",
]
