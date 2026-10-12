import type { ReactNode } from "react";

type IconProps = { className?: string };

function IconShell({ className, children }: IconProps & { children: ReactNode }) {
  return (
    <svg
      className={className}
      viewBox="0 0 48 48"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
    >
      {children}
    </svg>
  );
}

export function ChannelsIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <rect x="6" y="8" width="16" height="32" rx="4" fill="url(#g1)" opacity="0.9" />
      <rect x="26" y="8" width="16" height="20" rx="4" fill="url(#g2)" />
      <rect x="26" y="32" width="16" height="8" rx="3" fill="var(--color-accent-strong)" opacity="0.85" />
      <circle cx="14" cy="16" r="2" fill="#fff" />
      <circle cx="14" cy="24" r="2" fill="#fff" opacity="0.7" />
      <circle cx="14" cy="32" r="2" fill="#fff" opacity="0.45" />
      <defs>
        <linearGradient id="g1" x1="6" y1="8" x2="22" y2="40" gradientUnits="userSpaceOnUse">
          <stop stopColor="#4fd0df" />
          <stop offset="1" stopColor="#66acb7" />
        </linearGradient>
        <linearGradient id="g2" x1="26" y1="8" x2="42" y2="28" gradientUnits="userSpaceOnUse">
          <stop stopColor="#3ec4d4" />
          <stop offset="1" stopColor="#7ad8e0" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

export function PersonalIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <path
        d="M24 8c-4.4 0-8 3.4-8 7.6 0 5.2 4.2 9.6 8 12.4 3.8-2.8 8-7.2 8-12.4C32 11.4 28.4 8 24 8Z"
        fill="url(#g3)"
      />
      <rect x="12" y="30" width="24" height="10" rx="5" fill="var(--color-accent-strong)" />
      <circle cx="24" cy="35" r="2.5" fill="#4fd0df" />
      <defs>
        <linearGradient id="g3" x1="16" y1="8" x2="32" y2="28" gradientUnits="userSpaceOnUse">
          <stop stopColor="#7ad8e0" />
          <stop offset="1" stopColor="#3ec4d4" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

export function PermissionsIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <rect x="10" y="18" width="28" height="22" rx="5" fill="url(#g4)" />
      <path
        d="M17 18v-3.5a7 7 0 0 1 14 0V18"
        stroke="var(--color-accent-strong)"
        strokeWidth="3"
        strokeLinecap="round"
      />
      <circle cx="24" cy="29" r="3.5" fill="#fff" />
      <path d="M24 32.5v4" stroke="#fff" strokeWidth="2.5" strokeLinecap="round" />
      <defs>
        <linearGradient id="g4" x1="10" y1="18" x2="38" y2="40" gradientUnits="userSpaceOnUse">
          <stop stopColor="#4fd0df" />
          <stop offset="1" stopColor="#2a9aab" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

export function FilesIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <path
        d="M14 8h12l8 8v22a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4V12a4 4 0 0 1 4-4Z"
        fill="url(#g5)"
      />
      <path d="M26 8v8h8" fill="var(--color-soft)" />
      <path d="M18 26h12M18 32h8" stroke="#fff" strokeWidth="2.5" strokeLinecap="round" />
      <circle cx="34" cy="34" r="9" fill="var(--color-accent-strong)" />
      <path d="M34 30v5l3 2" stroke="#4fd0df" strokeWidth="2.2" strokeLinecap="round" />
      <defs>
        <linearGradient id="g5" x1="10" y1="8" x2="34" y2="42" gradientUnits="userSpaceOnUse">
          <stop stopColor="#7ad8e0" />
          <stop offset="1" stopColor="#66acb7" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

export function AuditIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <rect x="8" y="10" width="32" height="28" rx="5" fill="url(#g6)" />
      <path d="M14 20h12M14 26h20M14 32h10" stroke="#fff" strokeWidth="2.4" strokeLinecap="round" />
      <circle cx="34" cy="14" r="8" fill="var(--color-accent-strong)" />
      <path
        d="M31 14.5 33.2 16.8 37.5 11.8"
        stroke="#4fd0df"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <defs>
        <linearGradient id="g6" x1="8" y1="10" x2="40" y2="38" gradientUnits="userSpaceOnUse">
          <stop stopColor="#3ec4d4" />
          <stop offset="1" stopColor="#66acb7" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

