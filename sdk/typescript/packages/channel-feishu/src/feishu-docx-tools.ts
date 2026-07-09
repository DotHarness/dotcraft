import { randomUUID } from "node:crypto";

import {
  mediaSourceFromToolPath,
  prepareMediaBytes,
  type ChannelToolDescriptor,
} from "@dotcraft/sdk/channel";

import type { FeishuClient } from "./feishu-client.js";
import type { FeishuConfig } from "./feishu-types.js";
import { extractWikiNodeToken, resolveWikiSpaceTarget } from "./feishu-wiki-tools.js";

export const CREATE_DOCX_TOOL_NAME = "FeishuCreateDocxAndShareToCurrentChat";
export const READ_DOCX_TOOL_NAME = "FeishuReadDocxContent";
export const APPEND_DOCX_TOOL_NAME = "FeishuAppendDocxContent";
export const LIST_DOCX_BLOCKS_TOOL_NAME = "FeishuListDocxBlocks";
export const GET_DOCX_BLOCK_TOOL_NAME = "FeishuGetDocxBlock";
export const INSERT_DOCX_BLOCKS_TOOL_NAME = "FeishuInsertDocxBlocks";
export const UPDATE_DOCX_BLOCKS_TOOL_NAME = "FeishuUpdateDocxBlocks";
export const DELETE_DOCX_BLOCKS_TOOL_NAME = "FeishuDeleteDocxBlocks";
export const UPDATE_DOCX_CONTENT_TOOL_NAME = "FeishuUpdateDocxContent";
export const UPDATE_DOCX_TITLE_TOOL_NAME = "FeishuUpdateDocxTitle";
export const EMBED_DOCX_MEDIA_TOOL_NAME = "FeishuEmbedDocxMedia";
export const LIST_DOCX_COMMENTS_TOOL_NAME = "FeishuListDocxComments";
export const BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME = "FeishuBatchQueryDocxComments";
export const LIST_DOCX_COMMENT_REPLIES_TOOL_NAME = "FeishuListDocxCommentReplies";
export const ADD_DOCX_COMMENT_TOOL_NAME = "FeishuAddDocxComment";
export const ADD_DOCX_COMMENT_REPLY_TOOL_NAME = "FeishuAddDocxCommentReply";
export const RESOLVE_DOCX_COMMENT_TOOL_NAME = "FeishuResolveDocxComment";

const DOCX_ID_PATTERN = /^[A-Za-z0-9]{16,40}$/;
const MEDIA_ALIGN_MAP = new Map<string, number>([
  ["left", 1],
  ["center", 2],
  ["right", 3],
]);
const FILE_VIEW_MAP = new Map<string, number>([
  ["card", 1],
  ["preview", 2],
  ["inline", 3],
]);
const SIMPLE_BLOCK_KINDS = [
  "paragraph",
  "heading1",
  "heading2",
  "bullet",
  "ordered",
  "todo",
  "quote",
  "code",
  "divider",
] as const;
const SIMPLE_BLOCK_KIND_SET = new Set<string>(SIMPLE_BLOCK_KINDS);
const CODE_LANGUAGE_IDS = new Map<string, number>([
  ["plain", 1],
  ["plaintext", 1],
  ["text", 1],
  ["abap", 2],
  ["ada", 3],
  ["apache", 4],
  ["apex", 5],
  ["asm", 6],
  ["assembly", 6],
  ["bash", 7],
  ["csharp", 8],
  ["c#", 8],
  ["cpp", 9],
  ["c++", 9],
  ["c", 10],
  ["cobol", 11],
  ["css", 12],
  ["coffeescript", 13],
  ["dart", 15],
  ["django", 17],
  ["dockerfile", 18],
  ["erlang", 19],
  ["fortran", 20],
  ["go", 22],
  ["groovy", 23],
  ["html", 24],
  ["http", 26],
  ["haskell", 27],
  ["json", 28],
  ["java", 29],
  ["javascript", 30],
  ["js", 30],
  ["julia", 31],
  ["kotlin", 32],
  ["latex", 33],
  ["lua", 36],
  ["matlab", 37],
  ["makefile", 38],
  ["markdown", 39],
  ["md", 39],
  ["nginx", 40],
  ["objective-c", 41],
  ["objc", 41],
  ["php", 43],
  ["perl", 44],
  ["powershell", 46],
  ["pwsh", 46],
  ["python", 49],
  ["py", 49],
  ["r", 50],
  ["ruby", 52],
  ["rb", 52],
  ["rust", 53],
  ["rs", 53],
  ["scss", 55],
  ["sql", 56],
  ["scala", 57],
  ["scheme", 58],
  ["shell", 60],
  ["sh", 60],
  ["swift", 61],
  ["thrift", 62],
  ["typescript", 63],
  ["ts", 63],
  ["vbscript", 64],
  ["xml", 66],
  ["yaml", 67],
  ["yml", 67],
  ["cmake", 68],
  ["diff", 69],
  ["gherkin", 70],
  ["graphql", 71],
  ["properties", 73],
  ["solidity", 74],
  ["toml", 75],
]);

export type FeishuSimpleBlockKind = (typeof SIMPLE_BLOCK_KINDS)[number];

export interface FeishuSimpleBlock {
  kind: FeishuSimpleBlockKind;
  text?: string;
  checked?: boolean;
  language?: string;
}

export interface FeishuDocxToolCallResult {
  success: boolean;
  errorCode?: string;
  errorMessage?: string;
  contentItems?: Record<string, unknown>[];
  structuredResult?: unknown;
  [key: string]: unknown;
}

type DocxUpdateMode =
  | "append"
  | "overwrite"
  | "replaceRange"
  | "replaceAll"
  | "insertBefore"
  | "insertAfter"
  | "deleteRange";

class DocxToolError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "DocxToolError";
    this.code = code;
  }
}

export function areFeishuDocxToolsEnabled(config: FeishuConfig | undefined): boolean {
  return config?.feishu.tools?.docs?.enabled === true;
}

