import { renderHook, act } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useFileDrop } from "./useFileDrop";

function fireDrag(type: string, types: string[]) {
  const event = new Event(type, { bubbles: true, cancelable: true }) as DragEvent;
  Object.defineProperty(event, "dataTransfer", {
    value: {
      types,
      files: types.includes("Files") ? [new File(["x"], "a.txt")] : [],
      dropEffect: "none",
    },
  });
  window.dispatchEvent(event);
  return event;
}

describe("useFileDrop", () => {
  it("activates only when Files type is present", () => {
    const onFiles = vi.fn();
    const { result } = renderHook(() => useFileDrop({ enabled: true, onFiles }));

    act(() => {
      fireDrag("dragenter", ["application/x-channeled-items"]);
    });
    expect(result.current.dropActive).toBe(false);

    act(() => {
      fireDrag("dragenter", ["Files"]);
    });
    expect(result.current.dropActive).toBe(true);
  });
});
