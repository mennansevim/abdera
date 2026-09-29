using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations;

// The public demo admin account became the school's real admin account. Renaming it in
// place keeps its id, so audit_log rows and every created_by reference stay attached.
// The password is not touched here: it is changed from the settings screen after the
// first login, so it never lives in the repository. AdminBootstrapper no longer creates
// or resets a demo admin, otherwise it would bring back an admin with a public password.
//
// Hand-written (no model change), same pattern as `ResetToAdminOnly`. It is a no-op on an
// empty database or when the target address is already taken.
[DbContext(typeof(AbderaDbContext))]
[Migration("20260929120000_RenameDemoAdminAccount")]
public sealed class RenameDemoAdminAccount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE users
            SET email = 'admin@abderasanat.com', updated_at = now()
            WHERE email = 'demo.yonetici@abdera.com'
              AND NOT EXISTS (SELECT 1 FROM users WHERE email = 'admin@abderasanat.com');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE users
            SET email = 'demo.yonetici@abdera.com', updated_at = now()
            WHERE email = 'admin@abderasanat.com'
              AND NOT EXISTS (SELECT 1 FROM users WHERE email = 'demo.yonetici@abdera.com');
            """);
    }
}
