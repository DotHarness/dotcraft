import type { FeishuClient } from "./feishu-client.js";
import type { FeishuWikiObjType } from "./feishu-types.js";

export const LIST_WIKI_NODES_TOOL_NAME = "FeishuListWikiNodes";
export const GET_WIKI_NODE_INFO_TOOL_NAME = "FeishuGetWikiNodeInfo";
export const MOVE_DOCX_TO_WIKI_TOOL_NAME = "FeishuMoveDocxToWiki";
export const MOVE_WIKI_NODE_TOOL_NAME = "FeishuMoveWikiNode";
export const LIST_WIKI_SPACES_TOOL_NAME = "FeishuListWikiSpaces";
export const GET_WIKI_SPACE_TOOL_NAME = "FeishuGetWikiSpace";
export const CREATE_WIKI_NODE_TOOL_NAME = "FeishuCreateWikiNode";
export const RENAME_WIKI_NODE_TOOL_NAME = "FeishuRenameWikiNode";

const WIKI_NODE_TOKEN_PATTERN = /^[A-Za-z0-9]{16,40}$/;
const WIKI_SPACE_ID_PATTERN = /^\d{8,30}$/;
const DOCX_ID_PATTERN = /^[A-Za-z0-9]{16,40}$/;

const WIKI_OBJ_TYPES: readonly FeishuWikiObjType[] = [
  "wiki",
  "doc",
  "docx",
  "sheet",
  "bitable",
  "mindnote",
  "file",
  "slides",
];
const CREATE_WIKI_OBJ_TYPES: readonly FeishuWikiObjType[] = [
  "docx",
  "sheet",
  "bitable",
  "mindnote",
  "slides",
  "file",
];

const WIKI_MOVE_POLL_MAX_ATTEMPTS = 30;
const WIKI_MOVE_POLL_INTERVAL_MS = 2000;
const WIKI_MOVE_STATUS_PROCESSING = 1;
const WIKI_MOVE_STATUS_SUCCESS = 2;
const WIKI_MOVE_STATUS_FAILED = 3;

export interface WikiMovePollOverride {
  maxAttempts?: number;
  intervalMs?: number;
  sleep?: (ms: number) => Promise<void>;
}

let wikiMovePollOverride: WikiMovePollOverride | undefined;

export function __setWikiMovePollOverrideForTesting(override: WikiMovePollOverride | undefined): void {
  wikiMovePollOverride = override;
}

class WikiToolError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "WikiToolError";
    this.code = code;
  }
}

