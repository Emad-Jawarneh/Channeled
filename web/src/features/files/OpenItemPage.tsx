import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { itemsApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { pathForLocation } from "./locate";
import styles from "./FileBrowser.module.css";

/** `/open/:itemId` — one link target for chat file chips and search hits; resolves where the item lives now. */
export function OpenItemPage() {
  const { itemId = "" } = useParams();
  const navigate = useNavigate();
  const located = useQuery({
    queryKey: ["locate", itemId],
    queryFn: ({ signal }) => itemsApi.locate(itemId, signal),
    retry: false,
  });

  useEffect(() => {
    if (located.data) {
      navigate(pathForLocation(located.data), { replace: true });
    }
  }, [located.data, navigate]);

  return (
    <AppShell title="Open item">
      {located.isError ? (
        <div data-testid="open-item-missing">
          <h2 className={styles.heading}>Item unavailable</h2>
          <p className={styles.lede}>It may have been deleted, or you no longer have access to it.</p>
          <p>
            <Link to="/">Go home</Link>
          </p>
        </div>
      ) : (
        <p className={styles.lede} role="status">
          Opening…
        </p>
      )}
    </AppShell>
  );
}
