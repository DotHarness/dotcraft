import assert from "node:assert/strict";
import test from "node:test";

import { FeishuAdapter } from "./feishu-adapter.js";
import type { FeishuClient } from "./feishu-client.js";
import {
  APPEND_DOCX_TOOL_NAME,
  ADD_DOCX_COMMENT_REPLY_TOOL_NAME,
  ADD_DOCX_COMMENT_TOOL_NAME,
  BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME,
  CREATE_DOCX_TOOL_NAME,
  DELETE_DOCX_BLOCKS_TOOL_NAME,
  EMBED_DOCX_MEDIA_TOOL_NAME,
  extractDocxDocumentId,
  GET_DOCX_BLOCK_TOOL_NAME,
  getFeishuDocxChannelTools,
  INSERT_DOCX_BLOCKS_TOOL_NAME,
  LIST_DOCX_BLOCKS_TOOL_NAME,
  LIST_DOCX_COMMENT_REPLIES_TOOL_NAME,
  LIST_DOCX_COMMENTS_TOOL_NAME,
  maybeExecuteFeishuDocxToolCall,
  READ_DOCX_TOOL_NAME,
  RESOLVE_DOCX_COMMENT_TOOL_NAME,
  resolveDocxDocumentId,
  UPDATE_DOCX_BLOCKS_TOOL_NAME,
  UPDATE_DOCX_CONTENT_TOOL_NAME,
  UPDATE_DOCX_TITLE_TOOL_NAME,
  toFeishuDocxChildren,
  type FeishuSimpleBlock,
} from "./feishu-docx-tools.js";
import { FeishuApiError, type FeishuConfig } from "./feishu-types.js";
import {
  CREATE_WIKI_NODE_TOOL_NAME,
  GET_WIKI_NODE_INFO_TOOL_NAME,
  GET_WIKI_SPACE_TOOL_NAME,
  LIST_WIKI_NODES_TOOL_NAME,
  LIST_WIKI_SPACES_TOOL_NAME,
  MOVE_DOCX_TO_WIKI_TOOL_NAME,
  MOVE_WIKI_NODE_TOOL_NAME,
  RENAME_WIKI_NODE_TOOL_NAME,
} from "./feishu-wiki-tools.js";

const DOC_ID = "DocxPlaceholder000000000001";
const LEGACY_DOC_ID = "doxABCDEFGHIJKLMNOPQRSTUVWX";
const WIKI_NODE_ID = "WikiNodePlaceholder00000001";
const WIKI_SPACE_ID = "1234567890123456789";

function validConfig(): FeishuConfig {
  return {
    dotcraft: {
      wsUrl: "ws://127.0.0.1:9100/ws",
    },
    feishu: {
      appId: "cli_test",
      appSecret: "test-secret",
      tools: {
        docs: {
          enabled: true,
        },
      },
    },
  };
}

test("docx channel tool registry only returns tools when enabled", () => {
  assert.deepEqual(getFeishuDocxChannelTools(false), []);
  const tools = getFeishuDocxChannelTools(true);
  assert.deepEqual(
    tools.map((tool) => String(tool.name ?? "")),
    [
      CREATE_DOCX_TOOL_NAME,
      READ_DOCX_TOOL_NAME,
      APPEND_DOCX_TOOL_NAME,
      LIST_DOCX_BLOCKS_TOOL_NAME,
      GET_DOCX_BLOCK_TOOL_NAME,
      INSERT_DOCX_BLOCKS_TOOL_NAME,
      UPDATE_DOCX_BLOCKS_TOOL_NAME,
      DELETE_DOCX_BLOCKS_TOOL_NAME,
      UPDATE_DOCX_TITLE_TOOL_NAME,
      UPDATE_DOCX_CONTENT_TOOL_NAME,
      EMBED_DOCX_MEDIA_TOOL_NAME,
      LIST_DOCX_COMMENTS_TOOL_NAME,
      BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME,
      LIST_DOCX_COMMENT_REPLIES_TOOL_NAME,
      ADD_DOCX_COMMENT_TOOL_NAME,
      ADD_DOCX_COMMENT_REPLY_TOOL_NAME,
      RESOLVE_DOCX_COMMENT_TOOL_NAME,
    ],
  );
});

