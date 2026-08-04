"""Resume a thread by ID and continue its conversation."""

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
        started = await dotcraft.threads.start(user_id="sdk-example")
        await started.run("Remember that the release color is indigo.")

        resumed = await dotcraft.threads.resume(started.id)
        result = await resumed.run("What is the release color?")
        print(f"Thread {resumed.id}: {result.text}")


if __name__ == "__main__":
    asyncio.run(main())
