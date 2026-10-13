import { describe, expect, it } from "vitest";
import { ItemKind, MemberRole, type FileItem } from "../../api/channeled";
import { canMoveItem } from "./canMoveItem";

function item(partial: Partial<FileItem> & Pick<FileItem, "id" | "kind">): FileItem {
  return {
    name: "x",
    createdAt: "",
    updatedAt: "",
    ...partial,
  };
}

describe("canMoveItem", () => {
  it("allows personal owner always", () => {
    expect(
      canMoveItem({
        item: item({ id: "1", kind: ItemKind.File }),
        personal: true,
        myRole: MemberRole.Viewer,
      }),
    ).toBe(true);
  });

  it("blocks viewers", () => {
    expect(
      canMoveItem({
        item: item({ id: "1", kind: ItemKind.File, uploadedBy: "me" }),
        myUserId: "me",
        myRole: MemberRole.Viewer,
      }),
    ).toBe(false);
  });

  it("lets members move own files and any folders", () => {
    expect(
      canMoveItem({
        item: item({ id: "1", kind: ItemKind.File, uploadedBy: "me" }),
        myUserId: "me",
        myRole: MemberRole.Member,
      }),
    ).toBe(true);
    expect(
      canMoveItem({
        item: item({ id: "2", kind: ItemKind.File, uploadedBy: "other" }),
        myUserId: "me",
        myRole: MemberRole.Member,
      }),
    ).toBe(false);
    expect(
      canMoveItem({
        item: item({ id: "3", kind: ItemKind.Folder }),
        myUserId: "me",
        myRole: MemberRole.Member,
      }),
    ).toBe(true);
  });

  it("lets admins move anything", () => {
    expect(
      canMoveItem({
        item: item({ id: "1", kind: ItemKind.File, uploadedBy: "other" }),
        myUserId: "me",
        myRole: MemberRole.Admin,
      }),
    ).toBe(true);
  });
});