test("docx mutating tools declare remoteResource approval and read tool does not", () => {
  const tools = getFeishuDocxChannelTools(true);
  const byName = new Map(tools.map((tool) => [String(tool.name ?? ""), tool]));

  const createTool = byName.get(CREATE_DOCX_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(createTool, "create tool is registered");
  assert.deepEqual(createTool!.approval, {
    kind: "remoteResource",
    targetArgument: "title",
    operation: "create",
  });
  const createSchema = createTool!.inputSchema as { required?: string[] };
  assert.deepEqual(createSchema.required, ["title"]);

  const appendTool = byName.get(APPEND_DOCX_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(appendTool, "append tool is registered");
  assert.deepEqual(appendTool!.approval, {
    kind: "remoteResource",
    targetArgument: "documentIdOrUrl",
    operation: "append",
  });

  const readTool = byName.get(READ_DOCX_TOOL_NAME) as Record<string, unknown> | undefined;
  assert.ok(readTool, "read tool is registered");
  assert.equal(readTool!.approval, undefined);
});

test("FeishuAdapter only registers docx tools when docs config is enabled", () => {
  const adapter = new FeishuAdapter();
  const getChannelTools = (adapter as unknown as { getChannelTools: () => Record<string, unknown>[] | null }).getChannelTools;

  Reflect.set(adapter as object, "loadedConfig", {
    ...validConfig(),
    feishu: {
      ...validConfig().feishu,
      tools: {
        docs: {
          enabled: false,
        },
      },
    },
  } satisfies FeishuConfig);
  const toolsWhenDisabled = getChannelTools.call(adapter) ?? [];
  assert.deepEqual(
    toolsWhenDisabled.map((tool) => String(tool.name ?? "")),
    ["FeishuSendFileToCurrentChat"],
  );

  Reflect.set(adapter as object, "loadedConfig", validConfig());
  const toolsWhenEnabled = getChannelTools.call(adapter) ?? [];
  assert.deepEqual(
    toolsWhenEnabled.map((tool) => String(tool.name ?? "")),
    [
      "FeishuSendFileToCurrentChat",
      CREATE_DOCX_TOOL_NAME,
      READ_DOCX_TOOL_NAME,
      APPEND_DOCX_TOOL_NAME,
      LIST_DOCX_BLOCKS_TOOL_NAME,
      GET_DOCX_BLOCK_TOOL_NAME,
      INSERT_DOCX_BLOCKS_TOOL_NAME,
      UPDATE_DOCX_BLOCKS_TOOL_NAME,
      DELETE_DOCX_BLOCKS_TOOL_NAME,
      UPDATE_DOCX_TITLE_TOOL_NAME,
      UPDATE_DOCX_CONTENT_TOOL_NAME,
      EMBED_DOCX_MEDIA_TOOL_NAME,
      LIST_DOCX_COMMENTS_TOOL_NAME,
      BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME,
      LIST_DOCX_COMMENT_REPLIES_TOOL_NAME,
      ADD_DOCX_COMMENT_TOOL_NAME,
      ADD_DOCX_COMMENT_REPLY_TOOL_NAME,
      RESOLVE_DOCX_COMMENT_TOOL_NAME,
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

test("extractDocxDocumentId accepts raw docx IDs and docx URLs", () => {
  assert.equal(extractDocxDocumentId(DOC_ID), DOC_ID);
  assert.equal(extractDocxDocumentId(`https://feishu.cn/docx/${DOC_ID}`), DOC_ID);
  assert.equal(extractDocxDocumentId(`https://larksuite.com/docx/${DOC_ID}?from=wiki`), DOC_ID);
  assert.equal(extractDocxDocumentId(LEGACY_DOC_ID), LEGACY_DOC_ID);
  assert.equal(extractDocxDocumentId(`https://feishu.cn/docx/${LEGACY_DOC_ID}`), LEGACY_DOC_ID);
});

test("extractDocxDocumentId rejects legacy /doc/ URLs with UnsupportedLegacyDoc", () => {
  assert.throws(
    () => extractDocxDocumentId(`https://example.feishu.cn/doc/doccnABCDEFGHIJKLMNOPQRSTUV`),
    (error: unknown) => {
      const record = error as { code?: string; message?: string };
      return record.code === "UnsupportedLegacyDoc" && typeof record.message === "string";
    },
  );
});

test("resolveDocxDocumentId resolves wiki node tokens to backing docx IDs", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;
  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
  });
  assert.equal(resolved, DOC_ID);
});

test("resolveDocxDocumentId rejects wiki nodes that are not backed by docx", async () => {
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: "shtcn123",
        objType: "sheet",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;
  await assert.rejects(
    async () =>
      await resolveDocxDocumentId({
        client,
        documentIdOrUrl: `https://feishu.cn/wiki/${WIKI_NODE_ID}`,
      }),
    (error: unknown) => String((error as Error).message).includes("not docx"),
  );
});

test("resolveDocxDocumentId resolves bare ambiguous tokens via wiki first", async () => {
  let calls = 0;
  const client = {
    async getWikiNode(nodeToken: string) {
      calls += 1;
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
  } as unknown as FeishuClient;

  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: WIKI_NODE_ID,
  });
  assert.equal(resolved, DOC_ID);
  assert.equal(calls, 1);
});

test("resolveDocxDocumentId falls back to bare docx ID when wiki lookup fails", async () => {
  let calls = 0;
  const client = {
    async getWikiNode(nodeToken: string) {
      calls += 1;
      assert.equal(nodeToken, DOC_ID);
      throw new Error("node not found");
    },
  } as unknown as FeishuClient;

  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: DOC_ID,
  });
  assert.equal(resolved, DOC_ID);
  assert.equal(calls, 1);
});

