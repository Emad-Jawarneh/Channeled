import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import type { ChannelMessage } from "../../api/channeled";
import { MessageRow, type MessageRowProps } from "./MessageRow";

const message: ChannelMessage = {
  id: "m1",
  channelId: "c1",
  authorId: "u1",
  authorDisplayName: "Ada",
  body: "hello",
  createdAt: "2026-10-15T10:00:00Z",
  replyTo: null,
  mentions: [],
  fileIds: [],
  authorAvatarVersion: 42,
};

function wrap(children: ReactNode) {
  return <QueryClientProvider client={new QueryClient()}>{children}</QueryClientProvider>;
}

function renderRow(overrides: Partial<MessageRowProps> = {}) {
  const props: MessageRowProps = {
    message,
    teamSlug: "t",
    myId: "u2",
    grouped: false,
    dayLabel: null,
    canReply: true,
    canDelete: true,
    active: false,
    onReply: vi.fn(),
    onDelete: vi.fn(),
    onJumpTo: vi.fn(),
    onActivate: vi.fn(),
    ...overrides,
  };
  render(wrap(<ul>{<MessageRow {...props} />}</ul>));
  return props;
}

describe("MessageRow", () => {
  it("passes skipConfirm only for Shift+click delete", () => {
    const props = renderRow();
    fireEvent.click(screen.getByTestId("chat-delete"));
    fireEvent.click(screen.getByTestId("chat-delete"), { shiftKey: true });
    expect(props.onDelete).toHaveBeenNthCalledWith(1, message, false);
    expect(props.onDelete).toHaveBeenNthCalledWith(2, message, true);
  });

  it("only the active row is in the tab order (roving tab stop)", () => {
    renderRow({ active: false });
    expect(screen.getByRole("article")).toHaveAttribute("tabindex", "-1");
    expect(screen.getByTestId("chat-reply")).toHaveAttribute("tabindex", "-1");
  });

  it("the active row and its toolbar are tabbable", () => {
    renderRow({ active: true });
    expect(screen.getByRole("article")).toHaveAttribute("tabindex", "0");
    expect(screen.getByTestId("chat-delete")).toHaveAttribute("tabindex", "0");
  });

  it("shows the profile avatar image, and no header when grouped", () => {
    renderRow();
    expect(screen.getByTestId("chat-avatar")).toHaveAttribute("src", "/api/v1/users/u1/avatar?v=42");
  });

  it("grouped rows keep an accessible author name without the visual header", () => {
    renderRow({ grouped: true });
    expect(screen.queryByTestId("chat-avatar")).toBeNull();
    expect(screen.getByRole("article")).toHaveAccessibleName(/Ada/);
  });

  it("reports focus on the row itself to the parent", () => {
    const props = renderRow({ active: true });
    fireEvent.focus(screen.getByRole("article"));
    expect(props.onActivate).toHaveBeenCalledWith("m1");
  });
});
