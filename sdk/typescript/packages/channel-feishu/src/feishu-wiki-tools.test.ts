import assert from "node:assert/strict";
import test from "node:test";

import type { FeishuClient } from "./feishu-client.js";
import {
  __setWikiMovePollOverrideForTesting,
  CREATE_WIKI_NODE_TOOL_NAME,
  extractWikiNodeToken,
  extractWikiSpaceId,
  GET_WIKI_NODE_INFO_TOOL_NAME,
  GET_WIKI_SPACE_TOOL_NAME,
  getFeishuWikiChannelTools,
  LIST_WIKI_NODES_TOOL_NAME,
  LIST_WIKI_SPACES_TOOL_NAME,
  maybeExecuteFeishuWikiToolCall,
  MOVE_DOCX_TO_WIKI_TOOL_NAME,
  MOVE_WIKI_NODE_TOOL_NAME,
  RENAME_WIKI_NODE_TOOL_NAME,
  resolveWikiSpaceTarget,
} from "./feishu-wiki-tools.js";

const WIKI_SPACE_ID = "1234567890123456789";
const WIKI_NODE_TOKEN = "WikiNodePlaceholder00000001";
const LEGACY_WIKI_NODE_TOKEN = "wikABCDEFGHIJKLMNOPQRSTUVWX";
const DOC_ID = "DocxPlaceholder000000000001";
const LEGACY_DOC_ID = "doxABCDEFGHIJKLMNOPQRSTUVWX";

test("wiki channel tool registry only returns tools when enabled", () => {
  assert.deepEqual(getFeishuWikiChannelTools(false), []);
  const tools = getFeishuWikiChannelTools(true);
  assert.deepEqual(
    tools.map((tool) => String(tool.name ?? "")),
    [
      LIST_WIKI_NODES_TOOL_NAME,
      GET_WIKI_NODE_INFO_TOOL_NAME,
      MOVE_DOCX_TO_WIKI_TOOL_NAME,
      MOVE_WIKI_NODE_TOOL_NAME,
      LIST_WIKI_SPACES_TOOL_NAME,
      GET_WIKI_SPACE_TOOL_NAME,
      CREATE_WIKI_NODE_TOOL_NAME,
      RENAME_WIKI_NODE_TOOL_NAME,
    ],
  );
});