export function getFeishuWikiChannelTools(enabled: boolean): Record<string, unknown>[] {
  if (!enabled) return [];
  return [
    {
      name: LIST_WIKI_NODES_TOOL_NAME,
      description: "List child nodes under a Feishu wiki space (optionally under one parent node).",
      display: {
        icon: "\u{1F4D1}",
        title: "List Feishu wiki nodes",
      },
      inputSchema: {
        type: "object",
        properties: {
          spaceIdOrUrl: { type: "string" },
          parentNodeTokenOrUrl: { type: "string" },
          pageSize: { type: "integer" },
          pageToken: { type: "string" },
        },
        required: ["spaceIdOrUrl"],
      },
    },
    {
      name: GET_WIKI_NODE_INFO_TOOL_NAME,
      description:
        "Get metadata of a Feishu wiki node. Supports reverse lookup from docx/sheet/bitable/etc. obj_token when 'objType' is provided.",
      display: {
        icon: "\u{1F50E}",
        title: "Get Feishu wiki node info",
      },
      inputSchema: {
        type: "object",
        properties: {
          nodeTokenOrUrl: { type: "string" },
          objType: {
            type: "string",
            enum: [...WIKI_OBJ_TYPES],
          },
        },
        required: ["nodeTokenOrUrl"],
      },
    },
    {
      name: MOVE_DOCX_TO_WIKI_TOOL_NAME,
      description: "Move a Feishu docx document into a Feishu wiki space.",
      display: {
        icon: "\u{1F69A}",
        title: "Move Feishu docx to wiki",
      },
      inputSchema: {
        type: "object",
        properties: {
          spaceIdOrUrl: { type: "string" },
          documentIdOrUrl: { type: "string" },
          parentWikiNodeTokenOrUrl: { type: "string" },
          apply: { type: "boolean" },
          waitForCompletion: { type: "boolean" },
        },
        required: ["spaceIdOrUrl", "documentIdOrUrl"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "move",
      },
    },
    {
      name: MOVE_WIKI_NODE_TOOL_NAME,
      description:
        "Move an existing Feishu wiki node to another parent node or wiki space (intra- or cross-space).",
      display: {
        icon: "\u{1F4C1}",
        title: "Move Feishu wiki node",
      },
      inputSchema: {
        type: "object",
        properties: {
          nodeTokenOrUrl: { type: "string" },
          sourceSpaceIdOrUrl: { type: "string" },
          targetParentTokenOrUrl: { type: "string" },
          targetSpaceIdOrUrl: { type: "string" },
        },
        required: ["nodeTokenOrUrl"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "nodeTokenOrUrl",
        operation: "move",
      },
    },
    {
      name: LIST_WIKI_SPACES_TOOL_NAME,
      description:
        "List Feishu wiki spaces that the current identity can access (for wiki discovery).",
      display: {
        icon: "\u{1F4DA}",
        title: "List Feishu wiki spaces",
      },
      inputSchema: {
        type: "object",
        properties: {
          pageSize: { type: "integer" },
          pageToken: { type: "string" },
        },
      },
    },
    {
      name: GET_WIKI_SPACE_TOOL_NAME,
      description: "Get metadata (name, visibility, space type) of a Feishu wiki space.",
      display: {
        icon: "\u{1F4D6}",
        title: "Get Feishu wiki space",
      },
      inputSchema: {
        type: "object",
        properties: {
          spaceIdOrUrl: { type: "string" },
        },
        required: ["spaceIdOrUrl"],
      },
    },
    {
      name: CREATE_WIKI_NODE_TOOL_NAME,
      description:
        "Create a new Feishu wiki node (docx/sheet/bitable/mindnote/slides/file) under a wiki space or parent node. Supports origin or shortcut node types.",
      display: {
        icon: "\u{1F195}",
        title: "Create Feishu wiki node",
      },
      inputSchema: {
        type: "object",
        properties: {
          spaceIdOrUrl: { type: "string" },
          parentNodeTokenOrUrl: { type: "string" },
          objType: {
            type: "string",
            enum: [...CREATE_WIKI_OBJ_TYPES],
          },
          nodeType: {
            type: "string",
            enum: ["origin", "shortcut"],
          },
          originNodeTokenOrUrl: { type: "string" },
          title: { type: "string" },
        },
        required: ["spaceIdOrUrl"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "spaceIdOrUrl",
        operation: "create",
      },
    },
    {
      name: RENAME_WIKI_NODE_TOOL_NAME,
      description: "Rename an existing Feishu wiki node title.",
      display: {
        icon: "\u{1F3F7}\u{FE0F}",
        title: "Rename Feishu wiki node",
      },
      inputSchema: {
        type: "object",
        properties: {
          spaceIdOrUrl: { type: "string" },
          nodeTokenOrUrl: { type: "string" },
          title: { type: "string" },
        },
        required: ["spaceIdOrUrl", "nodeTokenOrUrl", "title"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "nodeTokenOrUrl",
        operation: "write",
      },
    },
  ];
}

export async function maybeExecuteFeishuWikiToolCall(params: {
  toolName: string;
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown> | null> {
  if (!isFeishuWikiToolName(params.toolName)) return null;

  try {
    if (params.toolName === LIST_WIKI_NODES_TOOL_NAME) {
      return await executeListWikiNodesTool(params);
    }
    if (params.toolName === GET_WIKI_NODE_INFO_TOOL_NAME) {
      return await executeGetWikiNodeInfoTool(params);
    }
    if (params.toolName === MOVE_WIKI_NODE_TOOL_NAME) {
      return await executeMoveWikiNodeTool(params);
    }
    if (params.toolName === LIST_WIKI_SPACES_TOOL_NAME) {
      return await executeListWikiSpacesTool(params);
    }
    if (params.toolName === GET_WIKI_SPACE_TOOL_NAME) {
      return await executeGetWikiSpaceTool(params);
    }
    if (params.toolName === CREATE_WIKI_NODE_TOOL_NAME) {
      return await executeCreateWikiNodeTool(params);
    }
    if (params.toolName === RENAME_WIKI_NODE_TOOL_NAME) {
      return await executeRenameWikiNodeTool(params);
    }
    return await executeMoveDocxToWikiTool(params);
  } catch (error) {
    if (error instanceof WikiToolError) {
      return {
        success: false,
        errorCode: error.code,
        errorMessage: error.message,
      };
    }
    throw error;
  }
}

export function extractWikiNodeToken(nodeTokenOrUrl: string): string {
  const normalized = nodeTokenOrUrl.trim();
  if (!normalized) {
    throw new WikiToolError("InvalidWikiNodeToken", "Provide a non-empty wiki node token or wiki URL.");
  }
  if (WIKI_NODE_TOKEN_PATTERN.test(normalized)) {
    return normalized;
  }

  const parsedUrl = tryParseUrl(normalized);
  if (!parsedUrl) {
    throw new WikiToolError("InvalidWikiNodeToken", "Provide a valid wiki node token or wiki URL.");
  }
  const segments = parsedUrl.pathname.split("/").filter(Boolean);
  const wikiIndex = segments.findIndex((segment) => segment.toLowerCase() === "wiki");
  const maybeToken = wikiIndex >= 0 ? segments[wikiIndex + 1] : undefined;
  const reservedSegment = new Set(["settings", "home"]);
  if (maybeToken && WIKI_NODE_TOKEN_PATTERN.test(maybeToken)) {
    if (reservedSegment.has(maybeToken.toLowerCase())) {
      throw new WikiToolError("InvalidWikiNodeToken", "Provide a valid wiki node token or wiki URL.");
    }
    return maybeToken;
  }

  throw new WikiToolError("InvalidWikiNodeToken", "Provide a valid wiki node token or wiki URL.");
}

export function extractWikiSpaceId(spaceIdOrUrl: string): string {
  const normalized = spaceIdOrUrl.trim();
  if (!normalized) {
    throw new WikiToolError("InvalidWikiSpaceId", "Provide a non-empty wiki space ID or URL.");
  }
  if (WIKI_SPACE_ID_PATTERN.test(normalized)) {
    return normalized;
  }

  const parsedUrl = safeParseUrl(normalized);
  const fromQuery = parsedUrl.searchParams.get("space_id");
  if (fromQuery && WIKI_SPACE_ID_PATTERN.test(fromQuery)) {
    return fromQuery;
  }
  const segments = parsedUrl.pathname.split("/").filter(Boolean);
  const settingsIndex = segments.findIndex((segment) => segment.toLowerCase() === "settings");
  const maybeSpaceId = settingsIndex >= 0 ? segments[settingsIndex + 1] : undefined;
  if (maybeSpaceId && WIKI_SPACE_ID_PATTERN.test(maybeSpaceId)) {
    return maybeSpaceId;
  }

  throw new WikiToolError("InvalidWikiSpaceId", "Provide a valid wiki space ID or wiki settings URL.");
}

export async function resolveWikiSpaceTarget(params: {
  client: FeishuClient;
  spaceIdOrUrl: string;
  parentNodeTokenOrUrl?: string;
}): Promise<{ spaceId: string; parentNodeToken?: string }> {
  const input = params.spaceIdOrUrl.trim();
  if (!input) {
    throw new WikiToolError("InvalidWikiSpaceId", "Provide a non-empty wiki space ID or URL.");
  }

  let explicitParentNodeToken: string | undefined;
  if (params.parentNodeTokenOrUrl) {
    explicitParentNodeToken = extractWikiNodeToken(params.parentNodeTokenOrUrl);
  }

  if (WIKI_SPACE_ID_PATTERN.test(input)) {
    return {
      spaceId: input,
      parentNodeToken: explicitParentNodeToken,
    };
  }

  const parsedUrl = tryParseUrl(input);
  if (parsedUrl) {
    const fromQuery = parsedUrl.searchParams.get("space_id");
    if (fromQuery && WIKI_SPACE_ID_PATTERN.test(fromQuery)) {
      return {
        spaceId: fromQuery,
        parentNodeToken: explicitParentNodeToken,
      };
    }

    const segments = parsedUrl.pathname.split("/").filter(Boolean);
    const settingsIndex = segments.findIndex((segment) => segment.toLowerCase() === "settings");
    const maybeSpaceId = settingsIndex >= 0 ? segments[settingsIndex + 1] : undefined;
    if (maybeSpaceId && WIKI_SPACE_ID_PATTERN.test(maybeSpaceId)) {
      return {
        spaceId: maybeSpaceId,
        parentNodeToken: explicitParentNodeToken,
      };
    }

    try {
      const wikiNodeToken = extractWikiNodeToken(input);
      const wikiNode = await params.client.getWikiNode(wikiNodeToken);
      return {
        spaceId: wikiNode.spaceId,
        parentNodeToken: explicitParentNodeToken ?? wikiNode.nodeToken,
      };
    } catch (error) {
      if (!(error instanceof WikiToolError)) throw error;
      if (error.code !== "InvalidWikiNodeToken") throw error;
    }
  }

  if (WIKI_NODE_TOKEN_PATTERN.test(input)) {
    const wikiNode = await params.client.getWikiNode(input);
    return {
      spaceId: wikiNode.spaceId,
      parentNodeToken: explicitParentNodeToken ?? wikiNode.nodeToken,
    };
  }

  throw new WikiToolError(
    "InvalidWikiSpaceId",
    "Provide a valid wiki space ID, wiki settings URL, or wiki node URL/token.",
  );
}

function isFeishuWikiToolName(toolName: string): boolean {
  return (
    toolName === LIST_WIKI_NODES_TOOL_NAME ||
    toolName === GET_WIKI_NODE_INFO_TOOL_NAME ||
    toolName === MOVE_DOCX_TO_WIKI_TOOL_NAME ||
    toolName === MOVE_WIKI_NODE_TOOL_NAME ||
    toolName === LIST_WIKI_SPACES_TOOL_NAME ||
    toolName === GET_WIKI_SPACE_TOOL_NAME ||
    toolName === CREATE_WIKI_NODE_TOOL_NAME ||
    toolName === RENAME_WIKI_NODE_TOOL_NAME
  );
}

async function executeListWikiNodesTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const parentNodeTokenOrUrl = optionalText(params.args.parentNodeTokenOrUrl);
  const target = await resolveWikiSpaceTarget({
    client: params.client,
    spaceIdOrUrl: requiredText(params.args.spaceIdOrUrl, "spaceIdOrUrl"),
    parentNodeTokenOrUrl,
  });
  const pageSize = optionalInteger(params.args.pageSize, "pageSize");
  const pageToken = optionalText(params.args.pageToken);
  const page = await params.client.listWikiNodes({
    spaceId: target.spaceId,
    parentNodeToken: target.parentNodeToken,
    pageSize,
    pageToken,
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `Listed ${page.items.length} wiki node(s) from space ${target.spaceId}.` }],
    structuredResult: {
      spaceId: target.spaceId,
      parentNodeToken: target.parentNodeToken,
      items: page.items.map((item) => ({
        nodeToken: item.nodeToken,
        objToken: item.objToken,
        objType: item.objType,
        nodeType: item.nodeType,
        title: item.title ?? "",
        hasChild: item.hasChild ?? false,
      })),
      nextPageToken: page.nextPageToken,
      hasMore: page.hasMore,
    },
  };
}

async function executeGetWikiNodeInfoTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const rawToken = requiredText(params.args.nodeTokenOrUrl, "nodeTokenOrUrl");
  const objType = optionalWikiObjType(params.args.objType, WIKI_OBJ_TYPES);
  const lookupToken =
    objType && objType !== "wiki" ? extractRawObjectToken(rawToken) : extractWikiNodeToken(rawToken);
  const node = await params.client.getWikiNode(lookupToken, objType ?? "wiki");
  return {
    success: true,
    contentItems: [{ type: "text", text: `Loaded wiki node ${node.nodeToken}.` }],
    structuredResult: {
      spaceId: node.spaceId,
      nodeToken: node.nodeToken,
      objToken: node.objToken,
      objType: node.objType,
      nodeType: node.nodeType,
      parentNodeToken: node.parentNodeToken,
      originNodeToken: node.originNodeToken,
      originSpaceId: node.originSpaceId,
      hasChild: node.hasChild,
      title: node.title,
      objCreateTime: node.objCreateTime,
      objEditTime: node.objEditTime,
      nodeCreateTime: node.nodeCreateTime,
      ...(node.objType === "docx" ? { documentId: node.objToken } : {}),
    },
  };
}

async function executeListWikiSpacesTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const pageSize = optionalInteger(params.args.pageSize, "pageSize");
  const pageToken = optionalText(params.args.pageToken);
  const page = await params.client.listWikiSpaces({ pageSize, pageToken });
  return {
    success: true,
    contentItems: [
      { type: "text", text: `Listed ${page.items.length} wiki space(s).` },
    ],
    structuredResult: {
      items: page.items.map((item) => ({
        spaceId: item.spaceId,
        name: item.name ?? "",
        description: item.description ?? "",
        visibility: item.visibility,
        spaceType: item.spaceType,
        openSharing: item.openSharing,
      })),
      nextPageToken: page.nextPageToken,
      hasMore: page.hasMore,
    },
  };
}

async function executeGetWikiSpaceTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const target = await resolveWikiSpaceTarget({
    client: params.client,
    spaceIdOrUrl: requiredText(params.args.spaceIdOrUrl, "spaceIdOrUrl"),
  });
  const space = await params.client.getWikiSpace(target.spaceId);
  return {
    success: true,
    contentItems: [
      { type: "text", text: `Loaded wiki space ${space.spaceId}.` },
    ],
    structuredResult: {
      spaceId: space.spaceId,
      name: space.name ?? "",
      description: space.description ?? "",
      visibility: space.visibility,
      spaceType: space.spaceType,
      openSharing: space.openSharing,
    },
  };
}

async function executeCreateWikiNodeTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const parentNodeTokenOrUrl = optionalText(params.args.parentNodeTokenOrUrl);
  const target = await resolveWikiSpaceTarget({
    client: params.client,
    spaceIdOrUrl: requiredText(params.args.spaceIdOrUrl, "spaceIdOrUrl"),
    parentNodeTokenOrUrl,
  });

  const objType =
    (optionalWikiObjType(params.args.objType, CREATE_WIKI_OBJ_TYPES) as
      | Exclude<FeishuWikiObjType, "wiki" | "doc">
      | undefined) ?? "docx";
  const nodeType = optionalNodeType(params.args.nodeType) ?? "origin";
  const title = optionalText(params.args.title);
  const originNodeTokenOrUrl = optionalText(params.args.originNodeTokenOrUrl);

  if (nodeType === "shortcut" && !originNodeTokenOrUrl) {
    throw new WikiToolError(
      "InvalidArguments",
      "FeishuCreateWikiNode requires 'originNodeTokenOrUrl' when nodeType is 'shortcut'.",
    );
  }
  if (nodeType === "origin" && originNodeTokenOrUrl) {
    throw new WikiToolError(
      "InvalidArguments",
      "FeishuCreateWikiNode should not receive 'originNodeTokenOrUrl' when nodeType is 'origin'.",
    );
  }

  const originNodeToken = originNodeTokenOrUrl
    ? extractWikiNodeToken(originNodeTokenOrUrl)
    : undefined;

  // For docx, createWikiNode ignores body.title, so send title separately via updateWikiNodeTitle after creation.
  // For other obj_types (sheet/bitable/etc.) the server accepts body.title directly.
  const sendTitleInBody = objType !== "docx";
  const created = await params.client.createWikiNode({
    spaceId: target.spaceId,
    parentNodeToken: target.parentNodeToken,
    objType,
    nodeType,
    originNodeToken,
    title: sendTitleInBody ? title : undefined,
  });

  let finalTitle = created.title;
  if (objType === "docx" && title) {
    await params.client.updateWikiNodeTitle(created.spaceId || target.spaceId, created.nodeToken, title);
    finalTitle = title;
  }

  return {
    success: true,
    contentItems: [
      {
        type: "text",
        text: `Created wiki node ${created.nodeToken} (objType=${objType}) in space ${target.spaceId}.`,
      },
    ],
    structuredResult: {
      spaceId: created.spaceId || target.spaceId,
      nodeToken: created.nodeToken,
      objToken: created.objToken,
      objType: created.objType || objType,
      nodeType: created.nodeType || nodeType,
      parentNodeToken: created.parentNodeToken ?? target.parentNodeToken,
      originNodeToken: created.originNodeToken ?? originNodeToken,
      originSpaceId: created.originSpaceId,
      title: finalTitle ?? title ?? "",
      ...(created.objType === "docx" || objType === "docx"
        ? { documentId: created.objToken }
        : {}),
    },
  };
}

