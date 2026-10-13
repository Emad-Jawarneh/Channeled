/** Mirrors `ChannelNameRules` on the server. */
export const CHANNEL_NAME_MAX = 200;

export function validateChannelName(name: string): string | null {
  const trimmed = name.trim();
  if (!trimmed) return "Channel name is required.";
  if (trimmed.length > CHANNEL_NAME_MAX) {
    return `Channel name must be at most ${CHANNEL_NAME_MAX} characters.`;
  }
  if (trimmed.includes("\0")) return "Channel name contains invalid characters.";
  return null;
}