test("resolveDocxDocumentId keeps docx URL path and does not call wiki lookup", async () => {
  let calls = 0;
  const client = {
    async getWikiNode() {
      calls += 1;
      throw new Error("unexpected call");
    },
  } as unknown as FeishuClient;

  const resolved = await resolveDocxDocumentId({
    client,
    documentIdOrUrl: `https://feishu.cn/docx/${DOC_ID}`,
  });
  assert.equal(resolved, DOC_ID);
  assert.equal(calls, 0);
});

test("FeishuReadDocxContent accepts docx URLs and returns raw content", async () => {
  const client = {
    async getDocxRawContent(documentId: string) {
      return {
        documentId,
        content: "hello from docx",
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: READ_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: `https://feishu.cn/docx/${DOC_ID}`,
    },
    client,
  });

  assert.ok(result);
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredContent, {
    documentId: DOC_ID,
    content: "hello from docx",
    isEmpty: false,
  });
});

test("FeishuReadDocxContent accepts wiki URLs by resolving node metadata", async () => {
  const client = {
    async getWikiNode(_nodeToken: string) {
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken: WIKI_NODE_ID,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async getDocxRawContent(documentId: string) {
      return {
        documentId,
        content: "hello from wiki-backed docx",
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: READ_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
    },
    client,
  });

  assert.ok(result);
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredContent, {
    documentId: DOC_ID,
    content: "hello from wiki-backed docx",
    isEmpty: false,
  });
});

