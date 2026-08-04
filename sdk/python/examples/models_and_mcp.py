"""List models and inspect MCP runtime status for one thread."""

import argparse
import asyncio
from pathlib import Path

from dotcraft import DotCraft, LocalOptions


async def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workspace")
    args = parser.parse_args()

    options = LocalOptions(workspace_path=str(Path(args.workspace).resolve()))
    async with await DotCraft.connect_local(options) as dotcraft:
        print("Models:")
        for model in await dotcraft.models.list():
            print(f"- {model.id or '(unnamed)'}")

        thread = await dotcraft.threads.start(user_id="sdk-example")
        status = await dotcraft.mcp_runtime.list_status(thread_id=thread.id)
        print("MCP servers:")
        for server in status.data or []:
            print(f"- {server.name or '(unnamed)'}: {server.startup_state or 'unknown'}")


if __name__ == "__main__":
    asyncio.run(main())
