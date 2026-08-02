"""
dotcraft — Python SDK for DotCraft.

High-level usage::

    from dotcraft import DotCraft, LocalOptions

    dotcraft = await DotCraft.connect_local(LocalOptions(workspace_path="/path/to/ws"))
    thread = await dotcraft.threads.get_or_create(user_id="me")
    result = await thread.run("Summarize this project.")
    print(result.text)

The low-level wire client, contracts, transports, and channel adapter remain
available for advanced clients through the ``dotcraft`` package.
"""

from .adapter import ChannelAdapter
from .app_binding import (
    APP_BINDING_ERROR_CODES,
    AppBindingHandoff,
    AppBindingManager,
    AppBindingRequestCreateResult,
    AppBindingRequestInfo,
    AppConnectionStartResult,
    AppConnectionConnectResult,
    AppConnectionStatus,
    AppInfo,
    AppSurface,
    ThreadAppBinding,
    app_binding_tool_error,
)
from .client import DotCraftWireClient, DotCraftError, ReconnectQueueFullError, RequestTimeoutError
from .appserver_client import DotCraftAppServerClient
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
    LocalChatOptions,
    LocalOptions,
    McpRuntimeManager,
    RemoteOptions,
    RunResult,
    Thread,
    ThreadManager,
)
from .hub import (
    HubAppServerResponse,
    HubClient,
    HubError,
    HubEvent,
    HubLockInfo,
    HubRuntimeToolsRequest,
    HubStatusResponse,
    default_chat_workspace_path,
    ensure_default_chat_workspace,
)
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
    DynamicToolDeclaration,
    DynamicToolFunction,
    DynamicToolNamespace,
    DynamicToolResult,
    InitializeResult,
    JsonRpcMessage,
    ModelInfo,
    McpServerElicitationRequest,
    McpServerElicitationResponse,
    McpServerOAuthLoginCompletedNotification,
    McpServerOAuthLoginResult,
    McpServerOrigin,
    McpServerReloadResult,
    McpServerResourceReadResult,
    McpServerRuntimeStatus,
    McpServerStartupStatusUpdatedNotification,
    McpServerStatusListResult,
    McpServerToolCallResult,
    ServerCapabilities,
    ServerInfo,
    SessionIdentity,
    command_ref_part,
    dynamic_tool_image,
    dynamic_tool_text,
    file_ref_part,
    image_data_url_part,
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

__version__ = "0.5.0"
sdk_contract_version = "1.0.0"

__all__ = [
    # High-level client
    "DotCraft",
    "Thread",
    "ThreadManager",
    "RunResult",
    "RunEvent",
    "LocalChatOptions",
    "LocalOptions",
    "RemoteOptions",
    "McpRuntimeManager",
    # Wire client
    "DotCraftWireClient",
    "DotCraftAppServerClient",
    "ReconnectQueueFullError",
    "RequestTimeoutError",
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
    "HubAppServerResponse",
    "HubRuntimeToolsRequest",
    "HubStatusResponse",
    "HubEvent",
    "default_chat_workspace_path",
    "ensure_default_chat_workspace",
    # App Binding
    "AppBindingManager",
    "AppBindingHandoff",
    "app_binding_tool_error",
    "APP_BINDING_ERROR_CODES",
    "AppInfo",
    "AppConnectionStatus",
    "AppConnectionStartResult",
    "AppConnectionConnectResult",
    "AppSurface",
    "ThreadAppBinding",
    "AppBindingRequestInfo",
    "AppBindingRequestCreateResult",
    # Channel adapter
    "ChannelAdapter",
    # Models
    "SessionIdentity",
    "ThreadInfo",
    "TurnInfo",
    "ModelInfo",
    "McpServerElicitationRequest",
    "McpServerElicitationResponse",
    "McpServerOAuthLoginCompletedNotification",
    "McpServerOAuthLoginResult",
    "McpServerOrigin",
    "McpServerReloadResult",
    "McpServerResourceReadResult",
    "McpServerRuntimeStatus",
    "McpServerStartupStatusUpdatedNotification",
    "McpServerStatusListResult",
    "McpServerToolCallResult",
    "DynamicToolDeclaration",
    "DynamicToolFunction",
    "DynamicToolNamespace",
    "DynamicToolResult",
    "InitializeResult",
    "ServerInfo",
    "ServerCapabilities",
    # Input part helpers
    "text_part",
    "image_data_url_part",
    "local_image_part",
    "skill_ref_part",
    "command_ref_part",
    "file_ref_part",
    "dynamic_tool_text",
    "dynamic_tool_image",
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
