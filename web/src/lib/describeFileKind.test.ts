import { describe, expect, it } from "vitest";
import { ItemKind } from "../api/channeled";
import {
  describeFileKind,
  formatFileType,
  matchesTypeFilter,
} from "./describeFileKind";

describe("describeFileKind", () => {
  it("labels folders", () => {
    expect(describeFileKind({ kind: ItemKind.Folder, name: "Docs" })).toBe("Folder");
  });

  it("categorizes from MIME", () => {
    expect(describeFileKind({ kind: ItemKind.File, name: "x", contentType: "image/png" })).toBe(
      "image",
    );
    expect(describeFileKind({ kind: ItemKind.File, name: "x", contentType: "video/mp4" })).toBe(
      "video",
    );
    expect(describeFileKind({ kind: ItemKind.File, name: "x", contentType: "audio/mpeg" })).toBe(
      "audio",
    );
    expect(
      describeFileKind({ kind: ItemKind.File, name: "x", contentType: "application/pdf" }),
    ).toBe("document");
    expect(
      describeFileKind({
        kind: ItemKind.File,
        name: "x",
        contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      }),
    ).toBe("spreadsheet");
    expect(
      describeFileKind({
        kind: ItemKind.File,
        name: "x",
        contentType: "application/vnd.openxmlformats-officedocument.presentationml.presentation",
      }),
    ).toBe("presentation");
    expect(
      describeFileKind({ kind: ItemKind.File, name: "x", contentType: "application/zip" }),
    ).toBe("archive");
    expect(
      describeFileKind({ kind: ItemKind.File, name: "x", contentType: "application/json" }),
    ).toBe("code");
    expect(describeFileKind({ kind: ItemKind.File, name: "x", contentType: "text/plain" })).toBe(
      "text",
    );
    expect(describeFileKind({ kind: ItemKind.File, name: "x", contentType: "text/csv" })).toBe(
      "spreadsheet",
    );
    expect(describeFileKind({ kind: ItemKind.File, name: "x", contentType: "text/html" })).toBe(
      "code",
    );
  });

  it("categorizes from extension when MIME is missing", () => {
    expect(describeFileKind({ kind: ItemKind.File, name: "a.HEIC" })).toBe("image");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.mkv" })).toBe("video");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.flac" })).toBe("audio");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.docx" })).toBe("document");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.xlsx" })).toBe("spreadsheet");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.pptx" })).toBe("presentation");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.7z" })).toBe("archive");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.tsx" })).toBe("code");
    expect(describeFileKind({ kind: ItemKind.File, name: "a.md" })).toBe("text");
    expect(describeFileKind({ kind: ItemKind.File, name: "bundle.tar.gz" })).toBe("archive");
  });

  it("falls back to extension for unknown types", () => {
    expect(describeFileKind({ kind: ItemKind.File, name: "model.glb" })).toBe("GLB");
  });

  it("defaults unknown blobs to file", () => {
    expect(describeFileKind({ kind: ItemKind.File, name: "mysterious" })).toBe("file");
  });

  it("formats table type labels and matches filters", () => {
    const pic = { kind: ItemKind.File, name: "a.png", contentType: "image/png" };
    expect(formatFileType(pic)).toBe("Image");
    expect(matchesTypeFilter(pic, "image")).toBe(true);
    expect(matchesTypeFilter(pic, "video")).toBe(false);
    expect(matchesTypeFilter({ kind: ItemKind.Folder, name: "Docs" }, "folder")).toBe(true);
    expect(formatFileType({ kind: ItemKind.File, name: "x.glb" })).toBe("GLB");
    expect(matchesTypeFilter({ kind: ItemKind.File, name: "x.glb" }, "other")).toBe(true);
  });
});
