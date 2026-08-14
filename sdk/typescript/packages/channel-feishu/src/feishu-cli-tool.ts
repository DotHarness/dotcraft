import type { ChannelToolDescriptor } from "@dotcraft/channel";

import {
  FeishuCliRunner,
  FeishuCliRunnerError,
  logFeishuCliFailure,
} from "./feishu-cli-runner.js";
import type { FeishuConfig } from "./feishu-types.js";

export function getFeishuCliToolDescriptors(enabled: boolean): ChannelToolDescriptor[] {
  if (!enabled) return [];
  return [{
    name: "FeishuCli",
    description:
      "Run one command from the pinned official Feishu CLI as the configured Bot. "
      + "Before business commands, use command='skills' with args=['list'] and then "
      + "args=['read', '<skill-name>'] to load the relevant official Skill. Every call requires approval.",
    requiresChatContext: false,
    approval: {
      kind: "remoteResource",
      targetArgument: "command",
      operation: "invoke",
    },
    display: {
      icon: "\u{1F6E0}\u{FE0F}",
      title: "Feishu CLI",
    },
    inputSchema: {
      type: "object",
      properties: {
        command: {
          type: "string",
          description: "One lark-cli subcommand token, such as skills, docs, wiki, or calendar.",
        },
        args: {
          type: "array",
          items: { type: "string" },
          description: "Argument tokens following command. Do not include credentials, identity flags, or --yes.",
        },
      },
      required: ["command", "args"],
      additionalProperties: false,
    },
  }];
}

export class FeishuCliTool {
  private readonly abortController = new AbortController();

  private constructor(private readonly state:
    | { runner: FeishuCliRunner }
    | { error: FeishuCliRunnerError }) {}

  static async create(
    workspaceRoot: string,
    config: FeishuConfig["feishu"],
  ): Promise<FeishuCliTool> {
    try {
      return new FeishuCliTool({ runner: await FeishuCliRunner.fromModule(workspaceRoot, config) });
    } catch (error) {
      const failure = error instanceof FeishuCliRunnerError
        ? error
        : new FeishuCliRunnerError(
          "FeishuCliArtifactUnavailable",
          "The bundled Feishu CLI is unavailable.",
        );
      logFeishuCliFailure(failure);
      return new FeishuCliTool({ error: failure });
    }
  }

  stop(): void {
    this.abortController.abort();
  }

  async invoke(args: Record<string, unknown>): Promise<Record<string, unknown>> {
    if ("error" in this.state) return failureResult(this.state.error);

    const command = typeof args.command === "string" ? args.command : "";
    const cliArgs = Array.isArray(args.args) ? args.args : [];
    try {
      const result = await this.state.runner.run(command, cliArgs as string[], this.abortController.signal);
      return {
        success: true,
        contentItems: result.contentItems,
        ...(result.structuredContent === undefined
          ? {}
          : { structuredContent: result.structuredContent }),
      };
    } catch (error) {
      logFeishuCliFailure(error);
      return failureResult(error instanceof FeishuCliRunnerError
        ? error
        : new FeishuCliRunnerError("FeishuCliExecutionFailed", "Feishu CLI execution failed."));
    }
  }
}

function failureResult(error: FeishuCliRunnerError): Record<string, unknown> {
  return {
    success: false,
    errorCode: error.code,
    errorMessage: error.message,
  };
}
