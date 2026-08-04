"""Send text and a local image as structured input parts."""

import argparse
import asyncio
from pathlib import Path

from dotcraft import DotCraft, LocalOptions, local_image_part, text_part


async def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workspace")
    parser.add_argument("image")
    args = parser.parse_args()

    workspace_path = str(Path(args.workspace).resolve())
    image_path = str(Path(args.image).resolve())
    async with await DotCraft.connect_local(LocalOptions(workspace_path=workspace_path)) as dotcraft:
        thread = await dotcraft.threads.start(user_id="sdk-example")
        result = await thread.run([
            text_part("Describe the important information in this image."),
            local_image_part(image_path),
        ])
        print(result.text)


if __name__ == "__main__":
    asyncio.run(main())