export function getFeishuDocxChannelTools(enabled: boolean): ChannelToolDescriptor[] {
  if (!enabled) return [];
  const simpleBlockSchema = {
    type: "object",
    properties: {
      kind: { type: "string", enum: [...SIMPLE_BLOCK_KINDS] },
      text: { type: "string" },
      checked: { type: "boolean" },
      language: { type: "string" },
    },
    required: ["kind"],
  };

  return [
    {
      name: CREATE_DOCX_TOOL_NAME,
      description: "Create a Feishu docx document and share the link to the current Feishu chat. Provide either folderToken or wiki, not both.",
      requiresChatContext: true,
      display: {
        icon: "\u{1F4C4}",
        title: "Create Feishu docx and share",
      },
      inputSchema: {
        type: "object",
        properties: {
          title: { type: "string" },
          folderToken: { type: "string" },
          wiki: {
            type: "object",
            properties: {
              spaceIdOrUrl: { type: "string" },
              parentNodeTokenOrUrl: { type: "string" },
            },
            required: ["spaceIdOrUrl"],
          },
          initialBlocks: {
            type: "array",
            items: simpleBlockSchema,
          },
        },
        required: ["title"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "title",
        operation: "create",
      },
    },
    {
      name: READ_DOCX_TOOL_NAME,
      description: "Read the raw text content of a Feishu docx document.",
      display: {
        icon: "\u{1F50D}",
        title: "Read Feishu docx content",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
        },
        required: ["documentIdOrUrl"],
      },
    },
    {
      name: APPEND_DOCX_TOOL_NAME,
      description: "Append high-level text blocks to the root of a Feishu docx document.",
      display: {
        icon: "\u{270F}\u{FE0F}",
        title: "Append Feishu docx content",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          blocks: {
            type: "array",
            items: simpleBlockSchema,
          },
        },
        required: ["documentIdOrUrl", "blocks"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "append",
      },
    },
    {
      name: LIST_DOCX_BLOCKS_TOOL_NAME,
      description: "List blocks from a Feishu docx document.",
      display: {
        icon: "\u{1F4DC}",
        title: "List Feishu docx blocks",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          pageSize: { type: "integer" },
          pageToken: { type: "string" },
        },
        required: ["documentIdOrUrl"],
      },
    },
    {
      name: GET_DOCX_BLOCK_TOOL_NAME,
      description: "Get one block from a Feishu docx document.",
      display: {
        icon: "\u{1F50E}",
        title: "Get Feishu docx block",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          blockId: { type: "string" },
        },
        required: ["documentIdOrUrl", "blockId"],
      },
    },
    {
      name: INSERT_DOCX_BLOCKS_TOOL_NAME,
      description: "Insert children blocks under a parent docx block at index.",
      display: {
        icon: "\u{2795}",
        title: "Insert Feishu docx blocks",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          parentBlockId: { type: "string" },
          index: { type: "integer" },
          blocks: { type: "array", items: simpleBlockSchema },
        },
        required: ["documentIdOrUrl", "parentBlockId", "blocks"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
    {
      name: UPDATE_DOCX_BLOCKS_TOOL_NAME,
      description: "Batch update Feishu docx blocks with raw update requests.",
      display: {
        icon: "\u{1F527}",
        title: "Batch update Feishu docx blocks",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          requests: { type: "array", items: { type: "object" } },
        },
        required: ["documentIdOrUrl", "requests"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
    {
      name: DELETE_DOCX_BLOCKS_TOOL_NAME,
      description: "Delete child blocks by [startIndex, endIndex) under parent block.",
      display: {
        icon: "\u{1F5D1}\u{FE0F}",
        title: "Delete Feishu docx blocks",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          parentBlockId: { type: "string" },
          startIndex: { type: "integer" },
          endIndex: { type: "integer" },
        },
        required: ["documentIdOrUrl", "parentBlockId", "startIndex", "endIndex"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "delete",
      },
    },
    {
      name: UPDATE_DOCX_TITLE_TOOL_NAME,
      description: "Update Feishu docx title text only.",
      display: {
        icon: "\u{1F3F7}\u{FE0F}",
        title: "Update Feishu docx title",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          title: { type: "string" },
        },
        required: ["documentIdOrUrl", "title"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
    {
      name: UPDATE_DOCX_CONTENT_TOOL_NAME,
      description:
        "Update Feishu docx content with high-level modes: append/overwrite/replaceRange/replaceAll/insertBefore/insertAfter/deleteRange.",
      display: {
        icon: "\u{270D}\u{FE0F}",
        title: "Update Feishu docx content",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          mode: {
            type: "string",
            enum: ["append", "overwrite", "replaceRange", "replaceAll", "insertBefore", "insertAfter", "deleteRange"],
          },
          markdown: { type: "string" },
          selectionWithEllipsis: { type: "string" },
          selectionByTitle: { type: "string" },
          newTitle: { type: "string" },
        },
        required: ["documentIdOrUrl", "mode"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
    {
      name: EMBED_DOCX_MEDIA_TOOL_NAME,
      description: "Upload local image/file and embed it into Feishu docx.",
      display: {
        icon: "\u{1F4F7}",
        title: "Embed media into Feishu docx",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          filePath: { type: "string" },
          mediaType: { type: "string", enum: ["image", "file"] },
          selectionWithEllipsis: { type: "string" },
          before: { type: "boolean" },
          align: { type: "string", enum: ["left", "center", "right"] },
          caption: { type: "string" },
          fileView: { type: "string", enum: ["card", "preview", "inline"] },
        },
        required: ["documentIdOrUrl", "filePath"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
    {
      name: LIST_DOCX_COMMENTS_TOOL_NAME,
      description: "List comment cards from a Feishu docx document.",
      display: {
        icon: "\u{1F4AC}",
        title: "List Feishu docx comments",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          pageSize: { type: "integer" },
          pageToken: { type: "string" },
          isSolved: { type: "boolean" },
          isWhole: { type: "boolean" },
        },
        required: ["documentIdOrUrl"],
      },
    },
    {
      name: BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME,
      description: "Batch query Feishu docx comments by comment IDs.",
      display: {
        icon: "\u{1F4E6}",
        title: "Batch query Feishu comments",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          commentIds: { type: "array", items: { type: "string" } },
        },
        required: ["documentIdOrUrl", "commentIds"],
      },
    },
    {
      name: LIST_DOCX_COMMENT_REPLIES_TOOL_NAME,
      description: "List replies under one Feishu docx comment card.",
      display: {
        icon: "\u{1F4DD}",
        title: "List Feishu comment replies",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          commentId: { type: "string" },
          pageSize: { type: "integer" },
          pageToken: { type: "string" },
        },
        required: ["documentIdOrUrl", "commentId"],
      },
    },
    {
      name: ADD_DOCX_COMMENT_TOOL_NAME,
      description: "Create a full or local comment in Feishu docx.",
      display: {
        icon: "\u{1F58A}\u{FE0F}",
        title: "Add Feishu docx comment",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          content: {
            type: "array",
            items: {
              type: "object",
              properties: {
                type: { type: "string", enum: ["text", "mention_user", "link"] },
                text: { type: "string" },
                mention_user: { type: "string" },
                link: { type: "string" },
              },
              required: ["type"],
            },
          },
          selectionWithEllipsis: { type: "string" },
          blockId: { type: "string" },
        },
        required: ["documentIdOrUrl", "content"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
    {
      name: ADD_DOCX_COMMENT_REPLY_TOOL_NAME,
      description: "Add a reply to a Feishu docx comment.",
      display: {
        icon: "\u{21A9}\u{FE0F}",
        title: "Reply Feishu docx comment",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          commentId: { type: "string" },
          content: {
            type: "array",
            items: {
              type: "object",
              properties: {
                type: { type: "string", enum: ["text", "mention_user", "link"] },
                text: { type: "string" },
                mention_user: { type: "string" },
                link: { type: "string" },
              },
              required: ["type"],
            },
          },
        },
        required: ["documentIdOrUrl", "commentId", "content"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
    {
      name: RESOLVE_DOCX_COMMENT_TOOL_NAME,
      description: "Resolve or unresolve a Feishu docx comment.",
      display: {
        icon: "\u{2705}",
        title: "Resolve Feishu docx comment",
      },
      inputSchema: {
        type: "object",
        properties: {
          documentIdOrUrl: { type: "string" },
          commentId: { type: "string" },
          isSolved: { type: "boolean" },
        },
        required: ["documentIdOrUrl", "commentId", "isSolved"],
      },
      approval: {
        kind: "remoteResource",
        targetArgument: "documentIdOrUrl",
        operation: "write",
      },
    },
  ];
}

export async function maybeExecuteFeishuDocxToolCall(params: {
  toolName: string;
  args: Record<string, unknown>;
  channelTarget?: string;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult | null> {
  if (!isFeishuDocxToolName(params.toolName)) return null;

  try {
    if (params.toolName === CREATE_DOCX_TOOL_NAME) {
      return await executeCreateDocxTool(params);
    }
    if (params.toolName === READ_DOCX_TOOL_NAME) {
      return await executeReadDocxTool(params);
    }
    if (params.toolName === APPEND_DOCX_TOOL_NAME) {
      return await executeAppendDocxTool(params);
    }
    if (params.toolName === LIST_DOCX_BLOCKS_TOOL_NAME) {
      return await executeListDocxBlocksTool(params);
    }
    if (params.toolName === GET_DOCX_BLOCK_TOOL_NAME) {
      return await executeGetDocxBlockTool(params);
    }
    if (params.toolName === INSERT_DOCX_BLOCKS_TOOL_NAME) {
      return await executeInsertDocxBlocksTool(params);
    }
    if (params.toolName === UPDATE_DOCX_BLOCKS_TOOL_NAME) {
      return await executeUpdateDocxBlocksTool(params);
    }
    if (params.toolName === DELETE_DOCX_BLOCKS_TOOL_NAME) {
      return await executeDeleteDocxBlocksTool(params);
    }
    if (params.toolName === UPDATE_DOCX_TITLE_TOOL_NAME) {
      return await executeUpdateDocxTitleTool(params);
    }
    if (params.toolName === UPDATE_DOCX_CONTENT_TOOL_NAME) {
      return await executeUpdateDocxContentTool(params);
    }
    if (params.toolName === EMBED_DOCX_MEDIA_TOOL_NAME) {
      return await executeEmbedDocxMediaTool(params);
    }
    if (params.toolName === LIST_DOCX_COMMENTS_TOOL_NAME) {
      return await executeListDocxCommentsTool(params);
    }
    if (params.toolName === BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME) {
      return await executeBatchQueryDocxCommentsTool(params);
    }
    if (params.toolName === LIST_DOCX_COMMENT_REPLIES_TOOL_NAME) {
      return await executeListDocxCommentRepliesTool(params);
    }
    if (params.toolName === ADD_DOCX_COMMENT_TOOL_NAME) {
      return await executeAddDocxCommentTool(params);
    }
    if (params.toolName === ADD_DOCX_COMMENT_REPLY_TOOL_NAME) {
      return await executeAddDocxCommentReplyTool(params);
    }
    return await executeResolveDocxCommentTool(params);
  } catch (error) {
    if (error instanceof DocxToolError) {
      return {
        success: false,
        errorCode: error.code,
        errorMessage: error.message,
      };
    }
    throw error;
  }
}

export function extractDocxDocumentId(documentIdOrUrl: string): string {
  const normalized = documentIdOrUrl.trim();
  if (!normalized) {
    throw new DocxToolError("MissingDocumentId", "Feishu docx tools require a documentIdOrUrl.");
  }
  if (DOCX_ID_PATTERN.test(normalized)) {
    return normalized;
  }

  let parsedUrl: URL;
  try {
    parsedUrl = new URL(normalized);
  } catch {
    throw new DocxToolError(
      "InvalidDocumentId",
      "Provide a Feishu docx document_id or a docx URL such as https://feishu.cn/docx/<document_id>.",
    );
  }

  if (parsedUrl.pathname.toLowerCase().includes("/wiki/")) {
    throw new DocxToolError(
      "UnsupportedWikiUrl",
      "Provide a docx document_id or docx URL, not a Feishu wiki node URL.",
    );
  }

  const lowerPath = parsedUrl.pathname.toLowerCase();
  if (/\/doc\//.test(lowerPath) && !lowerPath.includes("/docx/")) {
    throw new DocxToolError(
      "UnsupportedLegacyDoc",
      "Legacy /doc/ URLs point to old-style documents not supported by the docx v1 API; open the file in Feishu and copy the /docx/ URL instead.",
    );
  }

  const segments = parsedUrl.pathname.split("/").filter(Boolean);
  const docxIndex = segments.findIndex((segment) => segment.toLowerCase() === "docx");
  const maybeId = docxIndex >= 0 ? segments[docxIndex + 1] : undefined;
  if (maybeId && DOCX_ID_PATTERN.test(maybeId)) {
    return maybeId;
  }

  throw new DocxToolError(
    "InvalidDocumentId",
    "Provide a Feishu docx document_id or a docx URL such as https://feishu.cn/docx/<document_id>.",
  );
}

export async function resolveDocxDocumentId(params: {
  client: FeishuClient;
  documentIdOrUrl: string;
}): Promise<string> {
  const normalized = params.documentIdOrUrl.trim();
  if (!normalized) {
    throw new DocxToolError("MissingDocumentId", "Feishu docx tools require a documentIdOrUrl.");
  }

  // Bare tokens are ambiguous because wiki node tokens and docx IDs share the same shape.
  // Prefer wiki resolution first, then fall back to raw docx ID if lookup fails.
  if (DOCX_ID_PATTERN.test(normalized) && !isAbsoluteUrl(normalized)) {
    try {
      const wikiNode = await params.client.getWikiNode(normalized);
      return docxDocumentIdFromWikiNode(wikiNode);
    } catch (error) {
      if (error instanceof DocxToolError) {
        throw error;
      }
      return normalized;
    }
  }

  try {
    return extractDocxDocumentId(normalized);
  } catch (error) {
    if (
      !(error instanceof DocxToolError) ||
      (error.code !== "InvalidDocumentId" && error.code !== "UnsupportedWikiUrl")
    ) {
      throw error;
    }
  }

  let wikiNodeToken: string;
  try {
    wikiNodeToken = extractWikiNodeToken(normalized);
  } catch {
    throw new DocxToolError(
      "InvalidDocumentId",
      "Provide a Feishu docx document_id or a docx URL such as https://feishu.cn/docx/<document_id>.",
    );
  }
  const wikiNode = await params.client.getWikiNode(wikiNodeToken);
  return docxDocumentIdFromWikiNode(wikiNode);
}

function isAbsoluteUrl(value: string): boolean {
  try {
    // Feishu IDs/tokens are not valid absolute URLs; URL parse success indicates URL-like input.
    new URL(value);
    return true;
  } catch {
    return false;
  }
}

function docxDocumentIdFromWikiNode(wikiNode: {
  nodeToken: string;
  objType: string;
  objToken: string;
}): string {
  if (wikiNode.objType !== "docx") {
    throw new DocxToolError(
      "UnsupportedDocxBacking",
      `Resolved wiki node '${wikiNode.nodeToken}' to obj_type '${wikiNode.objType}', not docx.`,
    );
  }
  const documentId = wikiNode.objToken.trim();
  if (!documentId) {
    throw new DocxToolError("InvalidDocumentId", "Resolved wiki node does not include a backing docx document_id.");
  }
  return documentId;
}

export function parseFeishuSimpleBlocks(value: unknown, argName: string): FeishuSimpleBlock[] {
  if (!Array.isArray(value) || value.length === 0) {
    throw new DocxToolError(
      "InvalidBlocks",
      `Feishu docx tool argument '${argName}' must be a non-empty array of simple blocks.`,
    );
  }
  if (value.length > 50) {
    throw new DocxToolError("TooManyBlocks", "Feishu docx append only supports up to 50 blocks per request.");
  }

  return value.map((item, index) => parseFeishuSimpleBlock(item, `${argName}[${index}]`));
}

export function toFeishuDocxChildren(blocks: FeishuSimpleBlock[]): Record<string, unknown>[] {
  return blocks.map((block) => toFeishuDocxBlock(block));
}

function isFeishuDocxToolName(toolName: string): boolean {
  return (
    toolName === CREATE_DOCX_TOOL_NAME ||
    toolName === READ_DOCX_TOOL_NAME ||
    toolName === APPEND_DOCX_TOOL_NAME ||
    toolName === LIST_DOCX_BLOCKS_TOOL_NAME ||
    toolName === GET_DOCX_BLOCK_TOOL_NAME ||
    toolName === INSERT_DOCX_BLOCKS_TOOL_NAME ||
    toolName === UPDATE_DOCX_BLOCKS_TOOL_NAME ||
    toolName === DELETE_DOCX_BLOCKS_TOOL_NAME ||
    toolName === UPDATE_DOCX_TITLE_TOOL_NAME ||
    toolName === UPDATE_DOCX_CONTENT_TOOL_NAME ||
    toolName === EMBED_DOCX_MEDIA_TOOL_NAME ||
    toolName === LIST_DOCX_COMMENTS_TOOL_NAME ||
    toolName === BATCH_QUERY_DOCX_COMMENTS_TOOL_NAME ||
    toolName === LIST_DOCX_COMMENT_REPLIES_TOOL_NAME ||
    toolName === ADD_DOCX_COMMENT_TOOL_NAME ||
    toolName === ADD_DOCX_COMMENT_REPLY_TOOL_NAME ||
    toolName === RESOLVE_DOCX_COMMENT_TOOL_NAME
  );
}

async function executeCreateDocxTool(params: {
  args: Record<string, unknown>;
  channelTarget?: string;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const target = params.channelTarget?.trim() ?? "";
  if (!target) {
    throw new DocxToolError(
      "MissingChatContext",
      "Current tool call does not contain a Feishu chat target for docx sharing.",
    );
  }

  const title = optionalText(params.args.title);
  const folderToken = optionalText(params.args.folderToken);
  const wiki = await resolveWikiCreateTarget({
    client: params.client,
    value: params.args.wiki,
  });
  if (folderToken && wiki) {
    throw new DocxToolError("ConflictingTarget", "Provide either folderToken or wiki, not both.");
  }
  const initialBlocksValue = params.args.initialBlocks;
  const initialBlocks = Array.isArray(initialBlocksValue) && initialBlocksValue.length > 0
    ? parseFeishuSimpleBlocks(initialBlocksValue, "initialBlocks")
    : [];

  let document:
    | {
        documentId: string;
        revisionId: number;
        title: string;
        url: string;
      }
    | undefined;
  let createdWikiNode:
    | {
        spaceId: string;
        nodeToken: string;
        parentNodeToken?: string;
      }
    | undefined;
  if (wiki) {
    const node = await params.client.createWikiNode({
      spaceId: wiki.spaceId,
      parentNodeToken: wiki.parentNodeToken,
      objType: "docx",
      nodeType: "origin",
    });
    if (!node.objToken) {
      throw new DocxToolError("MissingDocumentId", "Feishu wiki node creation did not include a backing docx token.");
    }
    if (title) {
      await params.client.updateWikiNodeTitle(node.spaceId, node.nodeToken, title);
    }
    const wikiDocUrl = buildWikiUrl(node.nodeToken);
    document = {
      documentId: node.objToken,
      revisionId: 0,
      title: title ?? node.title ?? "",
      url: wikiDocUrl,
    };
    createdWikiNode = {
      spaceId: node.spaceId,
      nodeToken: node.nodeToken,
      parentNodeToken: node.parentNodeToken,
    };
  } else {
    document = await params.client.createDocxDocument({
      title,
      folderToken,
    });
  }
  if (!document) {
    throw new DocxToolError("CreateFailed", "Failed to create Feishu docx document.");
  }

  let appendResult:
    | {
        blocks: Array<{ blockId?: string; kind: FeishuSimpleBlockKind; text?: string }>;
        revisionId?: number;
      }
    | undefined;
  if (initialBlocks.length > 0) {
    const appended = await params.client.createDocxBlocks(document.documentId, document.documentId, {
      children: toFeishuDocxChildren(initialBlocks),
      index: -1,
      documentRevisionId: -1,
      clientToken: randomUUID(),
    });
    appendResult = {
      blocks: summarizeBlocks(initialBlocks, appended.blocks),
      revisionId: appended.revisionId,
    };
  }

  const shareText = buildCreateShareText(document.title || title || document.documentId, document.url);
  await params.client.sendTextMessage(target, shareText);

  return {
    success: true,
    contentItems: [{ type: "text", text: `Created Feishu docx ${document.documentId}.` }],
    structuredResult: {
      delivered: true,
      documentId: document.documentId,
      revisionId: appendResult?.revisionId ?? document.revisionId,
      title: document.title,
      url: document.url,
      sharedToCurrentChat: true,
      appendedBlocks: appendResult?.blocks ?? [],
      ...(createdWikiNode ? { wiki: createdWikiNode } : {}),
    },
  };
}

async function executeReadDocxTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const result = await params.client.getDocxRawContent(documentId);
  const readableText = result.content || "(empty document)";
  return {
    success: true,
    contentItems: [{ type: "text", text: readableText }],
    structuredResult: {
      documentId: result.documentId,
      content: result.content,
      isEmpty: result.content.length === 0,
    },
  };
}

async function executeAppendDocxTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const blocks = parseFeishuSimpleBlocks(params.args.blocks, "blocks");
  const result = await params.client.createDocxBlocks(documentId, documentId, {
    children: toFeishuDocxChildren(blocks),
    index: -1,
    documentRevisionId: -1,
    clientToken: randomUUID(),
  });
  const summary = summarizeBlocks(blocks, result.blocks);
  return {
    success: true,
    contentItems: [{ type: "text", text: `Appended ${summary.length} block(s) to Feishu docx ${documentId}.` }],
    structuredResult: {
      documentId,
      revisionId: result.revisionId,
      appendedBlocks: summary,
    },
  };
}

async function executeListDocxBlocksTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const page = await params.client.listDocxBlocks({
    documentId,
    pageSize: optionalInteger(params.args.pageSize, "pageSize"),
    pageToken: optionalText(params.args.pageToken),
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `Listed ${page.items.length} block(s) from docx ${documentId}.` }],
    structuredResult: {
      documentId,
      items: page.items,
      nextPageToken: page.nextPageToken,
      hasMore: page.hasMore,
    },
  };
}

async function executeGetDocxBlockTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const blockId = requiredText(params.args.blockId, "blockId");
  const block = await params.client.getDocxBlock(documentId, blockId);
  return {
    success: true,
    contentItems: [{ type: "text", text: `Loaded docx block ${blockId}.` }],
    structuredResult: {
      documentId,
      block,
    },
  };
}

async function executeInsertDocxBlocksTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const parentBlockId = requiredText(params.args.parentBlockId, "parentBlockId");
  const blocks = parseFeishuSimpleBlocks(params.args.blocks, "blocks");
  const result = await params.client.createDocxBlocks(documentId, parentBlockId, {
    children: toFeishuDocxChildren(blocks),
    index: optionalIntegerAllowNegativeOne(params.args.index, "index") ?? -1,
    documentRevisionId: -1,
    clientToken: randomUUID(),
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `Inserted ${result.blocks.length} block(s) into docx ${documentId}.` }],
    structuredResult: {
      documentId,
      parentBlockId,
      revisionId: result.revisionId,
      blocks: result.blocks,
    },
  };
}

async function executeUpdateDocxBlocksTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const requests = parseObjectArray(params.args.requests, "requests");

  // Guard: validate each request has block_id and at least one valid operation.
  // Feishu returns 1770001 (param is invalid) for malformed batch_update requests.
  const VALID_UPDATE_OPS = [
    "update_text_elements",
    "update_text_style",
    "replace_text",
    "insert_table_row",
    "delete_table_row",
    "insert_table_column",
    "delete_table_column",
    "merge_table_cells",
    "unmerge_table_cells",
    "update_table_property",
    "update_table_column",
    "update_table_cell",
    "update_table_border",
    "update_table_cell_border",
  ];
  for (let i = 0; i < requests.length; i++) {
    const req = requests[i] as Record<string, unknown>;
    if (!req.block_id || typeof req.block_id !== "string") {
      throw new DocxToolError(
        "InvalidRequest",
        `requests[${i}].block_id is required and must be a non-empty string.`,
      );
    }
    const hasOp = VALID_UPDATE_OPS.some((op) => req[op] !== undefined);
    if (!hasOp) {
      throw new DocxToolError(
        "InvalidRequest",
        `requests[${i}] must contain at least one valid operation field. ` +
          `Valid operations: ${VALID_UPDATE_OPS.join(", ")}. ` +
          `Received keys: ${Object.keys(req).join(", ")}`,
      );
    }
  }

  const result = await params.client.updateDocxBlocks(documentId, requests);
  return {
    success: true,
    contentItems: [{ type: "text", text: `Updated ${requests.length} block request(s) in docx ${documentId}.` }],
    structuredResult: result,
  };
}

async function executeDeleteDocxBlocksTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const parentBlockId = requiredText(params.args.parentBlockId, "parentBlockId");
  const startIndex = requiredInteger(params.args.startIndex, "startIndex");
  const endIndex = requiredInteger(params.args.endIndex, "endIndex");
  const result = await params.client.deleteDocxBlockChildren(documentId, parentBlockId, startIndex, endIndex);
  return {
    success: true,
    contentItems: [{ type: "text", text: `Deleted child blocks [${startIndex}, ${endIndex}) from ${parentBlockId}.` }],
    structuredResult: result,
  };
}

async function executeUpdateDocxTitleTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const title = requiredText(params.args.title, "title");
  await updateDocxTitle(params.client, documentId, title);
  return {
    success: true,
    contentItems: [{ type: "text", text: `Updated docx ${documentId} title.` }],
    structuredResult: {
      documentId,
      title,
    },
  };
}

async function executeUpdateDocxContentTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const mode = requiredMode(params.args.mode);
  const markdown = optionalText(params.args.markdown) ?? "";
  const selectionWithEllipsis = optionalText(params.args.selectionWithEllipsis);
  const selectionByTitle = optionalText(params.args.selectionByTitle);
  const newTitle = optionalText(params.args.newTitle);
  if (selectionWithEllipsis && selectionByTitle) {
    throw new DocxToolError(
      "InvalidArguments",
      "FeishuUpdateDocxContent requires either selectionWithEllipsis or selectionByTitle, not both.",
    );
  }
  const selectionRequired = new Set(["replaceRange", "replaceAll", "insertBefore", "insertAfter", "deleteRange"]);
  if (selectionRequired.has(mode) && !selectionWithEllipsis && !selectionByTitle) {
    throw new DocxToolError("InvalidArguments", `FeishuUpdateDocxContent mode '${mode}' requires a selection.`);
  }
  if (mode !== "deleteRange" && !markdown) {
    throw new DocxToolError("InvalidArguments", `FeishuUpdateDocxContent mode '${mode}' requires non-empty markdown.`);
  }

  const warnings = collectUpdateWarnings(mode, markdown);
  const root = await params.client.getDocxBlock(documentId, documentId);
  const rootChildren = root.children ?? [];
  const blocks = markdown ? parseMarkdownToSimpleBlocks(markdown) : [];
  const convertedChildren = blocks.length > 0 ? toFeishuDocxChildren(blocks) : [];
  let operationResult: Record<string, unknown> = {};

  if (mode === "append") {
    const inserted = await params.client.createDocxBlocks(documentId, documentId, {
      children: convertedChildren,
      index: -1,
      documentRevisionId: -1,
      clientToken: randomUUID(),
    });
    operationResult = { appendedBlocks: inserted.blocks, revisionId: inserted.revisionId };
  } else if (mode === "overwrite") {
    if (rootChildren.length > 0) {
      await params.client.deleteDocxBlockChildren(documentId, documentId, 0, rootChildren.length);
    }
    const inserted = await params.client.createDocxBlocks(documentId, documentId, {
      children: convertedChildren,
      index: 0,
      documentRevisionId: -1,
      clientToken: randomUUID(),
    });
    warnings.push("overwrite mode may remove media/comments from previous content.");
    operationResult = { insertedBlocks: inserted.blocks, revisionId: inserted.revisionId };
  } else {
    const locate = await locateSelectionRange({
      client: params.client,
      documentId,
      selectionWithEllipsis,
      selectionByTitle,
      rootChildren,
    });
    if (mode === "insertBefore" || mode === "insertAfter") {
      const insertIndex = mode === "insertBefore" ? locate.startIndex : locate.endIndex;
      const inserted = await params.client.createDocxBlocks(documentId, documentId, {
        children: convertedChildren,
        index: insertIndex,
        documentRevisionId: -1,
        clientToken: randomUUID(),
      });
      operationResult = { insertedBlocks: inserted.blocks, insertIndex, revisionId: inserted.revisionId };
    } else if (mode === "deleteRange") {
      await params.client.deleteDocxBlockChildren(documentId, documentId, locate.startIndex, locate.endIndex);
      operationResult = { deletedRange: locate };
    } else {
      await params.client.deleteDocxBlockChildren(documentId, documentId, locate.startIndex, locate.endIndex);
      if (convertedChildren.length > 0) {
        const inserted = await params.client.createDocxBlocks(documentId, documentId, {
          children: convertedChildren,
          index: locate.startIndex,
          documentRevisionId: -1,
          clientToken: randomUUID(),
        });
        operationResult = {
          replacedRange: locate,
          insertedBlocks: inserted.blocks,
          revisionId: inserted.revisionId,
        };
      } else {
        operationResult = { replacedRange: locate, insertedBlocks: [] };
      }
    }
  }

  if (newTitle) {
    await updateDocxTitle(params.client, documentId, newTitle);
  }

  return {
    success: true,
    contentItems: [{ type: "text", text: `Updated Feishu docx ${documentId} using mode ${mode}.` }],
    structuredResult: {
      documentId,
      mode,
      warnings,
      ...(newTitle ? { newTitle } : {}),
      ...operationResult,
    },
  };
}

async function executeEmbedDocxMediaTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const filePath = requiredText(params.args.filePath, "filePath");
  const mediaType = optionalText(params.args.mediaType) === "file" ? "file" : "image";
  const selectionWithEllipsis = optionalText(params.args.selectionWithEllipsis);
  const before = optionalBoolean(params.args.before) ?? false;
  if (before && !selectionWithEllipsis) {
    throw new DocxToolError("InvalidArguments", "FeishuEmbedDocxMedia 'before' requires selectionWithEllipsis.");
  }
  const align = optionalText(params.args.align);
  const fileView = optionalText(params.args.fileView);
  const caption = optionalText(params.args.caption);
  const preparedMedia = await prepareMediaBytes(
    mediaSourceFromToolPath(filePath, { fieldName: "filePath", errorFactory: docxToolError }),
    {
      fallbackFileName: mediaType === "file" ? "attachment" : "image",
      errorFactory: docxToolError,
    },
  );

  const root = await params.client.getDocxBlock(documentId, documentId);
  const rootChildren = root.children ?? [];
  let insertIndex = rootChildren.length;
  if (selectionWithEllipsis) {
    const locate = await locateSelectionRange({
      client: params.client,
      documentId,
      selectionWithEllipsis,
      rootChildren,
    });
    insertIndex = before ? locate.startIndex : locate.endIndex;
  }

  const createChild = mediaType === "file"
    ? { block_type: 23, file: buildFileCreateOptions(fileView) }
    : { block_type: 27, image: {} };
  const inserted = await params.client.createDocxBlocks(documentId, documentId, {
    children: [createChild],
    index: insertIndex,
    documentRevisionId: -1,
    clientToken: randomUUID(),
  });
  const createdBlock = inserted.blocks[0];
  if (!createdBlock?.blockId) {
    throw new DocxToolError("EmbedMediaFailed", "Feishu docx media embed failed to create placeholder block.");
  }

  let rollbackError: string | undefined;
  try {
    const uploaded = await params.client.uploadDocxMedia({
      fileName: preparedMedia.fileName,
      data: preparedMedia.bytes,
      mediaType: preparedMedia.mediaType,
      parentType: mediaType === "file" ? "docx_file" : "docx_image",
      parentNode: createdBlock.blockId,
      documentId,
    });
    const request = mediaType === "file"
      ? {
          block_id: createdBlock.blockId,
          replace_file: {
            token: uploaded.fileToken,
          },
        }
      : {
          block_id: createdBlock.blockId,
          replace_image: {
            token: uploaded.fileToken,
            ...(align && MEDIA_ALIGN_MAP.has(align) ? { align: MEDIA_ALIGN_MAP.get(align) } : {}),
            ...(caption ? { caption: { content: caption } } : {}),
          },
        };
    await params.client.updateDocxBlocks(documentId, [request]);
    return {
      success: true,
      contentItems: [{ type: "text", text: `Embedded ${mediaType} into docx ${documentId}.` }],
      structuredResult: {
        documentId,
        mediaType,
        filePath,
        fileName: preparedMedia.fileName,
        blockId: createdBlock.blockId,
        insertIndex,
        fileToken: uploaded.fileToken,
      },
    };
  } catch (error) {
    try {
      await params.client.deleteDocxBlockChildren(documentId, documentId, insertIndex, insertIndex + 1);
    } catch (rollback) {
      rollbackError = String((rollback as Error).message ?? rollback);
    }
    throw new DocxToolError(
      "EmbedMediaFailed",
      `${error instanceof Error ? error.message : String(error)}${rollbackError ? ` (rollback failed: ${rollbackError})` : ""}`,
    );
  }
}

