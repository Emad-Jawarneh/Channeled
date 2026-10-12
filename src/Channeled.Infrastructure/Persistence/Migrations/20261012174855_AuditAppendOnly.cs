using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Channeled.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION deny_audit_events_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                  RAISE EXCEPTION 'audit_events is append-only';
                END;
                $$;

                DROP TRIGGER IF EXISTS trg_audit_events_no_update ON audit_events;
                DROP TRIGGER IF EXISTS trg_audit_events_no_delete ON audit_events;

                CREATE TRIGGER trg_audit_events_no_update
                  BEFORE UPDATE ON audit_events
                  FOR EACH ROW EXECUTE FUNCTION deny_audit_events_mutation();

                CREATE TRIGGER trg_audit_events_no_delete
                  BEFORE DELETE ON audit_events
                  FOR EACH ROW EXECUTE FUNCTION deny_audit_events_mutation();

                DO $role$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'channeled') THEN
                    REVOKE UPDATE, DELETE ON TABLE audit_events FROM channeled;
                    GRANT SELECT, INSERT ON TABLE audit_events TO channeled;
                  END IF;
                END
                $role$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_events_no_update ON audit_events;
                DROP TRIGGER IF EXISTS trg_audit_events_no_delete ON audit_events;
                DROP FUNCTION IF EXISTS deny_audit_events_mutation();

                DO $role$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'channeled') THEN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE audit_events TO channeled;
                  END IF;
                END
                $role$;
                """);
        }
    }
}
