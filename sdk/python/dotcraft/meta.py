"""SDK and generated protocol metadata."""

from importlib.metadata import PackageNotFoundError, version

from ._generated.appserver import (
    APPSERVER_PROTOCOL_VERSION,
    CONTRACT_SHA256,
    CONTRACT_VERSION,
)

try:
    SDK_VERSION = version("dotcraft")
except PackageNotFoundError:
    SDK_VERSION = "0+unknown"

__all__ = [
    "SDK_VERSION",
    "CONTRACT_VERSION",
    "APPSERVER_PROTOCOL_VERSION",
    "CONTRACT_SHA256",
]