async function executeListDocxCommentsTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const page = await params.client.listDocxComments({
    fileToken: documentId,
    pageSize: optionalInteger(params.args.pageSize, "pageSize"),
    pageToken: optionalText(params.args.pageToken),
    isSolved: optionalBoolean(params.args.isSolved),
    isWhole: optionalBoolean(params.args.isWhole),
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `Listed ${page.items.length} comment card(s) from docx ${documentId}.` }],
    structuredResult: page,
  };
}

async function executeBatchQueryDocxCommentsTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const commentIds = requiredStringArray(params.args.commentIds, "commentIds");
  const result = await params.client.batchQueryDocxComments({
    fileToken: documentId,
    commentIds,
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `Loaded ${result.items.length} comment card(s) by ID from docx ${documentId}.` }],
    structuredResult: result,
  };
}

async function executeListDocxCommentRepliesTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const commentId = requiredText(params.args.commentId, "commentId");
  const page = await params.client.listDocxCommentReplies({
    fileToken: documentId,
    commentId,
    pageSize: optionalInteger(params.args.pageSize, "pageSize"),
    pageToken: optionalText(params.args.pageToken),
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `Listed ${page.items.length} reply item(s) from comment ${commentId}.` }],
    structuredResult: page,
  };
}

async function executeAddDocxCommentTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const selectionWithEllipsis = optionalText(params.args.selectionWithEllipsis);
  const blockId = optionalText(params.args.blockId);
  if (selectionWithEllipsis && blockId) {
    throw new DocxToolError("InvalidArguments", "Provide either selectionWithEllipsis or blockId, not both.");
  }
  const replyElements = parseCommentReplyElements(params.args.content, "content");
  let anchorBlockId = blockId;
  let selectionSource: "blockId" | "selectionWithEllipsis" | "fullComment" = "fullComment";
  if (!anchorBlockId && selectionWithEllipsis) {
    const root = await params.client.getDocxBlock(documentId, documentId);
    const rootChildren = root.children ?? [];
    anchorBlockId = await locateCommentAnchorBlockId({
      client: params.client,
      documentId,
      rootChildren,
      selectionWithEllipsis,
    });
    selectionSource = "selectionWithEllipsis";
  } else if (anchorBlockId) {
    selectionSource = "blockId";
  }
  const result = await params.client.createDocxComment({
    fileToken: documentId,
    replyElements,
    anchorBlockId,
  });

  return {
    success: true,
    contentItems: [
      {
        type: "text",
        text: anchorBlockId
          ? `Created local comment ${result.commentId} in docx ${documentId}.`
          : `Created full comment ${result.commentId} in docx ${documentId}.`,
      },
    ],
    structuredResult: {
      ...result,
      commentMode: anchorBlockId ? "local" : "full",
      ...(anchorBlockId ? { anchorBlockId } : {}),
      selectionSource,
      ...(selectionWithEllipsis ? { selectionWithEllipsis } : {}),
    },
  };
}

async function executeAddDocxCommentReplyTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const commentId = requiredText(params.args.commentId, "commentId");
  const replyElements = parseCommentReplyElements(params.args.content, "content");
  const target = await params.client.batchQueryDocxComments({
    fileToken: documentId,
    commentIds: [commentId],
  });
  const comment = target.items.find((item) => item.commentId === commentId);
  if (!comment) {
    throw new DocxToolError("CommentNotFound", `Cannot find comment '${commentId}' in docx ${documentId}.`);
  }
  if (comment.isWhole === true) {
    throw new DocxToolError("CommentNotReplyable", "Full comments do not support replies.");
  }
  if (comment.isSolved === true) {
    throw new DocxToolError("CommentNotReplyable", "Resolved comments do not support replies.");
  }
  const result = await params.client.createDocxCommentReply({
    fileToken: documentId,
    commentId,
    replyElements,
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `Added reply ${result.replyId} to comment ${commentId}.` }],
    structuredResult: result,
  };
}

async function executeResolveDocxCommentTool(params: {
  args: Record<string, unknown>;
  client: FeishuClient;
}): Promise<FeishuDocxToolCallResult> {
  const documentId = await resolveDocxDocumentId({
    client: params.client,
    documentIdOrUrl: requiredText(params.args.documentIdOrUrl, "documentIdOrUrl"),
  });
  const commentId = requiredText(params.args.commentId, "commentId");
  const isSolved = optionalBoolean(params.args.isSolved);
  if (isSolved === undefined) {
    throw new DocxToolError("InvalidArguments", "FeishuResolveDocxComment requires boolean 'isSolved'.");
  }
  await params.client.patchDocxCommentSolved({
    fileToken: documentId,
    commentId,
    isSolved,
  });
  return {
    success: true,
    contentItems: [{ type: "text", text: `${isSolved ? "Resolved" : "Reopened"} comment ${commentId}.` }],
    structuredResult: {
      fileToken: documentId,
      commentId,
      isSolved,
    },
  };
}

