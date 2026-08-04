import { DotCraft } from "@dotcraft/sdk";

const workspacePath = process.argv[2];
if (!workspacePath) throw new Error("Usage: continue-thread <workspace-path>");

const dotcraft = await DotCraft.local({ workspacePath });
try {
  const started = await dotcraft.threads.start({ userId: "sdk-example" });
  await started.run("Remember that the release color is indigo.");

  const resumed = await dotcraft.threads.resume(started.id);
  const result = await resumed.run("What is the release color?");
  console.log(`Thread ${resumed.id}: ${result.text}`);
} finally {
  await dotcraft.close();
}
