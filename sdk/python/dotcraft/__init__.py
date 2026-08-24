"""Curated high-level DotCraft Python SDK API."""

from importlib.metadata import PackageNotFoundError, version

from .errors import (
    ApprovalTimeoutError,
    DotCraftError,
    InitializationError,
    ProtocolViolationError,
    RunDisconnectedError,
    ThreadNotActiveError,
    ThreadNotFoundError,
    TurnCancelledError,
    TurnFailedError,
    TurnInProgressError,
)
from .events import RunEvent
from .highlevel import (
    DotCraft,
    DotCraftThread,
    LocalChatOptions,
    LocalOptions,
    RemoteOptions,
    RunResult,
    ThreadManager,
)
from .models import (
    DECISION_ACCEPT,
    DECISION_ACCEPT_ALWAYS,
    DECISION_ACCEPT_FOR_SESSION,
    DECISION_CANCEL,
    DECISION_DECLINE,
    command_ref_part,
    file_ref_part,
    image_data_url_part,
    local_image_part,
    skill_ref_part,
    text_part,
)

try:
    __version__ = version("dotcraft")
except PackageNotFoundError:
    __version__ = "0.5.9"

__all__ = [
    "DotCraft",
    "DotCraftThread",
    "ThreadManager",
    "RunResult",
    "RunEvent",
    "LocalChatOptions",
    "LocalOptions",
    "RemoteOptions",
    "DotCraftError",
    "InitializationError",
    "ProtocolViolationError",
    "TurnInProgressError",
    "ThreadNotFoundError",
    "ThreadNotActiveError",
    "TurnFailedError",
    "TurnCancelledError",
    "RunDisconnectedError",
    "ApprovalTimeoutError",
    "text_part",
    "image_data_url_part",
    "local_image_part",
    "skill_ref_part",
    "command_ref_part",
    "file_ref_part",
    "DECISION_ACCEPT",
    "DECISION_ACCEPT_FOR_SESSION",
    "DECISION_ACCEPT_ALWAYS",
    "DECISION_DECLINE",
    "DECISION_CANCEL",
    "__version__",
]