function parseFeishuSimpleBlock(value: unknown, path: string): FeishuSimpleBlock {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new DocxToolError("InvalidBlocks", `${path} must be an object.`);
  }
  const record = value as Record<string, unknown>;
  const kind = String(record.kind ?? "").trim();
  if (!SIMPLE_BLOCK_KIND_SET.has(kind)) {
    throw new DocxToolError(
      "InvalidBlocks",
      `${path}.kind must be one of: ${SIMPLE_BLOCK_KINDS.join(", ")}.`,
    );
  }

  // Guard: reject empty/whitespace-only text for non-divider blocks.
  // Feishu returns 99992402 (field validation failed) for empty children text.
  if (kind !== "divider") {
    const rawText = record.text;
    if (rawText == null || (typeof rawText === "string" && rawText.trim() === "")) {
      throw new DocxToolError(
        "InvalidBlocks",
        `${path}.text must be a non-empty string for kind "${kind}". ` +
          `Empty or whitespace-only text is rejected by Feishu (error 99992402). ` +
          `Use a divider block if you need a visual separator.`,
      );
    }
  }

  return {
    kind: kind as FeishuSimpleBlockKind,
    text: optionalText(record.text),
    checked: typeof record.checked === "boolean" ? record.checked : undefined,
    language: optionalText(record.language),
  };
}

