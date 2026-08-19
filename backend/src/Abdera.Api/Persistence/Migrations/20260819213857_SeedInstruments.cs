using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    // docs/08-migrations.md - seed verisi. INSERT ... ON CONFLICT ile tekrar çalıştırılabilir
    // (abdera-migration skill'i - "Down()'unu gerçekten geri alınabilir yap" kuralı).
    /// <inheritdoc />
    public partial class SeedInstruments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO instruments (id, name, code) VALUES
                    (gen_random_uuid(), 'Piyano', 'PIANO'),
                    (gen_random_uuid(), 'Gitar', 'GUITAR'),
                    (gen_random_uuid(), 'Keman', 'VIOLIN'),
                    (gen_random_uuid(), 'Bateri', 'DRUMS')
                ON CONFLICT (code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM instruments WHERE code IN ('PIANO', 'GUITAR', 'VIOLIN', 'DRUMS');
                """);
        }
    }
}