async function executeRenameWikiNodeTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const target = await resolveWikiSpaceTarget({
    client: params.client,
    spaceIdOrUrl: requiredText(params.args.spaceIdOrUrl, "spaceIdOrUrl"),
  });
  const nodeToken = extractWikiNodeToken(requiredText(params.args.nodeTokenOrUrl, "nodeTokenOrUrl"));
  const title = requiredText(params.args.title, "title");
  await params.client.updateWikiNodeTitle(target.spaceId, nodeToken, title);
  return {
    success: true,
    contentItems: [{ type: "text", text: `Renamed wiki node ${nodeToken} in space ${target.spaceId}.` }],
    structuredResult: {
      spaceId: target.spaceId,
      nodeToken,
      title,
    },
  };
}

async function executeMoveDocxToWikiTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const parentWikiNodeTokenOrUrl = optionalText(params.args.parentWikiNodeTokenOrUrl);
  const target = await resolveWikiSpaceTarget({
    client: params.client,
    spaceIdOrUrl: requiredText(params.args.spaceIdOrUrl, "spaceIdOrUrl"),
    parentNodeTokenOrUrl: parentWikiNodeTokenOrUrl,
  });
  const documentId = extractDocxId(requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"));
  const apply = optionalBoolean(params.args.apply);
  const waitForCompletion = params.args.waitForCompletion == null ? true : optionalBoolean(params.args.waitForCompletion);
  const result = await params.client.moveDocxToWiki({
    spaceId: target.spaceId,
    objToken: documentId,
    parentWikiToken: target.parentNodeToken,
    apply,
  });

  const baseStructured: Record<string, unknown> = {
    spaceId: target.spaceId,
    documentId,
    parentWikiNodeToken: target.parentNodeToken,
  };

  if (result.wikiToken) {
    return {
      success: true,
      contentItems: [{ type: "text", text: `Moved docx ${documentId} into wiki space ${target.spaceId}.` }],
      structuredResult: {
        ...baseStructured,
        ready: true,
        wikiToken: result.wikiToken,
      },
    };
  }

  if (result.applied) {
    return {
      success: true,
      contentItems: [
        { type: "text", text: `Move request submitted for approval (docx ${documentId}).` },
      ],
      structuredResult: {
        ...baseStructured,
        ready: false,
        applied: true,
        statusMessage: "move request submitted for approval",
      },
    };
  }

  if (!result.taskId) {
    return {
      success: true,
      contentItems: [
        { type: "text", text: `Move response returned neither wiki_token nor task_id for docx ${documentId}.` },
      ],
      structuredResult: {
        ...baseStructured,
        ready: false,
      },
    };
  }

  if (!waitForCompletion) {
    return {
      success: true,
      contentItems: [
        { type: "text", text: `Async move task created (task_id=${result.taskId}).` },
      ],
      structuredResult: {
        ...baseStructured,
        ready: false,
        taskId: result.taskId,
        nextAction: "Call FeishuMoveDocxToWiki with the same arguments (waitForCompletion omitted) to poll, or query the task via getWikiMoveTask API.",
      },
    };
  }

  const maxAttempts = wikiMovePollOverride?.maxAttempts ?? WIKI_MOVE_POLL_MAX_ATTEMPTS;
  const intervalMs = wikiMovePollOverride?.intervalMs ?? WIKI_MOVE_POLL_INTERVAL_MS;
  const sleep = wikiMovePollOverride?.sleep ?? defaultSleep;

  let lastStatus: number | undefined;
  let lastStatusMessage: string | undefined;
  let lastError: unknown;
  let successCount = 0;
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    if (attempt > 0) {
      await sleep(intervalMs);
    }
    try {
      const status = await params.client.getWikiMoveTask(result.taskId);
      successCount += 1;
      lastStatus = status.status;
      lastStatusMessage = status.statusMessage;
      if (status.status === WIKI_MOVE_STATUS_SUCCESS) {
        return {
          success: true,
          contentItems: [
            { type: "text", text: `Moved docx ${documentId} into wiki space ${target.spaceId}.` },
          ],
          structuredResult: {
            ...baseStructured,
            ready: true,
            taskId: result.taskId,
            wikiToken: status.wikiToken,
            statusMessage: status.statusMessage,
          },
        };
      }
      if (status.status === WIKI_MOVE_STATUS_FAILED) {
        throw new WikiToolError(
          "MoveDocxToWikiFailed",
          `Feishu wiki move task ${result.taskId} failed: ${status.statusMessage ?? "unknown error"}.`,
        );
      }
    } catch (error) {
      if (error instanceof WikiToolError) throw error;
      lastError = error;
    }
  }

  if (successCount === 0) {
    const message = lastError instanceof Error ? lastError.message : String(lastError ?? "unknown error");
    throw new WikiToolError(
      "WikiMoveTaskPollFailed",
      `Move task ${result.taskId} created, but all ${maxAttempts} status queries failed; last error: ${message}.`,
    );
  }

  return {
    success: true,
    contentItems: [
      { type: "text", text: `Async move task still processing (task_id=${result.taskId}).` },
    ],
    structuredResult: {
      ...baseStructured,
      ready: false,
      timedOut: true,
      taskId: result.taskId,
      status: lastStatus ?? WIKI_MOVE_STATUS_PROCESSING,
      statusMessage: lastStatusMessage ?? "processing",
      nextAction: `Re-query via getWikiMoveTask("${result.taskId}") once the move finishes.`,
    },
  };
}

