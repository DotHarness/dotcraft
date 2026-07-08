import { createHash } from "node:crypto";
import { mkdtemp, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, extname, join, resolve } from "node:path";

export type ChannelMediaSource =
  | { kind: "hostPath"; hostPath: string; fileName?: string; mediaType?: string }
  | { kind: "url"; url: string; fileName?: string; mediaType?: string }
  | { kind: "dataBase64"; dataBase64: string; fileName?: string; mediaType?: string };

export type MediaErrorFactory = (code: string, message: string) => Error;

export interface PrepareMediaBytesOptions {
  fileName?: string;
  fallbackFileName?: string;
  mediaType?: string;
  allowUrl?: boolean;
  maxBytes?: number;
  fetch?: typeof fetch;
  errorFactory?: MediaErrorFactory;
}

export interface PreparedMediaBytes {
  bytes: Buffer;
  byteLength: number;
  fileName: string;
  mediaType: string;
  md5: string;
  sourceKind: ChannelMediaSource["kind"];
  resolvedPath?: string;
  url?: string;
}

export interface PrepareMediaTempFileOptions extends PrepareMediaBytesOptions {
  tempPrefix?: string;
}

export interface PreparedMediaTempFile extends PreparedMediaBytes {
  path: string;
  cleanupDir: string;
  cleanup(): Promise<void>;
}

export interface PrepareMediaUploadUriOptions extends PrepareMediaBytesOptions {
  allowLocalFileUri?: boolean;
}

export interface PreparedMediaUploadUri {
  uri: string;
  fileName: string;
  mediaType: string;
  sourceKind: ChannelMediaSource["kind"];
  byteLength?: number;
  md5?: string;
  resolvedPath?: string;
  url?: string;
}

export class MediaSourceError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "MediaSourceError";
    this.code = code;
  }
}

export function mediaSourceFromToolPath(
  value: unknown,
  options: { fieldName?: string; errorFactory?: MediaErrorFactory } = {},
): ChannelMediaSource {
  return {
    kind: "hostPath",
    hostPath: requiredText(value, options.fieldName ?? "filePath", options),
  };
}

export function mediaSourceFromToolUrl(
  value: unknown,
  options: { fieldName?: string; errorFactory?: MediaErrorFactory } = {},
): ChannelMediaSource {
  return {
    kind: "url",
    url: requiredText(value, options.fieldName ?? "fileUrl", options),
  };
}

export function mediaSourceFromToolBase64(
  value: unknown,
  options: { fieldName?: string; errorFactory?: MediaErrorFactory } = {},
): ChannelMediaSource {
  return {
    kind: "dataBase64",
    dataBase64: requiredText(value, options.fieldName ?? "fileBase64", options),
  };
}

export async function prepareMediaBytes(
  source: Record<string, unknown> | ChannelMediaSource,
  options: PrepareMediaBytesOptions = {},
): Promise<PreparedMediaBytes> {
  const normalized = normalizeMediaSource(source, options);
  if (normalized.kind === "hostPath") {
    const resolvedPath = resolve(normalized.hostPath);
    const fileStats = await stat(resolvedPath).catch((error: unknown) => {
      fail(options, "InvalidArguments", `Cannot access file '${resolvedPath}': ${formatError(error)}`);
    });
    if (!fileStats.isFile()) {
      fail(options, "InvalidArguments", `Path '${resolvedPath}' is not a regular file.`);
    }
    const bytes = await readFile(resolvedPath).catch((error: unknown) => {
      fail(options, "InvalidArguments", `Cannot read file '${resolvedPath}': ${formatError(error)}`);
    });
    const fileName = normalizeFileName(options.fileName ?? normalized.fileName ?? basename(resolvedPath), options.fallbackFileName);
    return finalizePreparedMedia(bytes, {
      ...options,
      sourceKind: "hostPath",
      fileName,
      mediaType: options.mediaType ?? normalized.mediaType,
      resolvedPath,
    });
  }

  if (normalized.kind === "dataBase64") {
    const bytes = decodeBase64Media(normalized.dataBase64, options);
    const fileName = normalizeFileName(options.fileName ?? normalized.fileName, options.fallbackFileName);
    return finalizePreparedMedia(bytes, {
      ...options,
      sourceKind: "dataBase64",
      fileName,
      mediaType: options.mediaType ?? normalized.mediaType,
    });
  }

  if (!options.allowUrl) {
    fail(options, "UnsupportedMediaSource", "URL media sources are not enabled for this channel operation.");
  }
  const parsedUrl = parseHttpUrl(normalized.url, options);
  const response = await (options.fetch ?? fetch)(parsedUrl).catch((error: unknown) => {
    fail(options, "InvalidArguments", `Cannot fetch media URL '${parsedUrl.toString()}': ${formatError(error)}`);
  });
  if (!response.ok) {
    fail(options, "InvalidArguments", `Media URL '${parsedUrl.toString()}' returned HTTP ${response.status}.`);
  }
  const contentLength = parseContentLength(response.headers.get("content-length"));
  if (contentLength !== undefined) enforceMaxBytes(contentLength, options);
  const bytes = Buffer.from(await response.arrayBuffer());
  const responseMediaType = normalizeMediaType(response.headers.get("content-type") ?? undefined);
  const fileName = normalizeFileName(
    options.fileName ?? normalized.fileName ?? fileNameFromUrl(parsedUrl),
    options.fallbackFileName,
  );
  return finalizePreparedMedia(bytes, {
    ...options,
    sourceKind: "url",
    fileName,
    mediaType: options.mediaType ?? normalized.mediaType ?? responseMediaType,
    url: parsedUrl.toString(),
  });
}

