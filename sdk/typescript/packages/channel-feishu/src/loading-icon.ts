import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { errorMessage, logInfo, logWarn } from "./logging.js";

const DEFAULT_ASSET_PATH = fileURLToPath(new URL("../assets/loading.gif", import.meta.url));
const CACHE_FILE_NAME = "loading-icon.json";

export type LoadingIconClient = {
  uploadImage(data: Buffer): Promise<string>;
};

// Feishu cards can only animate images, so the status row icon is a GIF uploaded once per app
// and its image key cached under the module state directory. Any failure yields `undefined`.
export class FeishuLoadingIcon {
  private readonly cacheFile: string | undefined;
  private readonly assetPath: string;
  private pending: Promise<string | undefined> | undefined;

  constructor(
    private readonly client: LoadingIconClient,
    options: { stateDir?: string; assetPath?: string } = {},
  ) {
    this.cacheFile = options.stateDir ? path.join(options.stateDir, CACHE_FILE_NAME) : undefined;
    this.assetPath = options.assetPath ?? DEFAULT_ASSET_PATH;
  }

  imgKey(): Promise<string | undefined> {
    this.pending ??= this.resolve();
    return this.pending;
  }

  private async resolve(): Promise<string | undefined> {
    const cached = await this.readCache();
    if (cached) return cached;
    try {
      const data = await readFile(this.assetPath);
      const imgKey = await this.client.uploadImage(data);
      await this.writeCache(imgKey);
      logInfo("loading_icon.uploaded", { bytes: data.length });
      return imgKey;
    } catch (error) {
      logWarn("loading_icon.unavailable", { message: errorMessage(error) });
      return undefined;
    }
  }

  private async readCache(): Promise<string | undefined> {
    if (!this.cacheFile) return undefined;
    try {
      const parsed = JSON.parse(await readFile(this.cacheFile, "utf8")) as { imgKey?: unknown };
      return typeof parsed.imgKey === "string" && parsed.imgKey ? parsed.imgKey : undefined;
    } catch {
      return undefined;
    }
  }

  private async writeCache(imgKey: string): Promise<void> {
    if (!this.cacheFile) return;
    try {
      await mkdir(path.dirname(this.cacheFile), { recursive: true });
      await writeFile(
        this.cacheFile,
        JSON.stringify({ imgKey, uploadedAt: new Date().toISOString() }, null, 2),
        "utf8",
      );
    } catch (error) {
      logWarn("loading_icon.cache_write_failed", { message: errorMessage(error) });
    }
  }
}
