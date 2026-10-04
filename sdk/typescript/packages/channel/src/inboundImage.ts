import { randomUUID } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { localImagePart, type InputPart } from "@dotcraft/sdk";

export async function saveInboundImage(craftPath: string, bytes: Uint8Array, mediaType: string): Promise<InputPart> {
  const dir = join(craftPath, "attachments", "images");
  await mkdir(dir, { recursive: true });
  const fileName = `${randomUUID()}${imageFileExtension(mediaType)}`;
  const path = join(dir, fileName);
  await writeFile(path, bytes);
  return { ...localImagePart(path), mimeType: mediaType, fileName };
}

export function imageFileExtension(mediaType: string): string {
  switch (mediaType.toLowerCase()) {
    case "image/jpeg":
    case "image/jpg":
      return ".jpg";
    case "image/webp":
      return ".webp";
    case "image/gif":
      return ".gif";
    default:
      return ".png";
  }
}
