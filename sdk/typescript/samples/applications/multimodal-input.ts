import { resolve } from "node:path";

import { DotCraft, localImagePart, textPart } from "@dotcraft/sdk";

const [workspaceArgument, imageArgument] = process.argv.slice(2);
if (!workspaceArgument || !imageArgument) {
  throw new Error("Usage: multimodal-input <workspace-path> <image-path>");
}

const workspacePath = resolve(workspaceArgument);
const imagePath = resolve(imageArgument);
const dotcraft = await DotCraft.local({ workspacePath });
try {
  const thread = await dotcraft.threads.start({ userId: "sdk-example" });
  const result = await thread.run([
    textPart("Describe the important information in this image."),
    localImagePart(imagePath),
  ]);
  console.log(result.text);
} finally {
  await dotcraft.close();
}