test("FeishuCreateDocxAndShareToCurrentChat supports wiki target", async () => {
  let updateTitleCalls = 0;
  let sentShareText = "";
  let createWikiNodeOptions:
    | {
        spaceId: string;
        parentNodeToken?: string;
        objType?: "docx";
        nodeType?: "origin" | "shortcut";
      }
    | undefined;
  const client = {
    async getWikiNode(nodeToken: string) {
      assert.equal(nodeToken, WIKI_NODE_ID);
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
      };
    },
    async createWikiNode(options: {
      spaceId: string;
      parentNodeToken?: string;
      objType?: "docx";
      nodeType?: "origin" | "shortcut";
    }) {
      createWikiNodeOptions = options;
      return {
        spaceId: WIKI_SPACE_ID,
        nodeToken: WIKI_NODE_ID,
        objToken: DOC_ID,
        objType: "docx",
        nodeType: "origin",
        parentNodeToken: WIKI_NODE_ID,
      };
    },
    async updateWikiNodeTitle(_spaceId: string, _nodeToken: string, _title: string) {
      updateTitleCalls += 1;
    },
    async createDocxBlocks() {
      return {
        documentId: DOC_ID,
        revisionId: 17,
        blocks: [{ blockId: "blk_1", blockType: 3 }],
      };
    },
    async sendTextMessage(_target: string, text: string) {
      sentShareText = text;
      return { messageId: "om_1", chatId: "oc_1" };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: CREATE_DOCX_TOOL_NAME,
    args: {
      title: "Wiki Launch Notes",
      wiki: {
        spaceIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
      },
      initialBlocks: [{ kind: "heading1", text: "Launch Notes" }],
    },
    channelTarget: "group:oc_123",
    client,
  });

  assert.deepEqual(createWikiNodeOptions, {
    spaceId: WIKI_SPACE_ID,
    parentNodeToken: WIKI_NODE_ID,
    objType: "docx",
    nodeType: "origin",
  });
  assert.equal(updateTitleCalls, 1);
  assert.match(sentShareText, /https:\/\/feishu\.cn\/wiki\//);
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredContent, {
    delivered: true,
    documentId: DOC_ID,
    revisionId: 17,
    title: "Wiki Launch Notes",
    url: `https://feishu.cn/wiki/${WIKI_NODE_ID}`,
    sharedToCurrentChat: true,
    appendedBlocks: [{ blockId: "blk_1", kind: "heading1", text: "Launch Notes" }],
    wiki: {
      spaceId: WIKI_SPACE_ID,
      nodeToken: WIKI_NODE_ID,
      parentNodeToken: WIKI_NODE_ID,
    },
  });
});

test("FeishuCreateDocxAndShareToCurrentChat preserves wiki resolution API errors", async () => {
  const permissionError = new FeishuApiError({
    kind: "permission",
    retryable: false,
    code: 131006,
    msg: "permission denied: no destination parent node permission",
    message: "permission denied: no destination parent node permission code=131006",
  });
  const client = {
    async getWikiNode() {
      throw permissionError;
    },
  } as unknown as FeishuClient;

  await assert.rejects(
    async () =>
      await maybeExecuteFeishuDocxToolCall({
        toolName: CREATE_DOCX_TOOL_NAME,
        args: {
          title: "Wiki Target",
          wiki: {
            spaceIdOrUrl: `https://example.feishu.cn/wiki/${WIKI_NODE_ID}`,
          },
        },
        channelTarget: "group:oc_123",
        client,
      }),
    (error: unknown) => error === permissionError,
  );
});

test("FeishuCreateDocxAndShareToCurrentChat rejects folderToken and wiki together", async () => {
  const client = {} as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: CREATE_DOCX_TOOL_NAME,
    args: {
      folderToken: "fldcn123",
      wiki: {
        spaceIdOrUrl: WIKI_SPACE_ID,
      },
    },
    channelTarget: "group:oc_123",
    client,
  });
  assert.deepEqual(result, {
    success: false,
    errorCode: "ConflictingTarget",
    errorMessage: "Provide either folderToken or wiki, not both.",
  });
});