test("wiki mutating tools declare remoteResource approval and read-only tools do not", () => {
  const tools = getFeishuWikiChannelTools(true);
  const byName = new Map(tools.map((tool) => [String(tool.name ?? ""), tool]));

  const moveDocxTool = byName.get(MOVE_DOCX_TO_WIKI_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(moveDocxTool);
  assert.deepEqual(moveDocxTool!.approval, {
    kind: "remoteResource",
    targetArgument: "documentIdOrUrl",
    operation: "move",
  });

  const moveNodeTool = byName.get(MOVE_WIKI_NODE_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(moveNodeTool);
  assert.deepEqual(moveNodeTool!.approval, {
    kind: "remoteResource",
    targetArgument: "nodeTokenOrUrl",
    operation: "move",
  });

  const createNodeTool = byName.get(CREATE_WIKI_NODE_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(createNodeTool);
  assert.deepEqual(createNodeTool!.approval, {
    kind: "remoteResource",
    targetArgument: "spaceIdOrUrl",
    operation: "create",
  });

  const renameNodeTool = byName.get(RENAME_WIKI_NODE_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(renameNodeTool);
  assert.deepEqual(renameNodeTool!.approval, {
    kind: "remoteResource",
    targetArgument: "nodeTokenOrUrl",
    operation: "write",
  });

  const readOnlyToolNames = [
    LIST_WIKI_NODES_TOOL_NAME,
    GET_WIKI_NODE_INFO_TOOL_NAME,
    LIST_WIKI_SPACES_TOOL_NAME,
    GET_WIKI_SPACE_TOOL_NAME,
  ];
  for (const name of readOnlyToolNames) {
    const tool = byName.get(name) as Record<string, unknown> | undefined;
    assert.ok(tool, `${name} is registered`);
    assert.equal(tool!.approval, undefined, `${name} should not declare approval`);
  }
});

test("extractWikiNodeToken and extractWikiSpaceId support token and URL forms", () => {
  assert.equal(extractWikiNodeToken(WIKI_NODE_TOKEN), WIKI_NODE_TOKEN);
  assert.equal(extractWikiNodeToken(`https://feishu.cn/wiki/${WIKI_NODE_TOKEN}`), WIKI_NODE_TOKEN);
  assert.equal(extractWikiNodeToken(LEGACY_WIKI_NODE_TOKEN), LEGACY_WIKI_NODE_TOKEN);
  assert.equal(
    extractWikiNodeToken(`https://feishu.cn/wiki/${LEGACY_WIKI_NODE_TOKEN}`),
    LEGACY_WIKI_NODE_TOKEN,
  );
  assert.equal(extractWikiSpaceId(WIKI_SPACE_ID), WIKI_SPACE_ID);
  assert.equal(
    extractWikiSpaceId(`https://feishu.cn/wiki/settings/${WIKI_SPACE_ID}`),
    WIKI_SPACE_ID,
  );
});

test("resolveWikiSpaceTarget resolves wiki URL to spaceId and inferred parent node", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_TOKEN);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;

  const target = await resolveWikiSpaceTarget({
    client,
    spaceIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_TOKEN}`,
  });
  assert.deepEqual(target, {
    spaceId: WIKI_SPACE_ID,
    parentNodeToken: WIKI_NODE_TOKEN,
  });
});

test("FeishuListWikiNodes supports wiki URL and auto-resolves space", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_TOKEN);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async listWikiNodes(options: {
      spaceId: string;
      parentNodeToken?: string;
      pageSize?: number;
      pageToken?: string;
    }) {
      assert.deepEqual(options, {
        spaceId: WIKI_SPACE_ID,
        parentNodeToken: WIKI_NODE_TOKEN,
        pageSize: 20,
        pageToken: "next",
      });
      return {
        items: [
          {
            spaceId: WIKI_SPACE_ID,
            nodeToken: WIKI_NODE_TOKEN,
            objToken: DOC_ID,
            objType: "docx",
            nodeType: "origin",
            title: "Release Notes",
            hasChild: true,
          },
        ],
        nextPageToken: "next-2",
        hasMore: true,
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: LIST_WIKI_NODES_TOOL_NAME,
    args: {
      spaceIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_TOKEN}`,
      pageSize: 20,
      pageToken: "next",
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredResult, {
    spaceId: WIKI_SPACE_ID,
    parentNodeToken: WIKI_NODE_TOKEN,
    items: [
      {
        nodeToken: WIKI_NODE_TOKEN,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
        title: "Release Notes",
        hasChild: true,
      },
    ],
    nextPageToken: "next-2",
    hasMore: true,
  });
});

test("FeishuGetWikiNodeInfo includes documentId when node objType is docx", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_TOKEN);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: GET_WIKI_NODE_INFO_TOOL_NAME,
    args: {
      nodeTokenOrUrl: WIKI_NODE_TOKEN,
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredResult, {
    spaceId: WIKI_SPACE_ID,
    nodeToken: WIKI_NODE_TOKEN,
    objToken: DOC_ID,
    objType: "docx",
    nodeType: "origin",
    parentNodeToken: undefined,
    originNodeToken: undefined,
    originSpaceId: undefined,
    hasChild: undefined,
    title: undefined,
    objCreateTime: undefined,
    objEditTime: undefined,
    nodeCreateTime: undefined,
    documentId: DOC_ID,
  });
});

