import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemberRole, type TeamMember } from "../../api/channeled";
import { ManageChannelMembersDialog } from "./ManageChannelMembersDialog";

beforeEach(() => {
  HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
    this.setAttribute("open", "");
  });
  HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) {
    this.removeAttribute("open");
  });
});

function member(partial: Partial<TeamMember> & Pick<TeamMember, "userId" | "email" | "displayName">): TeamMember {
  return {
    role: MemberRole.Member,
    joinedAt: "2024-01-01T00:00:00Z",
    ...partial,
  };
}

describe("ManageChannelMembersDialog", () => {
  it("filters candidates by name or email and keeps selection chips", () => {
    const candidates = Array.from({ length: 120 }, (_, i) =>
      member({
        userId: `u${i}`,
        email: `user${i}@example.com`,
        displayName: i === 95 ? "Zed Searchable" : `Person ${i}`,
      }),
    );

    render(
      <ManageChannelMembersDialog
        open
        channelName="secret"
        candidates={candidates}
        onClose={() => {}}
        onAdd={() => {}}
      />,
    );

    expect(screen.queryByText("Person 95")).toBeNull();
    fireEvent.change(screen.getByTestId("grant-search-secret"), {
      target: { value: "searchable" },
    });
    expect(screen.getByText("Zed Searchable")).toBeTruthy();

    fireEvent.click(screen.getByTestId("grant-pick-secret-user95@example.com"));
    expect(screen.getByTestId("grant-selected-secret").textContent).toContain("Zed Searchable");
  });

  it("submits selected user ids with read-only flag", () => {
    const onAdd = vi.fn();
    const candidates = [
      member({ userId: "a", email: "a@x.com", displayName: "Ada" }),
      member({ userId: "b", email: "b@x.com", displayName: "Bea" }),
    ];

    render(
      <ManageChannelMembersDialog
        open
        channelName="secret"
        candidates={candidates}
        onClose={() => {}}
        onAdd={onAdd}
      />,
    );

    fireEvent.click(screen.getByTestId("grant-pick-secret-a@x.com"));
    fireEvent.click(screen.getByTestId("grant-bulk-readonly-secret"));
    fireEvent.click(screen.getByTestId("grant-add-submit-secret"));

    expect(onAdd).toHaveBeenCalledWith({ userIds: ["a"], readOnly: true });
  });
});
