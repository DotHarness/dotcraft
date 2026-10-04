import { describe, expect, it } from "vitest";
import { uploadPhotos, type AttachmentFileSystem } from "./attachments";

function memory(
  failing = false,
): AttachmentFileSystem & { written: Map<string, string>; folders: string[] } {
  const written = new Map<string, string>();
  const folders: string[] = [];
  return {
    written,
    folders,
    async createDirectory(path) {
      folders.push(path);
    },
    async writeFile(path, data) {
      if (failing) throw new Error("disk full");
      written.set(path, data);
    },
  };
}

describe("photo upload", () => {
  it("writes photos into the project attachments and keeps their type", async () => {
    const fs = memory();
    let id = 0;
    const photos = await uploadPhotos(
      fs,
      "D:/Projects/app",
      [
        { id: "a", dataUrl: "data:image/jpeg;base64,AAAA" },
        { id: "b", dataUrl: "data:image/png;base64,BBBB" },
      ],
      () => `p${(id += 1)}`,
    );
    expect(fs.folders).toEqual(["D:/Projects/app/.craft/attachments/images"]);
    expect(photos).toEqual([
      {
        path: "D:/Projects/app/.craft/attachments/images/p1.jpg",
        fileName: "photo-1.jpg",
        mimeType: "image/jpeg",
      },
      {
        path: "D:/Projects/app/.craft/attachments/images/p2.png",
        fileName: "photo-2.png",
        mimeType: "image/png",
      },
    ]);
    expect([...fs.written.values()]).toEqual(["AAAA", "BBBB"]);
  });

  it("names the photo that could not be uploaded", async () => {
    await expect(
      uploadPhotos(
        memory(true),
        "D:/p",
        [{ id: "a", dataUrl: "data:image/jpeg;base64,AAAA" }],
        () => "x",
      ),
    ).rejects.toMatchObject({ file: "photo-1.jpg" });
  });
});