test("FeishuAppendDocxContent appends blocks at the root using translated docx children", async () => {
  const blocks: FeishuSimpleBlock[] = [
    { kind: "heading1", text: "Release Notes" },
    { kind: "bullet", text: "Added docx tools" },
    { kind: "todo", text: "Write docs", checked: true },
    { kind: "code", text: "console.log('hi')", language: "typescript" },
    { kind: "divider" },
  ];
  let captured:
    | {
        documentId: string;
        blockId: string;
        options: {
          children: Record<string, unknown>[];
          documentRevisionId?: number;
          index?: number;
          clientToken?: string;
        };
      }
    | undefined;
  const client = {
    async createDocxBlocks(
      documentId: string,
      blockId: string,
      options: {
        children: Record<string, unknown>[];
        documentRevisionId?: number;
        index?: number;
        clientToken?: string;
      },
    ) {
      captured = {
        documentId,
        blockId,
        options,
      };
      return {
        documentId,
        revisionId: 7,
        blocks: options.children.map((child, index) => ({
          blockId: `blk_${index + 1}`,
          blockType: Number((child.block_type as number | undefined) ?? 0),
        })),
      };
    },
  } as unknown as FeishuClient;

  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: APPEND_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      blocks,
    },
    client,
  });

  assert.ok(captured);
  assert.equal(captured?.documentId, DOC_ID);
  assert.equal(captured?.blockId, DOC_ID);
  assert.equal(captured?.options.index, -1);
  assert.equal(captured?.options.documentRevisionId, -1);
  assert.equal(typeof captured?.options.clientToken, "string");
  assert.deepEqual(captured?.options.children, toFeishuDocxChildren(blocks));
  assert.equal(result?.success, true);
  assert.deepEqual(result?.structuredContent, {
    documentId: DOC_ID,
    revisionId: 7,
    appendedBlocks: [
      { blockId: "blk_1", kind: "heading1", text: "Release Notes" },
      { blockId: "blk_2", kind: "bullet", text: "Added docx tools" },
      { blockId: "blk_3", kind: "todo", text: "Write docs" },
      { blockId: "blk_4", kind: "code", text: "console.log('hi')" },
      { blockId: "blk_5", kind: "divider" },
    ],
  });
});

