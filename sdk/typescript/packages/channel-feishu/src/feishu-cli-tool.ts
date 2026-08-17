import type { ChannelToolDescriptor, RuntimeAdditionalContextEntry } from "@dotcraft/channel";

import {
  FeishuCliRunner,
  FeishuCliRunnerError,
  logFeishuCliFailure,
} from "./feishu-cli-runner.js";
import type { FeishuConfig } from "./feishu-types.js";

const FEISHU_CLI_RUNTIME_CONTEXT: Record<string, RuntimeAdditionalContextEntry> = {
  "feishu.cli": {
    kind: "application",
    value:
      "FeishuCli uses the bundled official CLI with adapter-managed Bot credentials. "
      + "Read a known Skill directly; use skills list only when the relevant Skill is unknown. "
      + "When a Skill links a reference, read it first with args=['read','<skill-name>','<relative-path>']; do not guess parameters. "
      + "This Channel is Bot-only and overrides upstream recommendations to use user identity: omit --as or use --as bot, and do not try user OAuth, auth, config, or profiles. "
      + "Call FeishuCli directly instead of locating lark-cli through Shell. Do not pass --yes. Document, wiki, file, media, and page tokens are valid resource identifiers.",
  },
};

export function getFeishuCliRuntimeAdditionalContext(
  enabled: boolean,
): Record<string, RuntimeAdditionalContextEntry> | undefined {
  return enabled ? FEISHU_CLI_RUNTIME_CONTEXT : undefined;
}

export function getFeishuCliToolDescriptors(enabled: boolean): ChannelToolDescriptor[] {
  if (!enabled) return [];
  return [{
    name: "FeishuCli",
    description:
      "Run one command from the pinned official Feishu CLI with adapter-managed credentials. "
      + "Pass the subcommand in command and each following argv token in args. "
      + "Use the Feishu Channel context for the Skill workflow and Bot-only policy. Every call requires approval.",
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
          description: "Argument tokens following command. Do not include --profile or --yes. --as bot and business resource tokens are allowed.",
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
    getTenantAccessToken: () => Promise<string>,
  ): Promise<FeishuCliTool> {
    try {
      return new FeishuCliTool({
        runner: await FeishuCliRunner.fromModule(workspaceRoot, config, getTenantAccessToken),
      });
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
        ...(result.structuredResult === undefined
          ? {}
          : { structuredResult: result.structuredResult }),
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
    ...(error.structuredResult === undefined
      ? {}
      : { structuredResult: error.structuredResult }),
  };
}
