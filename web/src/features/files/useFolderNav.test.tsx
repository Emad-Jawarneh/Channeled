import { renderHook, act } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { describe, expect, it } from "vitest";
import type { ReactNode } from "react";
import { useFolderNav } from "./useFolderNav";

function wrapper(initial: string) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <MemoryRouter initialEntries={[initial]}>{children}</MemoryRouter>;
  };
}

describe("useFolderNav", () => {
  it("opens a folder by pushing ?folder=", () => {
    const { result } = renderHook(() => useFolderNav(true), {
      wrapper: wrapper("/files"),
    });

    act(() => {
      result.current.openFolder("11111111-1111-1111-1111-111111111111", "Docs");
    });

    expect(result.current.folderId).toBe("11111111-1111-1111-1111-111111111111");
    expect(result.current.folderNameHint).toBe("Docs");
  });

  it("reads folder from the URL", () => {
    const id = "22222222-2222-2222-2222-222222222222";
    const { result } = renderHook(() => useFolderNav(true), {
      wrapper: wrapper(`/files?folder=${id}`),
    });
    expect(result.current.folderId).toBe(id);
  });

  it("clears invalid folder with replace", () => {
    const id = "33333333-3333-3333-3333-333333333333";
    const { result } = renderHook(
      () => {
        const nav = useFolderNav(true);
        const loc = useLocation();
        return { nav, search: loc.search };
      },
      { wrapper: wrapper(`/files?folder=${id}`) },
    );

    act(() => {
      result.current.nav.clearInvalidFolder();
    });

    expect(result.current.nav.folderId).toBeUndefined();
    expect(result.current.search).not.toContain("folder=");
  });

  it("ignores folder when disabled", () => {
    const id = "44444444-4444-4444-4444-444444444444";
    const { result } = renderHook(() => useFolderNav(false), {
      wrapper: wrapper(`/files?folder=${id}`),
    });
    expect(result.current.folderId).toBeUndefined();
  });
});
