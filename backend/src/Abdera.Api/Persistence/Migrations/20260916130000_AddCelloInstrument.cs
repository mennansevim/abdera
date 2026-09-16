using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations;

// Çello enstrümanı. Enstrüman listesi migration ile seed edilir (009_seed_reference_data'dan
// beri süregelen desen; Resim de RedesignTuitionAndDues içinde böyle eklendi) çünkü
// arayüzde enstrüman ekleme ekranı yok - `POST /api/instruments` var ama hiçbir ekran
// onu çağırmıyor.
//
// Ücret tarifesi tarafında YAPILACAK BİR ŞEY YOK: yeni modelde fiyat enstrümana değil
// dersin birebir/grup olmasına bağlı (docs/10-decisions.md H1). Eski modelde her yeni
// enstrüman için ayrıca fiyat listesi kalemi açmak gerekirdi.
[DbContext(typeof(AbderaDbContext))]
[Migration("20260916130000_AddCelloInstrument")]
public sealed class AddCelloInstrument : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO instruments (id, name, code)
            SELECT gen_random_uuid(), 'Çello', 'CELLO'
            WHERE NOT EXISTS (SELECT 1 FROM instruments WHERE code = 'CELLO');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Yalnızca hiç kullanılmadıysa geri alınabilir - bir kurs kaydı buna bağlıysa
        // enstrümanı silmek o kaydı kopuk bırakırdı.
        migrationBuilder.Sql("""
            DELETE FROM instruments
            WHERE code = 'CELLO'
              AND NOT EXISTS (SELECT 1 FROM enrollments e WHERE e.instrument_id = instruments.id);
            """);
    }
}