test("FeishuMoveDocxToWiki returns ready=true when wiki_token is immediate", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_TOKEN);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async moveDocxToWiki(options: {
      spaceId: string;
      objToken: string;
    }) {
      assert.equal(options.spaceId, WIKI_SPACE_ID);
      assert.equal(options.objToken, DOC_ID);
      return { wikiToken: WIKI_NODE_TOKEN };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: MOVE_DOCX_TO_WIKI_TOOL_NAME,
    args: {
      spaceIdOrUrl: WIKI_NODE_TOKEN,
      documentIdOrUrl: DOC_ID,
    },
    client,
  });
  assert.equal(result?.success, true);
  const structured = result?.structuredResult as Record<string, unknown>;
  assert.equal(structured.ready, true);
  assert.equal(structured.wikiToken, WIKI_NODE_TOKEN);
});

test("FeishuMoveDocxToWiki returns ready=true after polling completes", async () => {
  __setWikiMovePollOverrideForTesting({ intervalMs: 0, maxAttempts: 5, sleep: async () => {} });
  try {
    let pollCalls = 0;
    const client = {
      async moveDocxToWiki() {
        return { taskId: "task-success" };
      },
      async getWikiMoveTask(taskId: string) {
        assert.equal(taskId, "task-success");
        pollCalls += 1;
        if (pollCalls < 2) {
          return { taskId, status: 1, statusMessage: "processing" };
        }
        return {
          taskId,
          status: 2,
          statusMessage: "success",
          wikiToken: "ResolvedWikiPlaceholder0001",
          objToken: DOC_ID,
          objType: "docx",
        };
      },
    } as unknown as FeishuClient;

    const result = await maybeExecuteFeishuWikiToolCall({
      toolName: MOVE_DOCX_TO_WIKI_TOOL_NAME,
      args: {
        spaceIdOrUrl: WIKI_SPACE_ID,
        documentIdOrUrl: DOC_ID,
        apply: true,
      },
      client,
    });
    assert.equal(result?.success, true);
    const structured = result?.structuredResult as Record<string, unknown>;
    assert.equal(structured.ready, true);
    assert.equal(structured.wikiToken, "ResolvedWikiPlaceholder0001");
    assert.equal(pollCalls, 2);
  } finally {
    __setWikiMovePollOverrideForTesting(undefined);
  }
});