async function executeMoveWikiNodeTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<Record<string, unknown>> {
  const nodeToken = extractWikiNodeToken(requiredText(params.args.nodeTokenOrUrl, "nodeTokenOrUrl"));
  const sourceSpaceIdOrUrl = optionalText(params.args.sourceSpaceIdOrUrl);
  const targetParentTokenOrUrl = optionalText(params.args.targetParentTokenOrUrl);
  const targetSpaceIdOrUrl = optionalText(params.args.targetSpaceIdOrUrl);

  if (!targetParentTokenOrUrl && !targetSpaceIdOrUrl) {
    throw new WikiToolError(
      "InvalidArguments",
      "FeishuMoveWikiNode requires at least one of 'targetParentTokenOrUrl' or 'targetSpaceIdOrUrl'.",
    );
  }

  const sourceSpaceId = sourceSpaceIdOrUrl
    ? (await resolveWikiSpaceTarget({ client: params.client, spaceIdOrUrl: sourceSpaceIdOrUrl })).spaceId
    : (await params.client.getWikiNode(nodeToken)).spaceId;

  let targetSpaceId: string;
  let targetParentToken: string | undefined;

  if (targetParentTokenOrUrl && targetSpaceIdOrUrl) {
    const parentTarget = await resolveWikiSpaceTarget({
      client: params.client,
      spaceIdOrUrl: targetParentTokenOrUrl,
    });
    const spaceOnly = await resolveWikiSpaceTarget({
      client: params.client,
      spaceIdOrUrl: targetSpaceIdOrUrl,
    });
    if (parentTarget.spaceId !== spaceOnly.spaceId) {
      throw new WikiToolError(
        "InconsistentWikiTarget",
        `Target parent node belongs to space ${parentTarget.spaceId}, which does not match targetSpaceIdOrUrl space ${spaceOnly.spaceId}.`,
      );
    }
    targetSpaceId = spaceOnly.spaceId;
    targetParentToken = parentTarget.parentNodeToken ?? extractWikiNodeToken(targetParentTokenOrUrl);
  } else if (targetParentTokenOrUrl) {
    const parentTarget = await resolveWikiSpaceTarget({
      client: params.client,
      spaceIdOrUrl: targetParentTokenOrUrl,
    });
    targetSpaceId = parentTarget.spaceId;
    targetParentToken = parentTarget.parentNodeToken ?? extractWikiNodeToken(targetParentTokenOrUrl);
  } else {
    const spaceOnly = await resolveWikiSpaceTarget({
      client: params.client,
      spaceIdOrUrl: requiredText(targetSpaceIdOrUrl, "targetSpaceIdOrUrl"),
    });
    targetSpaceId = spaceOnly.spaceId;
    targetParentToken = undefined;
  }

  const movedNode = await params.client.moveWikiNode({
    spaceId: sourceSpaceId,
    nodeToken,
    targetParentToken,
    targetSpaceId: targetSpaceId !== sourceSpaceId ? targetSpaceId : undefined,
  });

  return {
    success: true,
    contentItems: [
      {
        type: "text",
        text: `Moved wiki node ${nodeToken} from space ${sourceSpaceId} to ${targetSpaceId}.`,
      },
    ],
    structuredResult: {
      sourceSpaceId,
      targetSpaceId,
      nodeToken: movedNode.nodeToken || nodeToken,
      objToken: movedNode.objToken,
      objType: movedNode.objType,
      nodeType: movedNode.nodeType,
      parentNodeToken: movedNode.parentNodeToken ?? targetParentToken,
      title: movedNode.title,
      hasChild: movedNode.hasChild,
    },
  };
}

function defaultSleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function extractDocxId(documentIdOrUrl: string): string {
  const normalized = documentIdOrUrl.trim();
  if (DOCX_ID_PATTERN.test(normalized)) return normalized;
  const parsedUrl = safeParseUrl(normalized);
  const segments = parsedUrl.pathname.split("/").filter(Boolean);
  const docxIndex = segments.findIndex((segment) => segment.toLowerCase() === "docx");
  const maybeId = docxIndex >= 0 ? segments[docxIndex + 1] : undefined;
  if (maybeId && DOCX_ID_PATTERN.test(maybeId)) {
    return maybeId;
  }
  throw new WikiToolError(
    "InvalidDocumentId",
    "Provide a Feishu docx document_id or a docx URL such as https://feishu.cn/docx/<document_id>.",
  );
}

function requiredText(value: unknown, fieldName: string): string {
  const text = String(value ?? "").trim();
  if (!text) {
    throw new WikiToolError("InvalidArguments", `Feishu wiki tool requires a non-empty '${fieldName}'.`);
  }
  return text;
}

function optionalText(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

function optionalInteger(value: unknown, fieldName: string): number | undefined {
  if (value == null) return undefined;
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed <= 0) {
    throw new WikiToolError("InvalidArguments", `Feishu wiki tool '${fieldName}' must be a positive integer.`);
  }
  return parsed;
}