test("FeishuListDocxBlocks returns block page", async () => {
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async listDocxBlocks() {
      return {
        documentId: DOC_ID,
        items: [{ blockId: "blk_1", blockType: 2, textContent: "hello" }],
        nextPageToken: "next",
        hasMore: true,
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: LIST_DOCX_BLOCKS_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, pageSize: 20 },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal((result?.structuredContent as { items: unknown[] }).items.length, 1);
});

test("FeishuGetDocxBlock returns one block", async () => {
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async getDocxBlock(_documentId: string, blockId: string) {
      return { blockId, blockType: 2, textContent: "hello" };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: GET_DOCX_BLOCK_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, blockId: "blk_100" },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal((result?.structuredContent as { block: { blockId: string } }).block.blockId, "blk_100");
});

test("FeishuInsertDocxBlocks inserts blocks at explicit index", async () => {
  let capturedIndex = -99;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async createDocxBlocks(_docId: string, _parentId: string, options: { index?: number; children: unknown[] }) {
      capturedIndex = options.index ?? -99;
      return {
        documentId: DOC_ID,
        revisionId: 3,
        blocks: options.children.map((_, i) => ({ blockId: `blk_${i + 1}`, blockType: 2 })),
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: INSERT_DOCX_BLOCKS_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      parentBlockId: DOC_ID,
      index: 0,
      blocks: [{ kind: "paragraph", text: "inserted" }],
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(capturedIndex, 0);
});

test("FeishuUpdateDocxBlocks passes raw requests", async () => {
  let requestCount = 0;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async updateDocxBlocks(_documentId: string, requests: Record<string, unknown>[]) {
      requestCount = requests.length;
      return {
        documentId: DOC_ID,
        revisionId: 12,
        updatedBlocks: [{ blockId: "blk_1", blockType: 2 }],
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: UPDATE_DOCX_BLOCKS_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      requests: [{ block_id: "blk_1", replace_text: { elements: [] } }],
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(requestCount, 1);
});

test("FeishuDeleteDocxBlocks deletes child range", async () => {
  let deleted: [number, number] | undefined;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async deleteDocxBlockChildren(_documentId: string, _parent: string, startIndex: number, endIndex: number) {
      deleted = [startIndex, endIndex];
      return {
        documentId: DOC_ID,
        parentBlockId: DOC_ID,
        startIndex,
        endIndex,
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: DELETE_DOCX_BLOCKS_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, parentBlockId: DOC_ID, startIndex: 1, endIndex: 2 },
    client,
  });
  assert.equal(result?.success, true);
  assert.deepEqual(deleted, [1, 2]);
});

test("FeishuUpdateDocxTitle updates title block", async () => {
  let updateCalled = 0;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async updateDocxBlocks() {
      updateCalled += 1;
      return { documentId: DOC_ID, updatedBlocks: [] };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: UPDATE_DOCX_TITLE_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, title: "New Title" },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(updateCalled, 1);
});

test("FeishuUpdateDocxContent supports append mode", async () => {
  let appendCalled = 0;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async getDocxBlock() {
      return { blockId: DOC_ID, blockType: 1, children: [] };
    },
    async createDocxBlocks() {
      appendCalled += 1;
      return { documentId: DOC_ID, blocks: [{ blockId: "blk_1", blockType: 2 }] };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: UPDATE_DOCX_CONTENT_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, mode: "append", markdown: "hello" },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(appendCalled, 1);
});

test("FeishuUpdateDocxContent selectionByTitle uses block_type to find heading boundary", async () => {
  let deletedRange: [number, number] | undefined;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async getDocxBlock(_documentId: string, blockId: string) {
      if (blockId === DOC_ID) {
        return { blockId: DOC_ID, blockType: 1, children: ["h1", "p1", "p2", "h2", "p3"] };
      }
      if (blockId === "h1") {
        return { blockId: "h1", blockType: 3, textContent: "Section A" };
      }
      if (blockId === "h2") {
        return { blockId: "h2", blockType: 3, textContent: "Section B" };
      }
      return { blockId, blockType: 2, textContent: `${blockId} text` };
    },
    async deleteDocxBlockChildren(_documentId: string, _parentBlockId: string, startIndex: number, endIndex: number) {
      deletedRange = [startIndex, endIndex];
      return {
        documentId: DOC_ID,
        parentBlockId: DOC_ID,
        startIndex,
        endIndex,
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: UPDATE_DOCX_CONTENT_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      mode: "deleteRange",
      selectionByTitle: "Section A",
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.deepEqual(deletedRange, [0, 3]);
});

test("FeishuEmbedDocxMedia uploads and binds media", async () => {
  const temp = await import("node:fs/promises");
  const os = await import("node:os");
  const path = await import("node:path");
  const filePath = path.join(os.tmpdir(), `dotcraft-feishu-test-${Date.now()}.txt`);
  await temp.writeFile(filePath, "hello media");
  try {
    let uploaded = false;
    let updated = false;
    let uploadParams: Record<string, unknown> = {};
    const client = {
      async getWikiNode() {
        throw new Error("should not call");
      },
      async getDocxBlock() {
        return { blockId: DOC_ID, blockType: 1, children: [] };
      },
      async createDocxBlocks() {
        return { documentId: DOC_ID, blocks: [{ blockId: "blk_1", blockType: 27 }] };
      },
      async uploadDocxMedia(params: Record<string, unknown>) {
        uploaded = true;
        uploadParams = params;
        return { fileToken: "file_tok_1" };
      },
      async updateDocxBlocks() {
        updated = true;
        return { documentId: DOC_ID, updatedBlocks: [] };
      },
    } as unknown as FeishuClient;
    const result = await maybeExecuteFeishuDocxToolCall({
      toolName: EMBED_DOCX_MEDIA_TOOL_NAME,
      args: { documentIdOrUrl: DOC_ID, filePath, mediaType: "image" },
      client,
    });
    assert.equal(result?.success, true);
    assert.equal(uploaded, true);
    assert.equal(uploadParams.fileName, path.basename(filePath));
    assert.equal(Buffer.isBuffer(uploadParams.data), true);
    assert.equal((uploadParams.data as Buffer).toString("utf-8"), "hello media");
    assert.equal(uploadParams.mediaType, "text/plain");
    assert.equal(updated, true);
  } finally {
    await temp.unlink(filePath).catch(() => {});
  }
});

test("FeishuListDocxComments returns paged comment cards", async () => {
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async listDocxComments() {
      return {
        fileToken: DOC_ID,
        items: [{ commentId: "comment_1", replyList: { replies: [] } }],
        nextPageToken: "next",
        hasMore: true,
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: LIST_DOCX_COMMENTS_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, pageSize: 20 },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal((result?.structuredContent as { items: unknown[] }).items.length, 1);
});

test("FeishuBatchQueryDocxComments queries by commentIds", async () => {
  let captured: string[] = [];
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async batchQueryDocxComments(_options: { fileToken: string; commentIds: string[] }) {
      captured = _options.commentIds;
      return { fileToken: DOC_ID, items: [{ commentId: "comment_1", replyList: { replies: [] } }] };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, commentIds: ["comment_1"] },
    client,
  });
  assert.equal(result?.success, true);
  assert.deepEqual(captured, ["comment_1"]);
});

test("FeishuListDocxCommentReplies returns replies page", async () => {
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async listDocxCommentReplies() {
      return {
        fileToken: DOC_ID,
        commentId: "comment_1",
        items: [{ replyId: "reply_1" }],
        hasMore: false,
      };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: LIST_DOCX_COMMENT_REPLIES_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, commentId: "comment_1" },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal((result?.structuredContent as { items: Array<{ replyId: string }> }).items[0]?.replyId, "reply_1");
});

test("FeishuAddDocxComment creates full comment when no locator is provided", async () => {
  let capturedAnchor: string | undefined;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async createDocxComment(options: { anchorBlockId?: string }) {
      capturedAnchor = options.anchorBlockId;
      return { fileToken: DOC_ID, commentId: "comment_1" };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: ADD_DOCX_COMMENT_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      content: [{ type: "text", text: "hello" }],
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(capturedAnchor, undefined);
  assert.equal((result?.structuredContent as { commentMode: string }).commentMode, "full");
});

test("FeishuAddDocxComment resolves selectionWithEllipsis into anchor block id", async () => {
  let capturedAnchor = "";
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async getDocxBlock(_documentId: string, blockId: string) {
      if (blockId === DOC_ID) {
        return { blockId: DOC_ID, blockType: 1, children: ["blk_1"] };
      }
      return { blockId: "blk_1", blockType: 2, textContent: "hello world" };
    },
    async createDocxComment(options: { anchorBlockId?: string }) {
      capturedAnchor = String(options.anchorBlockId ?? "");
      return { fileToken: DOC_ID, commentId: "comment_1" };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: ADD_DOCX_COMMENT_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      selectionWithEllipsis: "hello",
      content: [{ type: "text", text: "local comment" }],
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(capturedAnchor, "blk_1");
  assert.equal((result?.structuredContent as { commentMode: string }).commentMode, "local");
});

test("FeishuAddDocxComment returns AmbiguousMatch when selection hits multiple blocks", async () => {
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async getDocxBlock(_documentId: string, blockId: string) {
      if (blockId === DOC_ID) {
        return { blockId: DOC_ID, blockType: 1, children: ["blk_1", "blk_2"] };
      }
      return { blockId, blockType: 2, textContent: "same needle" };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: ADD_DOCX_COMMENT_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      selectionWithEllipsis: "needle",
      content: [{ type: "text", text: "local comment" }],
    },
    client,
  });
  assert.deepEqual(result, {
    success: false,
    errorCode: "AmbiguousMatch",
    errorMessage: "Selection 'needle' matched 2 blocks: blk_1, blk_2. Narrow selectionWithEllipsis.",
  });
});

test("FeishuAddDocxCommentReply creates reply for replyable comment", async () => {
  let created = false;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async batchQueryDocxComments() {
      return { fileToken: DOC_ID, items: [{ commentId: "comment_1", isWhole: false, isSolved: false, replyList: { replies: [] } }] };
    },
    async createDocxCommentReply() {
      created = true;
      return { fileToken: DOC_ID, commentId: "comment_1", replyId: "reply_1" };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: ADD_DOCX_COMMENT_REPLY_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      commentId: "comment_1",
      content: [{ type: "text", text: "reply" }],
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(created, true);
});

test("FeishuAddDocxCommentReply rejects full comments", async () => {
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async batchQueryDocxComments() {
      return { fileToken: DOC_ID, items: [{ commentId: "comment_1", isWhole: true, isSolved: false, replyList: { replies: [] } }] };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: ADD_DOCX_COMMENT_REPLY_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      commentId: "comment_1",
      content: [{ type: "text", text: "reply" }],
    },
    client,
  });
  assert.deepEqual(result, {
    success: false,
    errorCode: "CommentNotReplyable",
    errorMessage: "Full comments do not support replies.",
  });
});

test("FeishuAddDocxCommentReply rejects solved comments", async () => {
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async batchQueryDocxComments() {
      return { fileToken: DOC_ID, items: [{ commentId: "comment_1", isWhole: false, isSolved: true, replyList: { replies: [] } }] };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: ADD_DOCX_COMMENT_REPLY_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      commentId: "comment_1",
      content: [{ type: "text", text: "reply" }],
    },
    client,
  });
  assert.deepEqual(result, {
    success: false,
    errorCode: "CommentNotReplyable",
    errorMessage: "Resolved comments do not support replies.",
  });
});

test("FeishuResolveDocxComment patches solved state", async () => {
  let patched: boolean | undefined;
  const client = {
    async getWikiNode() {
      throw new Error("should not call");
    },
    async patchDocxCommentSolved(options: { isSolved: boolean }) {
      patched = options.isSolved;
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: RESOLVE_DOCX_COMMENT_TOOL_NAME,
    args: { documentIdOrUrl: DOC_ID, commentId: "comment_1", isSolved: true },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(patched, true);
});

// --- Defensive validation tests ---

test("FeishuAppendDocxContent rejects empty text for paragraph", async () => {
  const client = {} as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: APPEND_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      blocks: [{ kind: "paragraph", text: "" }],
    },
    client,
  });
  assert.equal(result?.success, false);
  assert.equal(result?.errorCode, "InvalidBlocks");
  assert.match(result?.errorMessage ?? "", /must be a non-empty string/);
});

test("FeishuAppendDocxContent rejects whitespace-only text for bullet", async () => {
  const client = {} as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: APPEND_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      blocks: [{ kind: "bullet", text: "   " }],
    },
    client,
  });
  assert.equal(result?.success, false);
  assert.equal(result?.errorCode, "InvalidBlocks");
});