export async function prepareMediaTempFile(
  source: Record<string, unknown> | ChannelMediaSource,
  options: PrepareMediaTempFileOptions = {},
): Promise<PreparedMediaTempFile> {
  const prepared = await prepareMediaBytes(source, options);
  const cleanupDir = await mkdtemp(join(tmpdir(), options.tempPrefix ?? "dotcraft-media-"));
  const path = join(cleanupDir, safeTempFileName(prepared.fileName));
  await writeFile(path, prepared.bytes);
  return {
    ...prepared,
    path,
    cleanupDir,
    cleanup: async () => {
      await rm(cleanupDir, { recursive: true, force: true });
    },
  };
}

export async function prepareMediaUploadUri(
  source: Record<string, unknown> | ChannelMediaSource,
  options: PrepareMediaUploadUriOptions = {},
): Promise<PreparedMediaUploadUri> {
  const normalized = normalizeMediaSource(source, options);
  if (normalized.kind === "url") {
    const parsedUrl = parseHttpUrl(normalized.url, options);
    if (options.allowUrl === false) {
      fail(options, "UnsupportedMediaSource", "URL media sources are not enabled for this media upload operation.");
    }
    const fileName = normalizeFileName(
      options.fileName ?? normalized.fileName ?? fileNameFromUrl(parsedUrl),
      options.fallbackFileName,
    );
    return {
      uri: parsedUrl.toString(),
      fileName,
      mediaType: options.mediaType ?? normalized.mediaType ?? inferMediaTypeFromFileName(fileName),
      sourceKind: "url",
      url: parsedUrl.toString(),
    };
  }

  if (normalized.kind === "hostPath" && options.allowLocalFileUri) {
    const prepared = await prepareMediaBytes(normalized, { ...options, allowUrl: false });
    return {
      uri: `file://${prepared.resolvedPath}`,
      fileName: prepared.fileName,
      mediaType: prepared.mediaType,
      sourceKind: prepared.sourceKind,
      byteLength: prepared.byteLength,
      md5: prepared.md5,
      resolvedPath: prepared.resolvedPath,
    };
  }

  const prepared = await prepareMediaBytes(normalized, { ...options, allowUrl: false });
  return {
    uri: `base64://${prepared.bytes.toString("base64")}`,
    fileName: prepared.fileName,
    mediaType: prepared.mediaType,
    sourceKind: prepared.sourceKind,
    byteLength: prepared.byteLength,
    md5: prepared.md5,
    resolvedPath: prepared.resolvedPath,
  };
}

export function decodeBase64Media(
  value: unknown,
  options: { fieldName?: string; errorFactory?: MediaErrorFactory } = {},
): Buffer {
  const fieldName = options.fieldName ?? "dataBase64";
  const text = requiredText(value, fieldName, options);
  const normalized = text.startsWith("base64://") ? text.slice("base64://".length) : text;
  const compact = normalized.replace(/\s+/g, "");
  if (
    !compact ||
    compact.length % 4 === 1 ||
    /[^A-Za-z0-9+/=]/.test(compact) ||
    !/^[A-Za-z0-9+/]*={0,2}$/.test(compact)
  ) {
    fail(options, "InvalidArguments", `${fieldName} did not contain valid base64.`);
  }
  return Buffer.from(compact, "base64");
}

export function inferMediaTypeFromFileName(fileName: string): string {
  switch (extname(fileName).toLowerCase()) {
    case ".csv":
      return "text/csv";
    case ".gif":
      return "image/gif";
    case ".htm":
    case ".html":
      return "text/html";
    case ".jpg":
    case ".jpeg":
      return "image/jpeg";
    case ".json":
      return "application/json";
    case ".md":
      return "text/markdown";
    case ".mp3":
      return "audio/mpeg";
    case ".mp4":
      return "video/mp4";
    case ".ogg":
    case ".oga":
      return "audio/ogg";
    case ".pdf":
      return "application/pdf";
    case ".png":
      return "image/png";
    case ".txt":
      return "text/plain";
    case ".xml":
      return "application/xml";
    case ".zip":
      return "application/zip";
    default:
      return "application/octet-stream";
  }
}

