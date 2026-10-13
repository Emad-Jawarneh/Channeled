import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { MemberRole } from "../../api/channeled";
import styles from "./RoleSelect.module.css";

export const ROLE_OPTIONS = [
  {
    value: MemberRole.Viewer,
    label: "Viewer",
    detail: "Browse files and channels",
  },
  {
    value: MemberRole.Member,
    label: "Member",
    detail: "Upload, post, and collaborate",
  },
  {
    value: MemberRole.Admin,
    label: "Admin",
    detail: "Manage channels and members",
  },
] as const;

type MenuCoords = {
  top: number;
  left: number;
  width: number;
  openUp: boolean;
};

type Props = {
  value: number;
  disabled?: boolean;
  /** Stable id for tests, e.g. member email. */
  testId: string;
  "aria-label"?: string;
  onChange: (role: number) => void;
};

const MENU_GAP = 6;
const MENU_ESTIMATE = 220;

export function RoleSelect({ value, disabled = false, testId, onChange, "aria-label": ariaLabel }: Props) {
  const [open, setOpen] = useState(false);
  const [coords, setCoords] = useState<MenuCoords | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLUListElement>(null);
  const listId = useId();
  const current = ROLE_OPTIONS.find((o) => o.value === value) ?? ROLE_OPTIONS[1];

  function measure(): MenuCoords | null {
    const trigger = triggerRef.current;
    if (!trigger) return null;
    const rect = trigger.getBoundingClientRect();
    const width = Math.max(rect.width, 14.5 * 16);
    const spaceBelow = window.innerHeight - rect.bottom;
    const openUp = spaceBelow < MENU_ESTIMATE && rect.top > spaceBelow;
    const left = Math.min(rect.left, window.innerWidth - width - 8);
    return {
      top: openUp ? rect.top - MENU_GAP : rect.bottom + MENU_GAP,
      left: Math.max(8, left),
      width,
      openUp,
    };
  }

  useLayoutEffect(() => {
    if (!open) {
      setCoords(null);
      return;
    }
    setCoords(measure());

    function reposition() {
      setCoords(measure());
    }

    window.addEventListener("resize", reposition);
    window.addEventListener("scroll", reposition, true);
    return () => {
      window.removeEventListener("resize", reposition);
      window.removeEventListener("scroll", reposition, true);
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent) {
      const target = event.target as Node;
      if (rootRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  useEffect(() => {
    if (disabled) setOpen(false);
  }, [disabled]);

  const menu =
    open && coords
      ? createPortal(
          <ul
            ref={menuRef}
            id={listId}
            className={`${styles.menu} ${coords.openUp ? styles.menuUp : ""}`}
            role="listbox"
            aria-label={ariaLabel ?? "Role"}
            style={{
              top: coords.openUp ? undefined : coords.top,
              bottom: coords.openUp ? window.innerHeight - coords.top : undefined,
              left: coords.left,
              width: coords.width,
            }}
          >
            {ROLE_OPTIONS.map((option) => {
              const selected = option.value === value;
              return (
                <li key={option.value} role="presentation">
                  <button
                    type="button"
                    role="option"
                    aria-selected={selected}
                    className={styles.option}
                    data-selected={selected ? "true" : "false"}
                    data-testid={`${testId}-option-${option.label}`}
                    onClick={() => {
                      setOpen(false);
                      if (option.value !== value) onChange(option.value);
                    }}
                  >
                    <span className={styles.optionCopy}>
                      <span className={styles.optionLabel}>{option.label}</span>
                      <span className={styles.optionDetail}>{option.detail}</span>
                    </span>
                    {selected && <span className={styles.check} aria-hidden="true" />}
                  </button>
                </li>
              );
            })}
          </ul>,
          document.body,
        )
      : null;

  return (
    <div className={styles.root} ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        className={styles.trigger}
        data-testid={testId}
        data-value={String(value)}
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        disabled={disabled}
        onClick={() => setOpen((v) => !v)}
      >
        <span className={styles.triggerLabel}>{current.label}</span>
        <span className={styles.chevron} aria-hidden="true" />
      </button>
      {menu}
    </div>
  );
}
