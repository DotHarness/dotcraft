import { DotCraft } from "@dotcraft/sdk";

const workspacePath = process.argv[2];
if (!workspacePath) throw new Error("Usage: models-and-mcp <workspace-path>");

const dotcraft = await DotCraft.local({ workspacePath });
try {
  const models = await dotcraft.models.list();
  console.log("Models:");
  for (const model of models) console.log(`- ${model.id ?? "(unnamed)"}`);

  const thread = await dotcraft.threads.start({ userId: "sdk-example" });
  const status = await dotcraft.mcpRuntime.listStatus({ threadId: thread.id });
  console.log("MCP servers:");
  for (const server of status.data ?? []) {
    console.log(`- ${server.name ?? "(unnamed)"}: ${server.startupState ?? "unknown"}`);
  }
} finally {
  await dotcraft.close();
}
