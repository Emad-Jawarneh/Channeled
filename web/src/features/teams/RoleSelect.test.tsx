import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { MemberRole } from "../../api/channeled";
import { RoleSelect } from "./RoleSelect";

describe("RoleSelect", () => {
  it("opens a padded menu and changes role", () => {
    const onChange = vi.fn();
    render(
      <RoleSelect
        testId="role-ada@x.com"
        value={MemberRole.Member}
        aria-label="Role for ada@x.com"
        onChange={onChange}
      />,
    );

    fireEvent.click(screen.getByTestId("role-ada@x.com"));
    expect(screen.getByText("Browse files and channels")).toBeTruthy();
    fireEvent.click(screen.getByTestId("role-ada@x.com-option-Admin"));
    expect(onChange).toHaveBeenCalledWith(MemberRole.Admin);
  });
});