test("FeishuAppendDocxContent accepts divider without text", async () => {
  let createCalled = 0;
  const client = {
    async createDocxBlocks() {
      createCalled += 1;
      return { documentId: DOC_ID, revisionId: 1, blocks: [{ blockId: "blk_1", blockType: 22 }] };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: APPEND_DOCX_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      blocks: [{ kind: "divider" }],
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(createCalled, 1);
});

test("FeishuUpdateDocxBlocks rejects request missing block_id", async () => {
  const client = {} as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: UPDATE_DOCX_BLOCKS_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      requests: [{ replace_text: { elements: [] } }],
    },
    client,
  });
  assert.equal(result?.success, false);
  assert.equal(result?.errorCode, "InvalidRequest");
  assert.match(result?.errorMessage ?? "", /block_id/);
});

test("FeishuUpdateDocxBlocks rejects request with no valid operation", async () => {
  const client = {} as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: UPDATE_DOCX_BLOCKS_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      requests: [{ block_id: "blk_1", foo: "bar" }],
    },
    client,
  });
  assert.equal(result?.success, false);
  assert.equal(result?.errorCode, "InvalidRequest");
  assert.match(result?.errorMessage ?? "", /at least one valid operation/);
});

test("FeishuUpdateDocxBlocks accepts valid request with replace_text", async () => {
  let requestCalled = 0;
  const client = {
    async updateDocxBlocks() {
      requestCalled += 1;
      return { documentId: DOC_ID, revisionId: 1, updatedBlocks: [] };
    },
  } as unknown as FeishuClient;
  const result = await maybeExecuteFeishuDocxToolCall({
    toolName: UPDATE_DOCX_BLOCKS_TOOL_NAME,
    args: {
      documentIdOrUrl: DOC_ID,
      requests: [{ block_id: "blk_1", replace_text: { elements: [{ text_run: { content: "hi" } }] } }],
    },
    client,
  });
  assert.equal(result?.success, true);
  assert.equal(requestCalled, 1);
});