function toFeishuDocxBlock(block: FeishuSimpleBlock): Record<string, unknown> {
  const textElements = buildTextElements(block.text ?? "");
  switch (block.kind) {
    case "paragraph":
      return { block_type: 2, text: { elements: textElements } };
    case "heading1":
      return { block_type: 3, heading1: { elements: textElements } };
    case "heading2":
      return { block_type: 4, heading2: { elements: textElements } };
    case "bullet":
      return { block_type: 12, bullet: { elements: textElements } };
    case "ordered":
      return { block_type: 13, ordered: { elements: textElements } };
    case "todo":
      return { block_type: 17, todo: { elements: textElements, style: { done: block.checked === true } } };
    case "quote":
      return { block_type: 15, quote: { elements: textElements } };
    case "code":
      return {
        block_type: 14,
        code: {
          elements: textElements,
          style: {
            language: resolveCodeLanguageId(block.language),
            wrap: false,
          },
        },
      };
    case "divider":
      return { block_type: 22, divider: {} };
  }
}

function buildTextElements(text: string): Record<string, unknown>[] {
  return [
    {
      text_run: {
        content: text,
      },
    },
  ];
}

function resolveCodeLanguageId(language: string | undefined): number {
  const normalized = (language ?? "").trim().toLowerCase();
  if (!normalized) return 1;
  return CODE_LANGUAGE_IDS.get(normalized) ?? 1;
}

function summarizeBlocks(
  requestedBlocks: FeishuSimpleBlock[],
  createdBlocks: Array<{ blockId: string; blockType: number }>,
): Array<{ blockId?: string; kind: FeishuSimpleBlockKind; text?: string }> {
  return requestedBlocks.map((block, index) => ({
    blockId: createdBlocks[index]?.blockId,
    kind: block.kind,
    ...(block.text ? { text: block.text.slice(0, 120) } : {}),
  }));
}

