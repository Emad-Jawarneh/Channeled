import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { MemberRole, teamsApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";

const roleLabel = ["Viewer", "Member", "Admin", "Owner"];

export function MembersPage({ teamSlug }: { teamSlug: string }) {
  const qc = useQueryClient();
  const [email, setEmail] = useState("");
  const [inviteToken, setInviteToken] = useState<string | null>(null);

  const members = useQuery({
    queryKey: ["members", teamSlug],
    queryFn: () => teamsApi.members(teamSlug),
  });

  const invite = useMutation({
    mutationFn: () => teamsApi.invite(teamSlug, email, MemberRole.Member),
    onSuccess: (dto) => {
      setInviteToken(dto.rawToken ?? null);
      void qc.invalidateQueries({ queryKey: ["members", teamSlug] });
    },
  });

  const changeRole = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: number }) =>
      teamsApi.changeRole(teamSlug, userId, role),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["members", teamSlug] }),
  });

  return (
    <AppShell title={`${teamSlug} · Members`}>
      <form
        className={styles.toolbar}
        onSubmit={(e) => {
          e.preventDefault();
          void invite.mutateAsync();
        }}
      >
        <label>
          Invite email
          <input
            data-testid="invite-email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </label>
        <Button type="submit" variant="primary" data-testid="invite-submit">
          Invite
        </Button>
      </form>
      {inviteToken && (
        <p className={styles.lede} data-testid="invite-token">
          Invite token: <code>{inviteToken}</code> — open /invite/{inviteToken}
        </p>
      )}

      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="members-table">
          <thead>
            <tr>
              <th scope="col">Member</th>
              <th scope="col">Role</th>
              <th scope="col">Change role</th>
            </tr>
          </thead>
          <tbody>
            {(members.data ?? []).map((m) => (
              <tr key={m.userId} data-testid={`member-${m.email}`}>
                <td>
                  {m.displayName} ({m.email})
                </td>
                <td>{roleLabel[m.role] ?? m.role}</td>
                <td>
                  {m.role !== MemberRole.Owner && (
                    <select
                      data-testid={`role-${m.email}`}
                      value={m.role}
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
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </AppShell>
  );
}