function normalizeMediaSource(
  source: Record<string, unknown> | ChannelMediaSource,
  options: { errorFactory?: MediaErrorFactory },
): ChannelMediaSource {
  const kind = String((source as Record<string, unknown>).kind ?? "").trim();
  if (kind === "hostPath") {
    return {
      kind,
      hostPath: requiredText((source as Record<string, unknown>).hostPath, "hostPath", options),
      fileName: optionalText((source as Record<string, unknown>).fileName),
      mediaType: optionalText((source as Record<string, unknown>).mediaType),
    };
  }
  if (kind === "url") {
    return {
      kind,
      url: requiredText((source as Record<string, unknown>).url, "url", options),
      fileName: optionalText((source as Record<string, unknown>).fileName),
      mediaType: optionalText((source as Record<string, unknown>).mediaType),
    };
  }
  if (kind === "dataBase64") {
    return {
      kind,
      dataBase64: requiredText((source as Record<string, unknown>).dataBase64, "dataBase64", options),
      fileName: optionalText((source as Record<string, unknown>).fileName),
      mediaType: optionalText((source as Record<string, unknown>).mediaType),
    };
  }
  fail(options, "UnsupportedMediaSource", `Unsupported media source kind '${kind || "unknown"}'.`);
}

function finalizePreparedMedia(
  bytes: Buffer,
  options: PrepareMediaBytesOptions & {
    sourceKind: ChannelMediaSource["kind"];
    fileName: string;
    resolvedPath?: string;
    url?: string;
  },
): PreparedMediaBytes {
  enforceMaxBytes(bytes.length, options);
  const mediaType = options.mediaType ?? inferMediaTypeFromFileName(options.fileName);
  return {
    bytes,
    byteLength: bytes.length,
    fileName: options.fileName,
    mediaType,
    md5: createHash("md5").update(bytes).digest("hex"),
    sourceKind: options.sourceKind,
    resolvedPath: options.resolvedPath,
    url: options.url,
  };
}

function enforceMaxBytes(byteLength: number, options: { maxBytes?: number; errorFactory?: MediaErrorFactory }): void {
  if (options.maxBytes !== undefined && byteLength > options.maxBytes) {
    fail(options, "InvalidArguments", `Media source is ${byteLength} bytes, exceeding the ${options.maxBytes} byte limit.`);
  }
}

function parseHttpUrl(value: string, options: { errorFactory?: MediaErrorFactory }): URL {
  let parsed: URL;
  try {
    parsed = new URL(value);
  } catch {
    fail(options, "InvalidArguments", `Media URL '${value}' is not a valid URL.`);
  }
  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    fail(options, "InvalidArguments", `Media URL '${value}' must use http or https.`);
  }
  return parsed;
}

function parseContentLength(value: string | null): number | undefined {
  if (!value) return undefined;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : undefined;
}

function normalizeFileName(fileName: string | undefined, fallbackFileName: string | undefined): string {
  const normalized = fileName?.trim() || fallbackFileName?.trim() || "attachment";
  return fileNameLeaf(normalized) || "attachment";
}

function safeTempFileName(fileName: string): string {
  const normalized = normalizeFileName(fileName, "attachment").replace(/[<>:"/\\|?*\x00-\x1F]/g, "_");
  return normalized || "attachment";
}

function fileNameFromUrl(url: URL): string | undefined {
  const pathName = url.pathname.trim();
  if (!pathName || pathName === "/") return undefined;
  const name = fileNameLeaf(pathName);
  if (!name) return undefined;
  try {
    return decodeURIComponent(name);
  } catch {
    return name;
  }
}

function fileNameLeaf(value: string): string {
  const normalized = value.replaceAll("\\", "/");
  const parts = normalized.split("/");
  return parts[parts.length - 1] ?? "";
}

function normalizeMediaType(value: string | undefined): string | undefined {
  const normalized = value?.split(";", 1)[0]?.trim();
  return normalized || undefined;
}

function requiredText(
  value: unknown,
  fieldName: string,
  options: { errorFactory?: MediaErrorFactory },
): string {
  const text = optionalText(value);
  if (!text) fail(options, "InvalidArguments", `${fieldName} is required.`);
  return text;
}

function optionalText(value: unknown): string | undefined {
  const text = String(value ?? "").trim();
  return text || undefined;
}

function fail(options: { errorFactory?: MediaErrorFactory }, code: string, message: string): never {
  throw options.errorFactory?.(code, message) ?? new MediaSourceError(code, message);
}

function formatError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