function optionalBoolean(value: unknown): boolean | undefined {
  if (value == null) return undefined;
  if (typeof value !== "boolean") {
    throw new WikiToolError("InvalidArguments", "Feishu wiki tool 'apply' must be a boolean.");
  }
  return value;
}

function optionalWikiObjType(
  value: unknown,
  allowed: readonly FeishuWikiObjType[],
): FeishuWikiObjType | undefined {
  if (value == null) return undefined;
  if (typeof value !== "string") {
    throw new WikiToolError("InvalidArguments", "Feishu wiki tool 'objType' must be a string.");
  }
  const normalized = value.trim().toLowerCase() as FeishuWikiObjType;
  if (!allowed.includes(normalized)) {
    throw new WikiToolError(
      "InvalidArguments",
      `Feishu wiki tool 'objType' must be one of: ${allowed.join(", ")}.`,
    );
  }
  return normalized;
}

function optionalNodeType(value: unknown): "origin" | "shortcut" | undefined {
  if (value == null) return undefined;
  if (typeof value !== "string") {
    throw new WikiToolError("InvalidArguments", "Feishu wiki tool 'nodeType' must be a string.");
  }
  const normalized = value.trim().toLowerCase();
  if (normalized !== "origin" && normalized !== "shortcut") {
    throw new WikiToolError(
      "InvalidArguments",
      "Feishu wiki tool 'nodeType' must be 'origin' or 'shortcut'.",
    );
  }
  return normalized;
}

