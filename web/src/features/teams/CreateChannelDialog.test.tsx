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
    const group = screen.getByTestId("create-channel-visibility");
    const everyone = screen.getByTestId("create-channel-visibility-everyone");
    const restricted = screen.getByTestId("create-channel-visibility-restricted");
    expect(group).toBeTruthy();
    expect(everyone.getAttribute("aria-checked")).toBe("true");
    expect(restricted.getAttribute("aria-checked")).toBe("false");
    expect(everyone.textContent).toContain("Everyone");
    expect(restricted.textContent).toContain("Restricted");
  });

  it("submits the chosen visibility", () => {
    const onCreate = vi.fn();
    render(<CreateChannelDialog open onClose={() => {}} onCreate={onCreate} />);
    fireEvent.change(screen.getByTestId("create-channel-name"), { target: { value: "ops" } });
    fireEvent.click(screen.getByTestId("create-channel-visibility-restricted"));
    fireEvent.click(screen.getByTestId("create-channel-submit"));
    expect(onCreate).toHaveBeenCalledWith({
      name: "ops",
      visibility: ChannelVisibility.Restricted,
      kind: ChannelKind.File,
    });
  });
});
