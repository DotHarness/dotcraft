import {
  DECISION_DECLINE,
  DotCraft,
  type DotCraftLocalOptions,
  type DotCraftRemoteOptions,
} from "@dotcraft/sdk";

type ConnectionMode = "local" | "remote";

function printUsage(): void {
  console.log(`Usage:
  npm run example -- local <workspace-path>
  npm run example -- remote <websocket-url>

Set DOTCRAFT_TOKEN when the remote AppServer requires authentication.`);
}

function parseTarget(): { mode: ConnectionMode; target: string } | null {
  const [mode, target] = process.argv.slice(2);
  if (mode === "--help" || mode === "-h") {
    printUsage();
    return null;
  }
  if ((mode !== "local" && mode !== "remote") || !target) {
    printUsage();
    process.exitCode = 2;
    return null;
  }
  return { mode, target };
}

const callbacks: Pick<
  DotCraftLocalOptions,
  "clientName" | "approvalHandler" | "userInputHandler"
> = {
  clientName: "dotcraft-sdk-example",
  approvalHandler(request: Record<string, unknown>) {
    console.error("Approval requested; declining in this safety-first example:", request);
    return DECISION_DECLINE;
  },
  userInputHandler(request: Record<string, unknown>) {
    console.error("User input requested; returning no answers in this non-interactive example:", request);
    return { answers: {} };
  },
};

async function connect(mode: ConnectionMode, target: string): Promise<DotCraft> {
  if (mode === "local") {
    const options: DotCraftLocalOptions = {
      ...callbacks,
      workspacePath: target,
    };
    return await DotCraft.local(options);
  }

  const options: DotCraftRemoteOptions = {
    ...callbacks,
    url: target,
    token: process.env.DOTCRAFT_TOKEN,
  };
  return await DotCraft.remote(options);
}

async function main(): Promise<void> {
  const connection = parseTarget();
  if (!connection) return;

  const { mode, target } = connection;
  const dotcraft = await connect(mode, target);

  try {
    const thread = await dotcraft.threads.start({
      userId: "sdk-example",
      dynamicTools: [
        {
          type: "namespace",
          name: "example",
          description: "Small tools owned by the TypeScript SDK example.",
          tools: [
            {
              type: "function",
              name: "Echo",
              description: "Echo a short text value.",
              inputSchema: {
                type: "object",
                properties: { text: { type: "string" } },
                required: ["text"],
                additionalProperties: false,
              },
              handler(request) {
                const text = request.arguments.text;
                if (typeof text !== "string") {
                  return {
                    success: false,
                    errorCode: "InvalidArguments",
                    errorMessage: "text must be a string",
                  };
                }
                return {
                  success: true,
                  contentItems: [{ type: "text", text }],
                  structuredContent: { echoed: text },
                };
              },
            },
          ],
        },
      ],
    });

    for await (const event of thread.runStreamed(
      "Call example.Echo with a short greeting, then report the returned value.",
    )) {
      if (event.type === "agent_message_delta") {
        process.stdout.write(event.delta ?? "");
      } else if (event.type === "completed") {
        if (event.result?.text) console.log(`\n\nFinal response: ${event.result.text}`);
      } else if (event.type === "failed") {
        console.error("Turn failed:", event.error ?? "unknown error");
      } else if (event.type === "cancelled") {
        console.error("Turn was cancelled.");
      }
    }
  } finally {
    await dotcraft.close();
  }
}

await main();