function extractRawObjectToken(tokenOrUrl: string): string {
  const normalized = tokenOrUrl.trim();
  if (!normalized) {
    throw new WikiToolError("InvalidArguments", "Provide a non-empty object token or URL.");
  }
  // Accept raw tokens (docx/sheet/bitable IDs share the same alphanumeric 16-40 range).
  if (WIKI_NODE_TOKEN_PATTERN.test(normalized)) {
    return normalized;
  }
  const parsedUrl = tryParseUrl(normalized);
  if (parsedUrl) {
    const segments = parsedUrl.pathname.split("/").filter(Boolean);
    // Common Feishu doc types appear as /docx/<token>, /sheets/<token>, /base/<token>, /mindnote/<token>, /file/<token>, /slides/<token>, /wiki/<token>.
    const knownHosts = new Set([
      "docx",
      "doc",
      "sheets",
      "sheet",
      "base",
      "bitable",
      "mindnotes",
      "mindnote",
      "file",
      "files",
      "slides",
      "wiki",
    ]);
    for (let i = 0; i < segments.length - 1; i++) {
      if (knownHosts.has(segments[i].toLowerCase())) {
        const maybeToken = segments[i + 1];
        if (maybeToken && WIKI_NODE_TOKEN_PATTERN.test(maybeToken)) {
          return maybeToken;
        }
      }
    }
    const tail = segments[segments.length - 1];
    if (tail && WIKI_NODE_TOKEN_PATTERN.test(tail)) {
      return tail;
    }
  }
  throw new WikiToolError(
    "InvalidArguments",
    "Provide a valid Feishu object token or URL (docx/sheet/bitable/mindnote/file/slides/wiki).",
  );
}

function safeParseUrl(value: string): URL {
  try {
    return new URL(value);
  } catch {
    throw new WikiToolError("InvalidArguments", "Provided value is not a valid URL or token.");
  }
}

function tryParseUrl(value: string): URL | undefined {
  try {
    return new URL(value);
  } catch {
    return undefined;
  }
}