export function HostingIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <rect x="8" y="26" width="32" height="12" rx="4" fill="var(--color-accent-strong)" />
      <rect x="12" y="12" width="24" height="14" rx="4" fill="url(#g7)" />
      <circle cx="18" cy="32" r="2" fill="#4fd0df" />
      <circle cx="24" cy="32" r="2" fill="#66acb7" />
      <path d="M18 12V9a6 6 0 0 1 12 0v3" stroke="#3ec4d4" strokeWidth="2.5" strokeLinecap="round" />
      <path d="M20 19h8" stroke="#fff" strokeWidth="2.2" strokeLinecap="round" />
      <defs>
        <linearGradient id="g7" x1="12" y1="12" x2="36" y2="26" gradientUnits="userSpaceOnUse">
          <stop stopColor="#7ad8e0" />
          <stop offset="1" stopColor="#2a9aab" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

/** Hierarchy cards — unique gradient ids so they can sit beside feature icons. */
export function TeamsIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <circle cx="16" cy="16" r="6" fill="url(#hTeamA)" />
      <circle cx="32" cy="16" r="6" fill="url(#hTeamB)" />
      <circle cx="24" cy="28" r="7" fill="var(--color-accent-strong)" />
      <path
        d="M8 38c1.5-5 5.5-8 8-8s6.5 3 8 8M24 38c1.5-5 5.5-8 8-8s6.5 3 8 8"
        stroke="url(#hTeamA)"
        strokeWidth="3"
        strokeLinecap="round"
      />
      <defs>
        <linearGradient id="hTeamA" x1="10" y1="10" x2="22" y2="22" gradientUnits="userSpaceOnUse">
          <stop stopColor="#4fd0df" />
          <stop offset="1" stopColor="#66acb7" />
        </linearGradient>
        <linearGradient id="hTeamB" x1="26" y1="10" x2="38" y2="22" gradientUnits="userSpaceOnUse">
          <stop stopColor="#7ad8e0" />
          <stop offset="1" stopColor="#3ec4d4" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

export function HierarchyChannelsIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <rect x="8" y="10" width="14" height="28" rx="4" fill="url(#hChanA)" />
      <rect x="26" y="10" width="14" height="16" rx="4" fill="url(#hChanB)" />
      <rect x="26" y="30" width="14" height="8" rx="3" fill="var(--color-accent-strong)" />
      <circle cx="15" cy="18" r="1.8" fill="#fff" />
      <circle cx="15" cy="24" r="1.8" fill="#fff" opacity="0.75" />
      <circle cx="15" cy="30" r="1.8" fill="#fff" opacity="0.5" />
      <defs>
        <linearGradient id="hChanA" x1="8" y1="10" x2="22" y2="38" gradientUnits="userSpaceOnUse">
          <stop stopColor="#4fd0df" />
          <stop offset="1" stopColor="#2a9aab" />
        </linearGradient>
        <linearGradient id="hChanB" x1="26" y1="10" x2="40" y2="26" gradientUnits="userSpaceOnUse">
          <stop stopColor="#7ad8e0" />
          <stop offset="1" stopColor="#66acb7" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}

export function HierarchyFilesIcon({ className }: IconProps) {
  return (
    <IconShell className={className}>
      <path
        d="M12 8h14l8 8v20a4 4 0 0 1-4 4H12a4 4 0 0 1-4-4V12a4 4 0 0 1 4-4Z"
        fill="url(#hFileA)"
      />
      <path d="M26 8v8h8" fill="var(--color-soft)" />
      <path d="M16 24h12M16 30h8" stroke="#fff" strokeWidth="2.4" strokeLinecap="round" />
      <rect x="28" y="28" width="14" height="12" rx="3" fill="var(--color-accent-strong)" />
      <path d="M32 34h6M35 31v6" stroke="#4fd0df" strokeWidth="2" strokeLinecap="round" />
      <defs>
        <linearGradient id="hFileA" x1="8" y1="8" x2="34" y2="40" gradientUnits="userSpaceOnUse">
          <stop stopColor="#7ad8e0" />
          <stop offset="1" stopColor="#66acb7" />
        </linearGradient>
      </defs>
    </IconShell>
  );
}
