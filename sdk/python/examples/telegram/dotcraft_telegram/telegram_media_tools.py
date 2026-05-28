"""
Telegram media helpers for the DotCraft Telegram adapter.

Keeps Telegram Bot API-specific source parsing, validation, and send logic out of
bot.py so channel orchestration remains easy to read.
"""

from __future__ import annotations

import base64
import io
from dataclasses import dataclass
from pathlib import Path
from typing import Any


DOCUMENT_TOOL_NAME = "TelegramSendDocumentToCurrentChat"
VOICE_TOOL_NAME = "TelegramSendVoiceToCurrentChat"

_DOCUMENT_URL_EXTENSIONS = {".pdf", ".zip"}
_VOICE_EXTENSIONS = {".ogg", ".oga"}


class TelegramMediaError(Exception):
    """Raised when Telegram media arguments or sources are invalid."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


@dataclass(slots=True)
class TelegramPreparedMedia:
    """Prepared Telegram media payload ready for bot.send_* methods."""

    source_kind: str
    media: Any
    file_name: str | None = None


class TelegramMediaTools:
    """Encapsulates Telegram Bot API media tooling and structured send behavior."""

    def get_delivery_capabilities(self) -> dict:
        return {
            "structuredDelivery": True,
            "media": {
                "file": {
                    "supportsHostPath": True,
                    "supportsUrl": True,
                    "supportsBase64": True,
                    "supportsCaption": True,
                },
                "audio": {
                    "supportsHostPath": True,
                    "supportsUrl": True,
                    "supportsBase64": True,
                    "supportsCaption": True,
                },
            },
        }

    def get_channel_tools(self) -> list[dict]:
        source_properties = {
            "filePath": {"type": "string"},
            "fileUrl": {"type": "string"},
            "fileBase64": {"type": "string"},
            "telegramFileId": {"type": "string"},
        }

        return [
            {
                "name": DOCUMENT_TOOL_NAME,
                "description": "Send a document to the current Telegram chat using the official sendDocument API.",
                "requiresChatContext": True,
                "display": {
                    "icon": "📎",
                    "title": "Send document to current Telegram chat",
                },
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        **source_properties,
                        "fileName": {"type": "string"},
                        "caption": {"type": "string"},
                        "disableContentTypeDetection": {"type": "boolean"},
                    },
                },
            },
            {
                "name": VOICE_TOOL_NAME,
                "description": "Send a voice note to the current Telegram chat using the official sendVoice API.",
                "requiresChatContext": True,
                "display": {
                    "icon": "🎤",
                    "title": "Send voice to current Telegram chat",
                },
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        **source_properties,
                        "fileName": {"type": "string"},
                        "caption": {"type": "string"},
                        "duration": {"type": "integer"},
                    },
                },
            },
        ]

    async def send_structured_message(
        self,
        bot: Any,
        chat_id: int,
        message: dict,
        metadata: dict | None = None,
    ) -> dict:
        kind = str(message.get("kind", ""))
        if kind == "file":
            source = message.get("source") or {}
            caption = _optional_text(message.get("caption"))
            file_name = _optional_text(message.get("fileName")) or "attachment"
            disable_detection = bool((metadata or {}).get("disableContentTypeDetection", False))
            remote_message, prepared = await self._send_document(
                bot=bot,
                chat_id=chat_id,
                prepared=self._prepare_adapter_media(
                    source=source,
                    default_file_name=file_name,
                    expected="document",
                ),
                caption=caption,
                disable_content_type_detection=disable_detection,
            )
            return {
                "delivered": True,
                "remoteMessageId": str(getattr(remote_message, "message_id", "")),
                "remoteMediaId": self._extract_remote_media_id(remote_message, "document"),
                "effectiveSourceKind": prepared.source_kind,
                "fileName": prepared.file_name,
            }

        if kind == "audio":
            source = message.get("source") or {}
            caption = _optional_text(message.get("caption"))
            file_name = _optional_text(message.get("fileName")) or "voice.ogg"
            duration = _optional_int(message.get("duration"))
            remote_message, prepared = await self._send_voice(
                bot=bot,
                chat_id=chat_id,
                prepared=self._prepare_adapter_media(
                    source=source,
                    default_file_name=file_name,
                    expected="voice",
                ),
                caption=caption,
                duration=duration,
            )
            return {
                "delivered": True,
                "remoteMessageId": str(getattr(remote_message, "message_id", "")),
                "remoteMediaId": self._extract_remote_media_id(remote_message, "voice"),
                "effectiveSourceKind": prepared.source_kind,
                "fileName": prepared.file_name,
            }

        raise TelegramMediaError(
            "UnsupportedDeliveryKind",
            f"Telegram example does not implement structured '{kind}' delivery.",
        )

    async def execute_tool_call(self, bot: Any, tool_name: str, chat_id: int, args: dict) -> dict:
        if tool_name == DOCUMENT_TOOL_NAME:
            caption = _optional_text(args.get("caption"))
            file_name = _optional_text(args.get("fileName")) or "attachment"
            disable_detection = bool(args.get("disableContentTypeDetection", False))
            remote_message, prepared = await self._send_document(
                bot=bot,
                chat_id=chat_id,
                prepared=self._prepare_tool_media(
                    args=args,
                    default_file_name=file_name,
                    expected="document",
                ),
                caption=caption,
                disable_content_type_detection=disable_detection,
            )
            return self._tool_success_result(
                noun="document",
                message_id=str(getattr(remote_message, "message_id", "")),
                media_id=self._extract_remote_media_id(remote_message, "document"),
                prepared=prepared,
            )

        if tool_name == VOICE_TOOL_NAME:
            caption = _optional_text(args.get("caption"))
            file_name = _optional_text(args.get("fileName")) or "voice.ogg"
            duration = _optional_int(args.get("duration"))
            remote_message, prepared = await self._send_voice(
                bot=bot,
                chat_id=chat_id,
                prepared=self._prepare_tool_media(
                    args=args,
                    default_file_name=file_name,
                    expected="voice",
                ),
                caption=caption,
                duration=duration,
            )
            return self._tool_success_result(
                noun="voice note",
                message_id=str(getattr(remote_message, "message_id", "")),
                media_id=self._extract_remote_media_id(remote_message, "voice"),
                prepared=prepared,
            )

        raise TelegramMediaError("UnsupportedTool", f"Unknown tool '{tool_name}'.")

    async def _send_document(
        self,
        bot: Any,
        chat_id: int,
        prepared: TelegramPreparedMedia,
        caption: str | None,
        disable_content_type_detection: bool,
    ) -> tuple[Any, TelegramPreparedMedia]:
        kwargs: dict[str, Any] = {
            "chat_id": chat_id,
            "document": prepared.media,
            "caption": caption,
        }
        if prepared.file_name:
            kwargs["filename"] = prepared.file_name
        if disable_content_type_detection:
            kwargs["disable_content_type_detection"] = True
        try:
            return await bot.send_document(**kwargs), prepared
        finally:
            self._close_media(prepared.media)

    async def _send_voice(
        self,
        bot: Any,
        chat_id: int,
        prepared: TelegramPreparedMedia,
        caption: str | None,
        duration: int | None,
    ) -> tuple[Any, TelegramPreparedMedia]:
        kwargs: dict[str, Any] = {
            "chat_id": chat_id,
            "voice": prepared.media,
            "caption": caption,
        }
        if duration is not None:
            kwargs["duration"] = duration
        try:
            return await bot.send_voice(**kwargs), prepared
        finally:
            self._close_media(prepared.media)

    def _prepare_tool_media(
        self,
        args: dict,
        default_file_name: str,
        expected: str,
    ) -> TelegramPreparedMedia:
        source_values = {
            "filePath": _optional_text(args.get("filePath")),
            "fileUrl": _optional_text(args.get("fileUrl")),
            "fileBase64": _optional_text(args.get("fileBase64")),
            "telegramFileId": _optional_text(args.get("telegramFileId")),
        }
        populated = {key: value for key, value in source_values.items() if value}
        if len(populated) != 1:
            raise TelegramMediaError(
                "InvalidArguments",
                "Exactly one of filePath, fileUrl, fileBase64, or telegramFileId must be provided.",
            )

        source_name, value = next(iter(populated.items()))
        file_name = _optional_text(args.get("fileName")) or default_file_name
        return self._prepare_common_media(source_name, value, file_name, expected)

    def _prepare_adapter_media(
        self,
        source: dict,
        default_file_name: str,
        expected: str,
    ) -> TelegramPreparedMedia:
        source_kind = str(source.get("kind", ""))
        if source_kind == "hostPath":
            return self._prepare_common_media(
                "filePath",
                _required_text(source.get("hostPath"), "hostPath source requires hostPath."),
                default_file_name,
                expected,
            )
        if source_kind == "url":
            return self._prepare_common_media(
                "fileUrl",
                _required_text(source.get("url"), "url source requires url."),
                default_file_name,
                expected,
            )
        if source_kind == "dataBase64":
            return self._prepare_common_media(
                "fileBase64",
                _required_text(source.get("dataBase64"), "dataBase64 source requires dataBase64."),
                default_file_name,
                expected,
            )

        raise TelegramMediaError(
            "UnsupportedMediaSource",
            f"Telegram example cannot send source kind '{source_kind}'.",
        )

    def _prepare_common_media(
        self,
        source_name: str,
        value: str,
        file_name: str,
        expected: str,
    ) -> TelegramPreparedMedia:
        if source_name == "filePath":
            full_path = Path(value).expanduser().resolve()
            if not full_path.exists() or not full_path.is_file():
                raise TelegramMediaError(
                    "InvalidArguments",
                    f"File '{full_path}' does not exist.",
                )
            actual_name = file_name or full_path.name
            self._validate_source(expected, actual_name, str(full_path))
            return TelegramPreparedMedia(
                source_kind="filePath",
                media=open(full_path, "rb"),
                file_name=actual_name,
            )

        if source_name == "fileUrl":
            self._validate_source(expected, file_name, value)
            return TelegramPreparedMedia(
                source_kind="fileUrl",
                media=value,
                file_name=file_name,
            )

        if source_name == "fileBase64":
            try:
                raw = base64.b64decode(value)
            except Exception as exc:  # pragma: no cover - library exception detail
                raise TelegramMediaError(
                    "InvalidArguments",
                    "fileBase64 did not contain valid base64.",
                ) from exc
            self._validate_source(expected, file_name, file_name)
            buffer = io.BytesIO(raw)
            buffer.name = file_name
            return TelegramPreparedMedia(
                source_kind="fileBase64",
                media=buffer,
                file_name=file_name,
            )

        if source_name == "telegramFileId":
            return TelegramPreparedMedia(
                source_kind="telegramFileId",
                media=value,
                file_name=file_name,
            )

        raise TelegramMediaError(
            "InvalidArguments",
            f"Unsupported source field '{source_name}'.",
        )

    def _validate_source(self, expected: str, file_name: str, location_hint: str) -> None:
        file_name_ext = Path((file_name or "").lower()).suffix.lower()
        location_ext = Path(location_hint.lower()).suffix.lower()

        if expected == "document":
            if location_hint.startswith(("http://", "https://")) and location_ext not in _DOCUMENT_URL_EXTENSIONS:
                raise TelegramMediaError(
                    "InvalidArguments",
                    "Telegram sendDocument URL mode currently works reliably for .pdf and .zip files only.",
                )
            return

        if expected == "voice":
            if file_name_ext and file_name_ext not in _VOICE_EXTENSIONS:
                raise TelegramMediaError(
                    "InvalidArguments",
                    "Telegram voice notes should use OGG/Opus (.ogg/.oga). Use document delivery for other audio formats.",
                )
            if location_ext and location_ext not in _VOICE_EXTENSIONS:
                raise TelegramMediaError(
                    "InvalidArguments",
                    "Telegram voice notes should use OGG/Opus (.ogg/.oga). Use document delivery for other audio formats.",
                )
            if location_hint.startswith(("http://", "https://")) and location_ext not in _VOICE_EXTENSIONS:
                raise TelegramMediaError(
                    "InvalidArguments",
                    "Telegram sendVoice URL mode requires an OGG voice source.",
                )

    def _tool_success_result(
        self,
        noun: str,
        message_id: str,
        media_id: str | None,
        prepared: TelegramPreparedMedia,
    ) -> dict:
        return {
            "success": True,
            "contentItems": [
                {
                    "type": "text",
                    "text": f"Sent {noun} '{prepared.file_name or 'attachment'}' to the current Telegram chat.",
                }
            ],
            "structuredResult": {
                "delivered": True,
                "messageId": message_id,
                "mediaId": media_id,
                "effectiveSourceKind": prepared.source_kind,
                "fileName": prepared.file_name,
            },
        }

    @staticmethod
    def _extract_remote_media_id(remote_message: Any, kind: str) -> str | None:
        payload = getattr(remote_message, kind, None)
        if payload is None:
            return None
        return getattr(payload, "file_id", None)

    @staticmethod
    def _close_media(media: Any) -> None:
        close = getattr(media, "close", None)
        if callable(close):
            close()


def _optional_text(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def _required_text(value: Any, message: str) -> str:
    text = _optional_text(value)
    if text is None:
        raise TelegramMediaError("InvalidArguments", message)
    return text


def _optional_int(value: Any) -> int | None:
    if value is None or value == "":
        return None
    return int(value)