function buildCreateShareText(title: string, url: string): string {
  return `Created Feishu docx: ${title}\n${url}`;
}

function buildWikiUrl(nodeToken: string): string {
  return `https://feishu.cn/wiki/${nodeToken}`;
}

function requiredText(value: unknown, fieldName: string): string {
  const text = String(value ?? "").trim();
  if (!text) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool requires a non-empty '${fieldName}'.`);
  }
  return text;
}

function docxToolError(code: string, message: string): DocxToolError {
  return new DocxToolError(code, message);
}

function optionalText(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

function requiredInteger(value: unknown, fieldName: string): number {
  const parsed = Number(value);
  if (!Number.isInteger(parsed)) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool '${fieldName}' must be an integer.`);
  }
  return parsed;
}

function optionalInteger(value: unknown, fieldName: string): number | undefined {
  if (value == null) return undefined;
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed <= 0) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool '${fieldName}' must be a positive integer.`);
  }
  return parsed;
}

function optionalIntegerAllowNegativeOne(value: unknown, fieldName: string): number | undefined {
  if (value == null) return undefined;
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < -1) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool '${fieldName}' must be an integer >= -1.`);
  }
  return parsed;
}

function optionalBoolean(value: unknown): boolean | undefined {
  if (value == null) return undefined;
  if (typeof value !== "boolean") {
    throw new DocxToolError("InvalidArguments", "Feishu docx tool boolean argument must be a boolean.");
  }
  return value;
}

function parseObjectArray(value: unknown, fieldName: string): Record<string, unknown>[] {
  if (!Array.isArray(value) || value.length === 0) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool '${fieldName}' must be a non-empty array.`);
  }
  return value.map((item, index) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) {
      throw new DocxToolError("InvalidArguments", `${fieldName}[${index}] must be an object.`);
    }
    return item as Record<string, unknown>;
  });
}

function requiredStringArray(value: unknown, fieldName: string): string[] {
  if (!Array.isArray(value) || value.length === 0) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool '${fieldName}' must be a non-empty string array.`);
  }
  return value.map((item, index) => {
    const parsed = String(item ?? "").trim();
    if (!parsed) {
      throw new DocxToolError("InvalidArguments", `${fieldName}[${index}] must be a non-empty string.`);
    }
    return parsed;
  });
}

function parseCommentReplyElements(value: unknown, fieldName: string): Record<string, unknown>[] {
  if (!Array.isArray(value) || value.length === 0) {
    throw new DocxToolError("InvalidArguments", `Feishu docx tool '${fieldName}' must be a non-empty array.`);
  }
  return value.map((item, index) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) {
      throw new DocxToolError("InvalidArguments", `${fieldName}[${index}] must be an object.`);
    }
    const record = item as Record<string, unknown>;
    const type = String(record.type ?? "").trim();
    if (!type) {
      throw new DocxToolError("InvalidArguments", `${fieldName}[${index}].type is required.`);
    }
    if (type === "text") {
      const text = String(record.text ?? "");
      if (!text.trim()) {
        throw new DocxToolError("InvalidArguments", `${fieldName}[${index}] type=text requires non-empty text.`);
      }
      if (text.length > 1000) {
        throw new DocxToolError("InvalidArguments", `${fieldName}[${index}] text exceeds 1000 characters.`);
      }
      return { type: "text", text };
    }
    if (type === "mention_user") {
      const mentionUser = optionalText(record.mention_user) ?? optionalText(record.text);
      if (!mentionUser) {
        throw new DocxToolError(
          "InvalidArguments",
          `${fieldName}[${index}] type=mention_user requires mention_user or text.`,
        );
      }
      return { type: "mention_user", mention_user: mentionUser };
    }
    if (type === "link") {
      const link = optionalText(record.link) ?? optionalText(record.text);
      if (!link) {
        throw new DocxToolError("InvalidArguments", `${fieldName}[${index}] type=link requires link or text.`);
      }
      return { type: "link", link };
    }
    throw new DocxToolError(
      "InvalidArguments",
      `${fieldName}[${index}].type must be one of: text, mention_user, link.`,
    );
  });
}

function requiredMode(value: unknown): DocxUpdateMode {
  const normalized = String(value ?? "").trim();
  const allowed = new Set([
    "append",
    "overwrite",
    "replaceRange",
    "replaceAll",
    "insertBefore",
    "insertAfter",
    "deleteRange",
  ]);
  if (!allowed.has(normalized)) {
    throw new DocxToolError(
      "InvalidArguments",
      "FeishuUpdateDocxContent 'mode' must be one of: append, overwrite, replaceRange, replaceAll, insertBefore, insertAfter, deleteRange.",
    );
  }
  return normalized as DocxUpdateMode;
}

function collectUpdateWarnings(
  mode: DocxUpdateMode,
  markdown: string,
): string[] {
  const warnings: string[] = [];
  if ((mode === "replaceRange" || mode === "replaceAll") && /\n\s*\n/.test(markdown)) {
    warnings.push(
      "replaceRange/replaceAll cannot split one existing block into multiple paragraph blocks; blank lines may render as literal line breaks.",
    );
  }
  const combinedEmphasisPatterns = [
    /\*\*\*[^*]+\*\*\*/,
    /___[^_]+___/,
    /\*\*_[^_]+_\*\*/,
    /__\*[^*]+\*__/,
    /_\*\*[^*]+\*\*_/,
    /\*__[^_]+__\*/,
  ];
  if (combinedEmphasisPatterns.some((pattern) => pattern.test(markdown))) {
    warnings.push("Combined bold+italic markdown may be downgraded by Feishu rendering.");
  }
  return warnings;
}

