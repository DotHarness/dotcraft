import { randomUUID } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { basename, join } from "node:path";
import { fileRefPart, type InputPart } from "@dotcraft/sdk";

export async function saveInboundFile(craftPath: string, bytes: Uint8Array, fileName: string): Promise<InputPart> {
  const dir = join(craftPath, "attachments", "files", randomUUID());
  await mkdir(dir, { recursive: true });
  const path = join(dir, safeFileName(fileName));
  await writeFile(path, bytes);
  return fileRefPart(path);
}

function safeFileName(fileName: string): string {
  const name = basename(fileName.replace(/\\/g, "/"))
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, "_")
    .replace(/^[.\s]+|[.\s]+$/g, "");
  return name || "attachment";
}
