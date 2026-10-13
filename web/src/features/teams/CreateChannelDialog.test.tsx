import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ChannelKind, ChannelVisibility } from "../../api/channeled";
import { CreateChannelDialog } from "./CreateChannelDialog";

beforeEach(() => {
  // jsdom does not implement HTMLDialogElement.showModal.
  HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
    this.setAttribute("open", "");
  });
  HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) {
    this.removeAttribute("open");
  });
});

describe("CreateChannelDialog", () => {
  it("offers Everyone / Restricted and defaults to Everyone", () => {
    render(<CreateChannelDialog open onClose={() => {}} onCreate={() => {}} />);
    const select = screen.getByTestId("create-channel-visibility") as HTMLSelectElement;
    expect(select.value).toBe(String(ChannelVisibility.Everyone));
    const labels = Array.from(select.options).map((o) => o.textContent);
    expect(labels).toEqual(["Everyone", "Restricted"]);
  });

  it("submits the chosen visibility", () => {
    const onCreate = vi.fn();
    render(<CreateChannelDialog open onClose={() => {}} onCreate={onCreate} />);
    fireEvent.change(screen.getByTestId("create-channel-name"), { target: { value: "ops" } });
    fireEvent.change(screen.getByTestId("create-channel-visibility"), {
      target: { value: String(ChannelVisibility.Restricted) },
    });
    fireEvent.click(screen.getByTestId("create-channel-submit"));
    expect(onCreate).toHaveBeenCalledWith({
      name: "ops",
      visibility: ChannelVisibility.Restricted,
      kind: ChannelKind.File,
    });
  });
});
