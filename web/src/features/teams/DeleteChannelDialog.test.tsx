import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ChannelKind, ChannelVisibility, type Channel } from "../../api/channeled";
import { DeleteChannelDialog } from "./DeleteChannelDialog";

const fileChannel: Channel = {
  id: "1",
  teamId: "t",
  name: "brand-assets",
  visibility: ChannelVisibility.Everyone,
  kind: ChannelKind.File,
};

const other: Channel = {
  id: "2",
  teamId: "t",
  name: "general",
  visibility: ChannelVisibility.Everyone,
  kind: ChannelKind.File,
};

beforeEach(() => {
  // jsdom does not implement HTMLDialogElement.showModal.
  HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
    this.setAttribute("open", "");
  });
  HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) {
    this.removeAttribute("open");
  });
});

describe("DeleteChannelDialog", () => {
  it("keeps confirm disabled until the channel name is typed", () => {
    const onDelete = vi.fn();
    render(
      <DeleteChannelDialog
        open
        channel={fileChannel}
        otherFileChannels={[other]}
        onClose={() => {}}
        onDelete={onDelete}
        onArchive={() => {}}
      />,
    );

    const submit = screen.getByTestId("delete-channel-submit");
    expect(submit).toBeDisabled();

    fireEvent.change(screen.getByTestId("delete-channel-confirm-name"), {
      target: { value: "brand-assets" },
    });
    expect(submit).toBeEnabled();

    fireEvent.click(submit);
    expect(onDelete).toHaveBeenCalledWith({ mode: "discard" });
  });

  it("requires a move target when moving files", () => {
    const onDelete = vi.fn();
    render(
      <DeleteChannelDialog
        open
        channel={fileChannel}
        otherFileChannels={[other]}
        onClose={() => {}}
        onDelete={onDelete}
        onArchive={() => {}}
      />,
    );

    fireEvent.click(screen.getByTestId("delete-channel-move"));
    fireEvent.change(screen.getByTestId("delete-channel-confirm-name"), {
      target: { value: "brand-assets" },
    });
    fireEvent.click(screen.getByTestId("delete-channel-submit"));
    expect(onDelete).toHaveBeenCalledWith({ mode: "move", targetChannel: "general" });
  });
});
