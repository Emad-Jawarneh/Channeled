/** Module ref for internal DnD — dragover cannot read custom payload. */
export const draggedItemIdsRef: { current: string[] } = { current: [] };

export const CHANNELED_ITEMS_MIME = "application/x-channeled-items";

export function setDraggedItemIds(ids: string[]): void {
  draggedItemIdsRef.current = ids;
}

export function clearDraggedItemIds(): void {
  draggedItemIdsRef.current = [];
}

export function getDraggedItemIds(): string[] {
  return draggedItemIdsRef.current;
}

export function isInternalItemsDrag(types: readonly string[] | DOMStringList | undefined): boolean {
  if (!types) return false;
  return Array.from(types as ArrayLike<string>).includes(CHANNELED_ITEMS_MIME);
}