test("FeishuRenameWikiNode updates node title", async () => {
  let called = 0;
  const client = {
    async updateWikiNodeTitle(spaceId: string, nodeToken: string, title: string) {
      called += 1;
      assert.equal(spaceId, WIKI_SPACE_ID);
      assert.equal(nodeToken, WIKI_NODE_TOKEN);
      assert.equal(title, "Renamed");
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: RENAME_WIKI_NODE_TOOL_NAME,
    args: {
      spaceIdOrUrl: WIKI_SPACE_ID,
      nodeTokenOrUrl: WIKI_NODE_TOKEN,
      title: "Renamed",
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(called, 1);
});

test("FeishuMoveDocxToWiki returns timedOut when polling exhausts attempts", async () => {
  __setWikiMovePollOverrideForTesting({ intervalMs: 0, maxAttempts: 3, sleep: async () => {} });
  try {
    let pollCalls = 0;
    const client = {
      async moveDocxToWiki() {
        return { taskId: "task-timeout" };
      },
      async getWikiMoveTask(taskId: string) {
        pollCalls += 1;
        return { taskId, status: 1, statusMessage: "processing" };
      },
    } as unknown as FeishuClient;

    const result = await maybeExecuteFeishuWikiToolCall({
      toolName: MOVE_DOCX_TO_WIKI_TOOL_NAME,
      args: {
        spaceIdOrUrl: WIKI_SPACE_ID,
        documentIdOrUrl: `https://feishu.cn/docx/${LEGACY_DOC_ID}`,
      },
      client,
    });
    assert.equal(result?.success, true);
    const structured = result?.structuredResult as Record<string, unknown>;
    assert.equal(structured.ready, false);
    assert.equal(structured.timedOut, true);
    assert.equal(structured.taskId, "task-timeout");
    assert.equal(pollCalls, 3);
  } finally {
    __setWikiMovePollOverrideForTesting(undefined);
  }
});

test("FeishuMoveDocxToWiki raises WikiMoveTaskPollFailed when every poll errors", async () => {
  __setWikiMovePollOverrideForTesting({ intervalMs: 0, maxAttempts: 2, sleep: async () => {} });
  try {
    const client = {
      async moveDocxToWiki() {
        return { taskId: "task-error" };
      },
      async getWikiMoveTask() {
        throw new Error("transient network failure");
      },
    } as unknown as FeishuClient;

    const result = await maybeExecuteFeishuWikiToolCall({
      toolName: MOVE_DOCX_TO_WIKI_TOOL_NAME,
      args: {
        spaceIdOrUrl: WIKI_SPACE_ID,
        documentIdOrUrl: DOC_ID,
      },
      client,
    });
    assert.equal(result?.success, false);
    assert.equal(result?.errorCode, "WikiMoveTaskPollFailed");
  } finally {
    __setWikiMovePollOverrideForTesting(undefined);
  }
});

test("FeishuMoveDocxToWiki with waitForCompletion=false surfaces taskId and nextAction", async () => {
  const client = {
    async moveDocxToWiki() {
      return { taskId: "task-async" };
    },
    async getWikiMoveTask() {
      throw new Error("should not be called when waitForCompletion=false");
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: MOVE_DOCX_TO_WIKI_TOOL_NAME,
    args: {
      spaceIdOrUrl: WIKI_SPACE_ID,
      documentIdOrUrl: DOC_ID,
      waitForCompletion: false,
    },
    client,
  });
  assert.equal(result?.success, true);
  const structured = result?.structuredResult as Record<string, unknown>;
  assert.equal(structured.ready, false);
  assert.equal(structured.taskId, "task-async");
  assert.ok(typeof structured.nextAction === "string");
});

test("FeishuMoveWikiNode auto-resolves source space from node and target space from parent", async () => {
  const TARGET_PARENT_TOKEN = "TargetParentPlaceholder00001";
  const TARGET_SPACE_ID = "7000000000000000001";
  const getWikiNodeCalls: string[] = [];
  let moveCalls = 0;

  const client = {
    async getWikiNode(nodeToken: string) {
      getWikiNodeCalls.push(nodeToken);
      if (nodeToken === WIKI_NODE_TOKEN) {
        return {
          spaceId: WIKI_SPACE_ID,
          nodeToken,
          objToken: DOC_ID,
          objType: "docx",
          nodeType: "origin",
        };
      }
      if (nodeToken === TARGET_PARENT_TOKEN) {
        return {
          spaceId: TARGET_SPACE_ID,
          nodeToken,
          objToken: DOC_ID,
          objType: "docx",
          nodeType: "origin",
        };
      }
      throw new Error(`unexpected nodeToken ${nodeToken}`);
    },
    async moveWikiNode(options: {
      spaceId: string;
      nodeToken: string;
      targetParentToken?: string;
      targetSpaceId?: string;
    }) {
      moveCalls += 1;
      assert.equal(options.spaceId, WIKI_SPACE_ID);
      assert.equal(options.nodeToken, WIKI_NODE_TOKEN);
      assert.equal(options.targetParentToken, TARGET_PARENT_TOKEN);
      assert.equal(options.targetSpaceId, TARGET_SPACE_ID);
      return {
        spaceId: TARGET_SPACE_ID,
        nodeToken: WIKI_NODE_TOKEN,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
        parentNodeToken: TARGET_PARENT_TOKEN,
        title: "Relocated Page",
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: MOVE_WIKI_NODE_TOOL_NAME,
    args: {
      nodeTokenOrUrl: WIKI_NODE_TOKEN,
      targetParentTokenOrUrl: TARGET_PARENT_TOKEN,
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(moveCalls, 1);
  const structured = result?.structuredResult as Record<string, unknown>;
  assert.equal(structured.sourceSpaceId, WIKI_SPACE_ID);
  assert.equal(structured.targetSpaceId, TARGET_SPACE_ID);
  assert.equal(structured.parentNodeToken, TARGET_PARENT_TOKEN);
});

test("FeishuListWikiSpaces surfaces paginated space metadata", async () => {
  const client = {
    async listWikiSpaces(options: { pageSize?: number; pageToken?: string }) {
      assert.deepEqual(options, { pageSize: 20, pageToken: "cursor" });
      return {
        items: [
          {
            spaceId: WIKI_SPACE_ID,
            name: "Team Wiki",
            description: "Shared workspace",
            visibility: "public",
            spaceType: "team",
            openSharing: "open",
          },
        ],
        nextPageToken: "next-page",
        hasMore: true,
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: LIST_WIKI_SPACES_TOOL_NAME,
    args: { pageSize: 20, pageToken: "cursor" },
    client,
  });
  assert.equal(result?.success, true);
  const structured = result?.structuredResult as Record<string, unknown>;
  assert.equal(structured.nextPageToken, "next-page");
  assert.equal(structured.hasMore, true);
  const items = structured.items as Array<Record<string, unknown>>;
  assert.equal(items.length, 1);
  assert.equal(items[0]!.spaceId, WIKI_SPACE_ID);
  assert.equal(items[0]!.name, "Team Wiki");
  assert.equal(items[0]!.visibility, "public");
});

test("FeishuGetWikiSpace accepts wiki URL and returns space metadata", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_TOKEN);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async getWikiSpace(spaceId: string) {
      assert.equal(spaceId, WIKI_SPACE_ID);
      return {
        spaceId,
        name: "Team Wiki",
        description: "Shared workspace",
        visibility: "public",
        spaceType: "team",
        openSharing: "open",
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: GET_WIKI_SPACE_TOOL_NAME,
    args: { spaceIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_TOKEN}` },
    client,
  });
  assert.equal(result?.success, true);
  const structured = result?.structuredResult as Record<string, unknown>;
  assert.equal(structured.spaceId, WIKI_SPACE_ID);
  assert.equal(structured.name, "Team Wiki");
  assert.equal(structured.visibility, "public");
});

test("FeishuGetWikiNodeInfo reverse-looks-up via objType=docx", async () => {
  const client = {
    async getWikiNode(token: string, objType: string) {
      assert.equal(token, DOC_ID);
      assert.equal(objType, "docx");
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken: WIKI_NODE_TOKEN,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: GET_WIKI_NODE_INFO_TOOL_NAME,
    args: {
      nodeTokenOrUrl: DOC_ID,
      objType: "docx",
    },
    client,
  });
  assert.equal(result?.success, true);
  const structured = result?.structuredResult as Record<string, unknown>;
  assert.equal(structured.nodeToken, WIKI_NODE_TOKEN);
  assert.equal(structured.objToken, DOC_ID);
  assert.equal(structured.objType, "docx");
  assert.equal(structured.documentId, DOC_ID);
});

test("FeishuCreateWikiNode creates docx node and updates title separately", async () => {
  const createCalls: Array<Record<string, unknown>> = [];
  const titleCalls: Array<{ spaceId: string; nodeToken: string; title: string }> = [];
  const client = {
    async createWikiNode(options: Record<string, unknown>) {
      createCalls.push(options);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken: WIKI_NODE_TOKEN,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
        parentNodeToken: undefined,
      };
    },
    async updateWikiNodeTitle(spaceId: string, nodeToken: string, title: string) {
      titleCalls.push({ spaceId, nodeToken, title });
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: CREATE_WIKI_NODE_TOOL_NAME,
    args: {
      spaceIdOrUrl: WIKI_SPACE_ID,
      objType: "docx",
      title: "New Release Notes",
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(createCalls.length, 1);
  assert.equal(createCalls[0]!.objType, "docx");
  assert.equal(createCalls[0]!.nodeType, "origin");
  assert.equal(createCalls[0]!.title, undefined);
  assert.equal(titleCalls.length, 1);
  assert.equal(titleCalls[0]!.title, "New Release Notes");
  const structured = result?.structuredResult as Record<string, unknown>;
  assert.equal(structured.nodeToken, WIKI_NODE_TOKEN);
  assert.equal(structured.title, "New Release Notes");
  assert.equal(structured.documentId, DOC_ID);
});

test("FeishuCreateWikiNode creates sheet node with inline body title", async () => {
  const createCalls: Array<Record<string, unknown>> = [];
  const client = {
    async createWikiNode(options: Record<string, unknown>) {
      createCalls.push(options);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken: WIKI_NODE_TOKEN,
        objToken: "SheetToken0000000000000000001",
        objType: "sheet",
        nodeType: "origin",
      };
    },
    async updateWikiNodeTitle() {
      throw new Error("updateWikiNodeTitle should not be called for non-docx objType");
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: CREATE_WIKI_NODE_TOOL_NAME,
    args: {
      spaceIdOrUrl: WIKI_SPACE_ID,
      objType: "sheet",
      title: "Metrics",
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(createCalls.length, 1);
  assert.equal(createCalls[0]!.objType, "sheet");
  assert.equal(createCalls[0]!.title, "Metrics");
});

test("FeishuCreateWikiNode shortcut requires originNodeTokenOrUrl", async () => {
  const client = {
    async createWikiNode() {
      throw new Error("createWikiNode should not be called when validation fails");
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: CREATE_WIKI_NODE_TOOL_NAME,
    args: {
      spaceIdOrUrl: WIKI_SPACE_ID,
      nodeType: "shortcut",
    },
    client,
  });
  assert.equal(result?.success, false);
  assert.equal(result?.errorCode, "InvalidArguments");
});

test("FeishuCreateWikiNode origin rejects originNodeTokenOrUrl", async () => {
  const ORIGIN_TOKEN = "OriginNodePlaceholder0000001";
  const client = {
    async createWikiNode() {
      throw new Error("createWikiNode should not be called when validation fails");
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: CREATE_WIKI_NODE_TOOL_NAME,
    args: {
      spaceIdOrUrl: WIKI_SPACE_ID,
      nodeType: "origin",
      originNodeTokenOrUrl: ORIGIN_TOKEN,
    },
    client,
  });
  assert.equal(result?.success, false);
  assert.equal(result?.errorCode, "InvalidArguments");
});

test("FeishuMoveWikiNode rejects inconsistent targetSpaceId vs targetParent", async () => {
  const TARGET_PARENT_TOKEN = "TargetParentPlaceholder00001";
  const OTHER_SPACE_ID = "7000000000000000002";

  const client = {
    async getWikiNode(nodeToken: string) {
      if (nodeToken === TARGET_PARENT_TOKEN) {
        return {
          spaceId: "7000000000000000001",
          nodeToken,
          objToken: DOC_ID,
          objType: "docx",
          nodeType: "origin",
        };
      }
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async moveWikiNode() {
      throw new Error("moveWikiNode should not be invoked when targets are inconsistent");
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuWikiToolCall({
    toolName: MOVE_WIKI_NODE_TOOL_NAME,
    args: {
      nodeTokenOrUrl: WIKI_NODE_TOKEN,
      targetParentTokenOrUrl: TARGET_PARENT_TOKEN,
      targetSpaceIdOrUrl: OTHER_SPACE_ID,
    },
    client,
  });
  assert.equal(result?.success, false);
  assert.equal(result?.errorCode, "InconsistentWikiTarget");
});
