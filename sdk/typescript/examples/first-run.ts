import { DotCraft } from "@dotcraft/sdk";

const workspacePath = process.argv[2];
if (!workspacePath) throw new Error("Usage: first-run <workspace-path>");

const dotcraft = await DotCraft.local({ workspacePath });
try {
  const thread = await dotcraft.threads.start({ userId: "sdk-example" });
  const result = await thread.run("Summarize this workspace in three bullets.");
  console.log(result.text);
} finally {
  await dotcraft.close();
}
