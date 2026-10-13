import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { MemberRole, teamsApi, type TeamMember } from "../../api/channeled";
import { useAuth } from "../../auth/AuthContext";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";
import { ConfirmDialog } from "./ConfirmDialog";

const roleLabel = ["Viewer", "Member", "Admin", "Owner"];

type Confirm = { kind: "remove"; member: TeamMember } | { kind: "leave" } | null;

export function MembersPage({ teamSlug }: { teamSlug: string }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const { user } = useAuth();
  const [confirm, setConfirm] = useState<Confirm>(null);
  const [error, setError] = useState<string | null>(null);

  const members = useQuery({
    queryKey: ["members", teamSlug],
    queryFn: () => teamsApi.members(teamSlug),
  });
  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });

  const myRole = team.data?.myRole ?? MemberRole.Viewer;
  const isOwner = myRole === MemberRole.Owner;
  const isAdmin = myRole === MemberRole.Admin;

  function canRemove(m: TeamMember): boolean {
    if (m.userId === user?.id) return false;
    if (isOwner) return true;
    return isAdmin && (m.role === MemberRole.Viewer || m.role === MemberRole.Member);
  }

  const changeRole = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: number }) =>
      teamsApi.changeRole(teamSlug, userId, role),
    onSuccess: () => setError(null),
    onError: (e) => setError(e instanceof Error ? e.message : "Could not change role."),
    onSettled: () => void qc.invalidateQueries({ queryKey: ["members", teamSlug] }),
  });

  const remove = useMutation({
    mutationFn: (userId: string) => teamsApi.removeMember(teamSlug, userId),
    onSuccess: async () => {
      setConfirm(null);
      setError(null);
      await qc.invalidateQueries({ queryKey: ["members", teamSlug] });
    },
    onError: (e) => setError(e instanceof Error ? e.message : "Could not remove member."),
  });

  const leave = useMutation({
    mutationFn: () => teamsApi.leave(teamSlug),
    onSuccess: async () => {
      setConfirm(null);
      qc.removeQueries({ queryKey: ["team", teamSlug] });
      qc.removeQueries({ queryKey: ["channels", teamSlug] });
      qc.removeQueries({ queryKey: ["members", teamSlug] });
      await qc.invalidateQueries({ queryKey: ["teams"] });
      navigate("/", { replace: true });
    },
    onError: (e) => setError(e instanceof Error ? e.message : "Could not leave team."),
  });

  const busy = remove.isPending || leave.isPending;

  return (
    <AppShell title={`${teamSlug} · Members`}>
      {error && (
        <p role="alert" data-testid="members-error" style={{ color: "var(--color-danger)" }}>
          {error}
        </p>
      )}

      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="members-table">
          <thead>
            <tr>
              <th scope="col">Member</th>
              <th scope="col">Role</th>
              {isOwner && <th scope="col">Change role</th>}
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {(members.data ?? []).map((m) => (
              <tr key={m.userId} data-testid={`member-${m.email}`}>
                <td>
                  {m.displayName} ({m.email})
                </td>
                <td>{roleLabel[m.role] ?? m.role}</td>
                {isOwner && (
                  <td>
                    {m.role !== MemberRole.Owner && m.userId !== user?.id && (
                      <select
                        data-testid={`role-${m.email}`}
                        aria-label={`Role for ${m.email}`}
                        value={m.role}
                        disabled={changeRole.isPending}
                        onChange={(e) =>
                          void changeRole.mutateAsync({
                            userId: m.userId,
                            role: Number(e.target.value),
                          })
                        }
                      >
                        <option value={MemberRole.Viewer}>Viewer</option>
                        <option value={MemberRole.Member}>Member</option>
                        <option value={MemberRole.Admin}>Admin</option>
                      </select>
                    )}
                  </td>
                )}
                <td>
                  {canRemove(m) && (
                    <Button
                      type="button"
                      data-testid={`remove-${m.email}`}
                      aria-label={`Remove ${m.email}`}
                      onClick={() => {
                        setError(null);
                        setConfirm({ kind: "remove", member: m });
                      }}
                    >
                      Remove
                    </Button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {team.data && !isOwner && (
        <p>
          <Button
            type="button"
            data-testid="leave-team"
            onClick={() => {
              setError(null);
              setConfirm({ kind: "leave" });
            }}
          >
            Leave team
          </Button>
        </p>
      )}

      <ConfirmDialog
        open={confirm !== null}
        title={confirm?.kind === "leave" ? "Leave team?" : "Remove member?"}
        confirmLabel={confirm?.kind === "leave" ? "Leave team" : "Remove member"}
        busy={busy}
        error={error}
        testId="members-confirm"
        onClose={() => {
          if (!busy) setConfirm(null);
        }}
        onConfirm={() => {
          if (confirm?.kind === "leave") void leave.mutateAsync();
          else if (confirm?.kind === "remove") void remove.mutateAsync(confirm.member.userId);
        }}
      >
        {confirm?.kind === "leave" ? (
          <p>You will lose access to this team&apos;s channels and files. You can rejoin with a join link.</p>
        ) : confirm?.kind === "remove" ? (
          <p>
            {confirm.member.displayName} ({confirm.member.email}) will lose access to this team.
          </p>
        ) : null}
      </ConfirmDialog>
    </AppShell>
  );
}
