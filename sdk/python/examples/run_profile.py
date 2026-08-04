"""Minimal DotCraft Run-profile example for local and remote AppServer connections."""

from __future__ import annotations

import argparse
import asyncio
import os
from pathlib import Path
from typing import Any

from dotcraft import (
    DECISION_ACCEPT,
    DECISION_DECLINE,
    DotCraft,
    LocalOptions,
    RemoteOptions,
)
from dotcraft.dynamic_tools import (
    DynamicToolFunction,
    DynamicToolNamespace,
    DynamicToolResult,
    dynamic_tool_text,
)


def read_input(prompt: str) -> str:
    try:
        return input(prompt)
    except EOFError:
        return ""


async def approval_handler(request: dict[str, Any]) -> str:
    """Ask before allowing an approval-gated operation; decline by default."""
    operation = request.get("operation", "operation")
    target = request.get("target", "unknown target")
    answer = await asyncio.to_thread(
        read_input,
        f"\nApprove {operation} on {target}? [y/N] ",
    )
    return (
        DECISION_ACCEPT if answer.strip().lower() in {"y", "yes"} else DECISION_DECLINE
    )


async def user_input_handler(request: dict[str, Any]) -> dict[str, Any]:
    """Collect free-form answers for model-initiated user-input requests."""
    answers: dict[str, Any] = {}
    for question in request.get("questions", []):
        if not isinstance(question, dict):
            continue
        question_id = question.get("id")
        prompt = question.get("question")
        if not isinstance(question_id, str) or not isinstance(prompt, str):
            continue

        options = question.get("options", [])
        labels = [
            option["label"]
            for option in options
            if isinstance(option, dict) and isinstance(option.get("label"), str)
        ]
        suffix = f" ({' / '.join(labels)})" if labels else ""
        answer = await asyncio.to_thread(read_input, f"\n{prompt}{suffix}: ")
        if answer:
            answers[question_id] = {"answers": [answer]}
    return answers


async def greet_tool(call: dict[str, Any]) -> dict[str, Any]:
    """Handle calls to the demo/Greet runtime dynamic tool."""
    arguments = call.get("arguments", {})
    name = arguments.get("name", "world") if isinstance(arguments, dict) else "world"
    message = f"Hello, {name}!"
    return DynamicToolResult(
        success=True,
        content_items=[dynamic_tool_text(message)],
        structured_content={"greeting": message},
    ).to_wire()


def dynamic_tools() -> list[dict[str, Any]]:
    namespace = DynamicToolNamespace(
        name="demo",
        description="Small tools implemented by this Python client.",
        tools=[
            DynamicToolFunction(
                name="Greet",
                description="Return a friendly greeting for a name.",
                input_schema={
                    "type": "object",
                    "properties": {"name": {"type": "string"}},
                    "required": ["name"],
                    "additionalProperties": False,
                },
            )
        ],
    )
    return [namespace.to_wire()]


async def connect(args: argparse.Namespace) -> DotCraft:
    if args.remote:
        return await DotCraft.connect_remote(
            RemoteOptions(
                url=args.remote,
                token=os.environ.get("DOTCRAFT_APPSERVER_TOKEN"),
                approval_handler=approval_handler,
                user_input_handler=user_input_handler,
            )
        )

    return await DotCraft.connect_local(
        LocalOptions(
            workspace_path=str(Path(args.workspace).resolve()),
            approval_handler=approval_handler,
            user_input_handler=user_input_handler,
        )
    )


async def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "prompt",
        nargs="?",
        default="Call demo/Greet with the name 'SDK user', then report the result.",
    )
    parser.add_argument(
        "--remote",
        metavar="WS_URL",
        help="connect to a remote AppServer instead of starting or reusing a local one",
    )
    parser.add_argument(
        "--workspace",
        default=".",
        help="local workspace path, or the server-side workspace path with --remote",
    )
    args = parser.parse_args()

    workspace = args.workspace if args.remote else str(Path(args.workspace).resolve())
    async with await connect(args) as dotcraft:
        thread = await dotcraft.threads.start(
            workspace_path=workspace,
            dynamic_tools=dynamic_tools(),
        )
        thread.on_tool_call("demo", "Greet", greet_tool)

        async for event in thread.run_streamed(args.prompt):
            if event.type == "agent_message_delta":
                delta = event.params.get("delta")
                if isinstance(delta, str):
                    print(delta, end="", flush=True)
        print()


if __name__ == "__main__":
    asyncio.run(main())
