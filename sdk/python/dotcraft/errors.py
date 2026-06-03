"""Typed SDK errors for the high-level DotCraft client.

All errors derive from :class:`~dotcraft.client.DotCraftError`, which carries a
numeric ``code`` and ``message``. Error ``code`` strings/values are stable API.
"""

from __future__ import annotations

from .client import DotCraftError
from .models import (
    ERR_APPROVAL_TIMEOUT,
    ERR_THREAD_NOT_ACTIVE,
    ERR_THREAD_NOT_FOUND,
    ERR_TURN_IN_PROGRESS,
)


class InitializationError(DotCraftError):
    """The AppServer initialize handshake failed."""

    def __init__(self, message: str) -> None:
        super().__init__(-1, message)


class TurnInProgressError(DotCraftError):
    """The server rejected turn start because a turn is already running."""

    def __init__(self, message: str = "A turn is already running on this thread.") -> None:
        super().__init__(ERR_TURN_IN_PROGRESS, message)


class ThreadNotFoundError(DotCraftError):
    """The specified thread does not exist."""

    def __init__(self, message: str = "Thread not found.") -> None:
        super().__init__(ERR_THREAD_NOT_FOUND, message)


class ThreadNotActiveError(DotCraftError):
    """The thread cannot accept turns because it is paused or archived."""

    def __init__(self, message: str = "Thread is not active.") -> None:
        super().__init__(ERR_THREAD_NOT_ACTIVE, message)


class TurnFailedError(DotCraftError):
    """Agent execution failed after turn/start succeeded."""

    def __init__(self, message: str, thread_id: str, turn_id: str | None = None) -> None:
        super().__init__(-1, message)
        self.thread_id = thread_id
        self.turn_id = turn_id


class TurnCancelledError(DotCraftError):
    """The turn was cancelled before completing successfully."""

    def __init__(self, thread_id: str, turn_id: str | None = None, reason: str | None = None) -> None:
        super().__init__(-1, reason or "The turn was cancelled.")
        self.thread_id = thread_id
        self.turn_id = turn_id


class ApprovalTimeoutError(DotCraftError):
    """The client did not answer an approval request in time."""

    def __init__(self, message: str = "Approval timed out.") -> None:
        super().__init__(ERR_APPROVAL_TIMEOUT, message)


def error_for_code(code: int, message: str, data=None) -> DotCraftError:
    """Map a JSON-RPC error code to the most specific typed error."""
    if code == ERR_TURN_IN_PROGRESS:
        return TurnInProgressError(message)
    if code == ERR_THREAD_NOT_FOUND:
        return ThreadNotFoundError(message)
    if code == ERR_THREAD_NOT_ACTIVE:
        return ThreadNotActiveError(message)
    if code == ERR_APPROVAL_TIMEOUT:
        return ApprovalTimeoutError(message)
    return DotCraftError(code, message, data)
