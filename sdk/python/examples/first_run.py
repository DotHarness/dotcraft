"""Connect locally, run one turn, and close the SDK connection."""

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
        thread = await dotcraft.threads.start(user_id="sdk-example")
        result = await thread.run("Summarize this workspace in three bullets.")
        print(result.text)


if __name__ == "__main__":
    asyncio.run(main())
