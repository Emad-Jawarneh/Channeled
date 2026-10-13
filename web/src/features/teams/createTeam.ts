import { teamsApi, type Team } from "../../api/channeled";

/** Creates a team and its default general channel. */
export async function createTeamWithGeneral(name: string): Promise<Team> {
  const team = await teamsApi.create(name.trim());
  await teamsApi.createChannel(team.slug, {
    name: "general",
    visibility: 0,
    kind: 0,
  });
  return team;
}