function parseMarkdownToSimpleBlocks(markdown: string): FeishuSimpleBlock[] {
  const normalized = markdown.replace(/\r\n/g, "\n");
  const lines = normalized.split("\n");
  const blocks: FeishuSimpleBlock[] = [];
  let inCode = false;
  let codeLanguage = "";
  let codeLines: string[] = [];
  for (const rawLine of lines) {
    const line = rawLine ?? "";
    const trimmed = line.trim();
    if (trimmed.startsWith("```")) {
      if (inCode) {
        blocks.push({
          kind: "code",
          text: codeLines.join("\n"),
          language: codeLanguage,
        });
        inCode = false;
        codeLanguage = "";
        codeLines = [];
      } else {
        inCode = true;
        codeLanguage = trimmed.slice(3).trim();
      }
      continue;
    }
    if (inCode) {
      codeLines.push(line);
      continue;
    }
    if (!trimmed) continue;
    if (/^#{1,2}\s+/.test(trimmed)) {
      blocks.push({
        kind: trimmed.startsWith("## ") ? "heading2" : "heading1",
        text: trimmed.replace(/^#{1,2}\s+/, ""),
      });
      continue;
    }
    if (/^- \[([xX ])\]\s+/.test(trimmed)) {
      const done = trimmed.startsWith("- [x]") || trimmed.startsWith("- [X]");
      blocks.push({
        kind: "todo",
        checked: done,
        text: trimmed.replace(/^- \[[xX ]\]\s+/, ""),
      });
      continue;
    }
    if (/^- /.test(trimmed)) {
      blocks.push({
        kind: "bullet",
        text: trimmed.slice(2),
      });
      continue;
    }
    if (/^\d+\.\s+/.test(trimmed)) {
      blocks.push({
        kind: "ordered",
        text: trimmed.replace(/^\d+\.\s+/, ""),
      });
      continue;
    }
    if (/^>\s+/.test(trimmed)) {
      blocks.push({
        kind: "quote",
        text: trimmed.replace(/^>\s+/, ""),
      });
      continue;
    }
    if (/^---+$/.test(trimmed)) {
      blocks.push({ kind: "divider" });
      continue;
    }
    blocks.push({
      kind: "paragraph",
      text: line,
    });
  }
  if (inCode) {
    blocks.push({
      kind: "code",
      text: codeLines.join("\n"),
      language: codeLanguage,
    });
  }
  return blocks.length > 0 ? blocks : [{ kind: "paragraph", text: "" }];
}

async function locateSelectionRange(params: {
  client: FeishuClient;
  documentId: string;
  rootChildren: string[];
  selectionWithEllipsis?: string;
  selectionByTitle?: string;
}): Promise<{ startIndex: number; endIndex: number; reason: string }> {
  const blocksWithText: Array<{ id: string; text: string; blockType: number }> = [];
  for (const blockId of params.rootChildren) {
    const block = await params.client.getDocxBlock(params.documentId, blockId);
    blocksWithText.push({
      id: blockId,
      text: block.textContent ?? "",
      blockType: block.blockType,
    });
  }

  if (params.selectionByTitle) {
    const plainTitle = params.selectionByTitle.trim().replace(/^#+\s*/, "");
    const index = blocksWithText.findIndex((item) => isHeadingBlockType(item.blockType) && item.text.trim() === plainTitle);
    if (index < 0) {
      throw new DocxToolError("SelectionNotFound", `Cannot find title '${plainTitle}' in top-level docx blocks.`);
    }
    let endIndex = blocksWithText.length;
    for (let i = index + 1; i < blocksWithText.length; i += 1) {
      if (isHeadingBlockType(blocksWithText[i]!.blockType)) {
        endIndex = i;
        break;
      }
    }
    return { startIndex: index, endIndex, reason: "selectionByTitle" };
  }

  const selection = params.selectionWithEllipsis?.trim() ?? "";
  if (!selection) {
    throw new DocxToolError("SelectionNotFound", "FeishuUpdateDocxContent selection is required for this mode.");
  }
  const unescaped = selection.replace(/\\\.\.\./g, "__LITERAL_ELLIPSIS__");
  if (unescaped.includes("...")) {
    const [rawStart, rawEnd] = unescaped.split("...", 2);
    const startNeedle = rawStart.replace(/__LITERAL_ELLIPSIS__/g, "...").trim();
    const endNeedle = rawEnd.replace(/__LITERAL_ELLIPSIS__/g, "...").trim();
    const startIndex = blocksWithText.findIndex((item) => item.text.includes(startNeedle));
    if (startIndex < 0) {
      throw new DocxToolError("SelectionNotFound", `Cannot find selection start '${startNeedle}'.`);
    }
    let endIndex = -1;
    for (let i = startIndex; i < blocksWithText.length; i += 1) {
      if (blocksWithText[i]!.text.includes(endNeedle)) {
        endIndex = i + 1;
        break;
      }
    }
    if (endIndex < 0) {
      throw new DocxToolError("SelectionNotFound", `Cannot find selection end '${endNeedle}'.`);
    }
    return {
      startIndex,
      endIndex,
      reason: "selectionWithEllipsisRange",
    };
  }

  const needle = unescaped.replace(/__LITERAL_ELLIPSIS__/g, "...").trim();
  const matchedIndexes = blocksWithText
    .map((item, index) => ({ index, matched: item.text.includes(needle) }))
    .filter((item) => item.matched)
    .map((item) => item.index);
  if (matchedIndexes.length === 0) {
    throw new DocxToolError("SelectionNotFound", `Cannot find selection '${needle}'.`);
  }
  if (matchedIndexes.length > 1) {
    throw new DocxToolError(
      "AmbiguousSelection",
      `Selection '${needle}' matched ${matchedIndexes.length} top-level blocks. Use start...end form.`,
    );
  }
  return {
    startIndex: matchedIndexes[0]!,
    endIndex: matchedIndexes[0]! + 1,
    reason: "selectionWithEllipsisSingle",
  };
}

function isHeadingBlockType(blockType: number): boolean {
  return blockType >= 3 && blockType <= 11;
}

async function locateCommentAnchorBlockId(params: {
  client: FeishuClient;
  documentId: string;
  rootChildren: string[];
  selectionWithEllipsis: string;
}): Promise<string> {
  const selection = params.selectionWithEllipsis.trim();
  if (!selection) {
    throw new DocxToolError("SelectionNotFound", "FeishuAddDocxComment requires non-empty selectionWithEllipsis.");
  }
  const blocksWithText: Array<{ id: string; text: string }> = [];
  for (const blockId of params.rootChildren) {
    const block = await params.client.getDocxBlock(params.documentId, blockId);
    blocksWithText.push({
      id: blockId,
      text: block.textContent ?? "",
    });
  }
  const unescaped = selection.replace(/\\\.\.\./g, "__LITERAL_ELLIPSIS__");
  if (unescaped.includes("...")) {
    const [rawStart, rawEnd] = unescaped.split("...", 2);
    const startNeedle = rawStart.replace(/__LITERAL_ELLIPSIS__/g, "...").trim();
    const endNeedle = rawEnd.replace(/__LITERAL_ELLIPSIS__/g, "...").trim();
    const startIndex = blocksWithText.findIndex((item) => item.text.includes(startNeedle));
    if (startIndex < 0) {
      throw new DocxToolError("SelectionNotFound", `Cannot find selection start '${startNeedle}'.`);
    }
    for (let i = startIndex; i < blocksWithText.length; i += 1) {
      if (blocksWithText[i]!.text.includes(endNeedle)) {
        return blocksWithText[startIndex]!.id;
      }
    }
    throw new DocxToolError("SelectionNotFound", `Cannot find selection end '${endNeedle}'.`);
  }
  const needle = unescaped.replace(/__LITERAL_ELLIPSIS__/g, "...").trim();
  const matchedIndexes = blocksWithText
    .map((item, index) => ({ index, matched: item.text.includes(needle) }))
    .filter((item) => item.matched)
    .map((item) => item.index);
  if (matchedIndexes.length === 0) {
    throw new DocxToolError("SelectionNotFound", `Cannot find selection '${needle}'.`);
  }
  if (matchedIndexes.length > 1) {
    const candidates = matchedIndexes.map((index) => blocksWithText[index]!.id).join(", ");
    throw new DocxToolError(
      "AmbiguousMatch",
      `Selection '${needle}' matched ${matchedIndexes.length} blocks: ${candidates}. Narrow selectionWithEllipsis.`,
    );
  }
  return blocksWithText[matchedIndexes[0]!]!.id;
}

function buildFileCreateOptions(fileView: string | undefined): Record<string, unknown> {
  const normalized = (fileView ?? "").trim().toLowerCase();
  if (!normalized) return {};
  const mapped = FILE_VIEW_MAP.get(normalized);
  if (!mapped) {
    throw new DocxToolError("InvalidArguments", "FeishuEmbedDocxMedia fileView must be card, preview, or inline.");
  }
  return { view_type: mapped };
}

async function updateDocxTitle(client: FeishuClient, documentId: string, title: string): Promise<void> {
  await client.updateDocxBlocks(documentId, [
    {
      block_id: documentId,
      update_text_elements: {
        elements: buildTextElements(title),
      },
    },
  ]);
}

async function resolveWikiCreateTarget(params: {
  client: FeishuClient;
  value: unknown;
}): Promise<{ spaceId: string; parentNodeToken?: string } | undefined> {
  if (params.value == null) return undefined;
  if (!params.value || typeof params.value !== "object" || Array.isArray(params.value)) {
    throw new DocxToolError("InvalidArguments", "Feishu docx tool 'wiki' must be an object.");
  }
  const record = params.value as Record<string, unknown>;
  const spaceIdOrUrl = requiredText(record.spaceIdOrUrl, "wiki.spaceIdOrUrl");
  const parentNodeTokenOrUrl = optionalText(record.parentNodeTokenOrUrl);
  const target = await resolveWikiSpaceTarget({
    client: params.client,
    spaceIdOrUrl,
    parentNodeTokenOrUrl,
  });
  return {
    spaceId: target.spaceId,
    parentNodeToken: target.parentNodeToken,
  };
}
