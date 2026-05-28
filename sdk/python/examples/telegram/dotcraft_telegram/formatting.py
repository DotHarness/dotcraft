"""Telegram message formatting utilities."""

from __future__ import annotations

import re


def markdown_to_telegram_html(text: str) -> str:
    """
    Convert Markdown to Telegram-safe HTML.

    Telegram's HTML parse mode supports: <b>, <i>, <s>, <u>, <code>,
    <pre>, <a href="...">, and basic entity escaping (&amp; &lt; &gt;).

    Strategy:
    1. Extract and protect code blocks / inline code (preserve from escaping).
    2. Convert structural elements (headers, blockquotes, lists).
    3. HTML-escape the remaining text.
    4. Apply inline formatting (bold, italic, strikethrough, links).
    5. Restore protected code blocks.
    """
    if not text:
        return ""

    # --- Step 1: protect code blocks ---
    code_blocks: list[str] = []

    def save_code_block(m: re.Match) -> str:
        code_blocks.append(m.group(1))
        return f"\x00CB{len(code_blocks) - 1}\x00"

    text = re.sub(r"```[\w]*\n?([\s\S]*?)```", save_code_block, text)

    # --- Step 2: protect inline code ---
    inline_codes: list[str] = []

    def save_inline_code(m: re.Match) -> str:
        inline_codes.append(m.group(1))
        return f"\x00IC{len(inline_codes) - 1}\x00"

    text = re.sub(r"`([^`]+)`", save_inline_code, text)

    # --- Step 3: strip header markers (keep text) ---
    text = re.sub(r"^#{1,6}\s+(.+)$", r"\1", text, flags=re.MULTILINE)

    # --- Step 4: strip blockquote markers ---
    text = re.sub(r"^>\s*(.*)$", r"\1", text, flags=re.MULTILINE)

    # --- Step 5: HTML-escape ---
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

    # --- Step 6: links [text](url) ---
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)

    # --- Step 7: bold **text** or __text__ ---
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)
    text = re.sub(r"__(.+?)__", r"<b>\1</b>", text)

    # --- Step 8: italic _text_ (avoid matching inside snake_case words) ---
    text = re.sub(r"(?<![a-zA-Z0-9])_([^_]+)_(?![a-zA-Z0-9])", r"<i>\1</i>", text)

    # --- Step 9: strikethrough ~~text~~ ---
    text = re.sub(r"~~(.+?)~~", r"<s>\1</s>", text)

    # --- Step 10: bullet lists ---
    text = re.sub(r"^[-*]\s+", "• ", text, flags=re.MULTILINE)

    # --- Step 11: restore inline code ---
    for i, code in enumerate(inline_codes):
        escaped = code.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        text = text.replace(f"\x00IC{i}\x00", f"<code>{escaped}</code>")

    # --- Step 12: restore code blocks ---
    for i, code in enumerate(code_blocks):
        escaped = code.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        text = text.replace(f"\x00CB{i}\x00", f"<pre><code>{escaped}</code></pre>")

    return text


def split_message(content: str, max_len: int = 4000) -> list[str]:
    """
    Split a long message into chunks that fit within Telegram's message limit.

    Prefers splitting at paragraph breaks, then line breaks, then word
    boundaries, and falls back to hard-cutting at max_len characters.
    """
    if len(content) <= max_len:
        return [content]

    chunks: list[str] = []
    while content:
        if len(content) <= max_len:
            chunks.append(content)
            break
        cut = content[:max_len]
        # Prefer paragraph break
        pos = cut.rfind("\n\n")
        if pos == -1:
            pos = cut.rfind("\n")
        if pos == -1:
            pos = cut.rfind(" ")
        if pos == -1:
            pos = max_len
        chunks.append(content[:pos].rstrip())
        content = content[pos:].lstrip()

    return chunks
