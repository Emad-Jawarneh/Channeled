import { Navigate, Route, Routes, useParams } from "react-router-dom";
import { LoginPage } from "./features/login/LoginPage";
import { InvitePage } from "./features/invite/InvitePage";
import { ResetPage } from "./features/reset/ResetPage";
import { SharePage } from "./features/share/SharePage";
import { FileBrowser } from "./features/files/FileBrowser";
import { PersonalFilesPage } from "./features/personal/PersonalFilesPage";
import { SearchPage } from "./features/search/SearchPage";
import { MembersPage } from "./features/teams/MembersPage";
import { ChannelsSettingsPage } from "./features/teams/ChannelsSettingsPage";
import { JoinChannelPage } from "./features/teams/JoinChannelPage";
import { TextChannelPage } from "./features/teams/TextChannelPage";
import { ChannelKind, teamsApi } from "./api/channeled";
import {
  AdminAuditPage,
  AdminDashboardPage,
  AdminHealthPage,
  AdminStoragePage,
  AdminTeamsPage,
  AdminUsersPage,
} from "./features/admin/AdminPages";
import { useAuth } from "./auth/AuthContext";
import { useQuery } from "@tanstack/react-query";
import { AppShell } from "./components/AppShell";
import { LandingPage } from "./features/landing/LandingPage";
import { SignupPage } from "./features/signup/SignupPage";
import { WelcomePage } from "./features/welcome/WelcomePage";
import { RequireAuth } from "./auth/RequireAuth";

function ChannelRoute({ trash = false }: { trash?: boolean }) {
  const { team = "", channel = "general" } = useParams();
  const channels = useQuery({
    queryKey: ["channels", team],
    queryFn: () => teamsApi.channels(team),
    enabled: !!team,
  });

  if (channels.isLoading) {
    return <p className="loading-session">Loading…</p>;
  }

  const current = (channels.data ?? []).find((c) => c.name === channel);
  const kind = current?.kind ?? ChannelKind.File;
  if (kind === ChannelKind.Text && !trash) {
    return <TextChannelPage teamSlug={team} channelName={channel} />;
  }

  return <FileBrowser teamSlug={team} channelName={channel} trash={trash} />;
}

function MembersRoute() {
  const { team = "" } = useParams();
  return <MembersPage teamSlug={team} />;
}

function ChannelsRoute() {
  const { team = "" } = useParams();
  return <ChannelsSettingsPage teamSlug={team} />;
}

function RootGate() {
  const { user, loading } = useAuth();
  if (loading) {
    return <p className="loading-session">Loading…</p>;
  }
  if (!user) {
    return <LandingPage />;
  }
  return <HomeRedirect />;
}

function HomeRedirect() {
  const { user } = useAuth();
  const teams = useQuery({
    queryKey: ["teams"],
    queryFn: () => teamsApi.list(),
    enabled: !!user,
  });

  if (teams.isLoading) {
    return <p className="loading-session">Loading…</p>;
  }

  const first = teams.data?.[0];
  if (first) {
    return <Navigate to={`/t/${first.slug}/c/general`} replace />;
  }

  // Returning users with no team go to personal files; create-team onboarding is signup-only (/welcome).
  return <Navigate to="/me/files" replace />;
}

function WelcomeRoute() {
  const { user } = useAuth();
  const teams = useQuery({
    queryKey: ["teams"],
    queryFn: () => teamsApi.list(),
    enabled: !!user,
  });

  if (teams.isLoading) {
    return <WelcomePage loadingTeams />;
  }

  const first = teams.data?.[0];
  if (first) {
    return <Navigate to={`/t/${first.slug}/c/general`} replace />;
  }

  return <WelcomePage />;
}

export function App() {
  return (
    <Routes>
      <Route path="/" element={<RootGate />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/signup" element={<SignupPage />} />
      <Route
        path="/welcome"
        element={
          <RequireAuth>
            <WelcomeRoute />
          </RequireAuth>
        }
      />
      <Route path="/invite/:token" element={<InvitePage />} />
      <Route path="/reset/:token" element={<ResetPage />} />
      <Route path="/s/:token" element={<SharePage />} />
      <Route
        path="/join/:team/:channel"
        element={
          <RequireAuth>
            <JoinChannelPage />
          </RequireAuth>
        }
      />
      <Route
        path="/me/files"
        element={
          <RequireAuth>
            <PersonalFilesPage />
          </RequireAuth>
        }
      />
      <Route
        path="/me/files/recent"
        element={
          <RequireAuth>
            <PersonalFilesPage mode="recent" />
          </RequireAuth>
        }
      />
      <Route
        path="/me/files/starred"
        element={
          <RequireAuth>
            <PersonalFilesPage mode="starred" />
          </RequireAuth>
        }
      />
      <Route
        path="/me/files/trash"
        element={
          <RequireAuth>
            <PersonalFilesPage mode="trash" />
          </RequireAuth>
        }
      />
      <Route
        path="/search"
        element={
          <RequireAuth>
            <SearchPage />
          </RequireAuth>
        }
      />
      <Route
        path="/t/:team/c/:channel"
        element={
          <RequireAuth>
            <ChannelRoute />
          </RequireAuth>
        }
      />
      <Route
        path="/t/:team/c/:channel/trash"
        element={
          <RequireAuth>
            <ChannelRoute trash />
          </RequireAuth>
        }
      />
      <Route
        path="/t/:team/settings/members"
        element={
          <RequireAuth>
            <MembersRoute />
          </RequireAuth>
        }
      />
      <Route
        path="/t/:team/settings/channels"
        element={
          <RequireAuth>
            <ChannelsRoute />
          </RequireAuth>
        }
      />
      <Route
        path="/t/:team/settings/storage"
        element={
          <RequireAuth>
            <AppShell title="Storage">
              <p>Team storage is available via the API usage endpoint.</p>
            </AppShell>
          </RequireAuth>
        }
      />
      <Route
        path="/t/:team/settings/activity"
        element={
          <RequireAuth>
            <AppShell title="Activity">
              <p>Team activity feeds through admin audit for platform operators.</p>
            </AppShell>
          </RequireAuth>
        }
      />
      <Route
        path="/t/:team/settings/general"
        element={
          <RequireAuth>
            <AppShell title="General">
              <p>Team general settings.</p>
            </AppShell>
          </RequireAuth>
        }
      />
      <Route
        path="/admin/dashboard"
        element={
          <RequireAuth>
            <AdminDashboardPage />
          </RequireAuth>
        }
      />
      <Route
        path="/admin/users"
        element={
          <RequireAuth>
            <AdminUsersPage />
          </RequireAuth>
        }
      />
      <Route
        path="/admin/teams"
        element={
          <RequireAuth>
            <AdminTeamsPage />
          </RequireAuth>
        }
      />
      <Route
        path="/admin/audit"
        element={
          <RequireAuth>
            <AdminAuditPage />
          </RequireAuth>
        }
      />
      <Route
        path="/admin/health"
        element={
          <RequireAuth>
            <AdminHealthPage />
          </RequireAuth>
        }
      />
      <Route
        path="/admin/storage"
        element={
          <RequireAuth>
            <AdminStoragePage />
          </RequireAuth>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
