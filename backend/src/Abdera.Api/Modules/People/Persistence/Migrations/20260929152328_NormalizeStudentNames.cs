using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeStudentNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kayıtlı öğrenci adlarını Shared/PersonNameFormatter ile aynı biçime getirir
            // ("miray SEVİM" -> "Miray Sevim"). Büyük/küçük harf çevrimi upper()/lower() ile
            // DEĞİL translate() ile yapılır: upper('i') veritabanının locale'ine göre 'I' verir,
            // Türkçede 'İ' olmalı. Harf tablosu PersonNameFormatter'daki eşlemeyle aynı.
            migrationBuilder.Sql("""
                CREATE FUNCTION pg_temp.tr_title(input text) RETURNS text LANGUAGE sql IMMUTABLE AS $fn$
                    SELECT coalesce(string_agg(
                        CASE WHEN part IN (' ', '-') THEN part
                             ELSE translate(left(part, 1),
                                      'abcçdefgğhıijklmnoöprsştuüvyzqwxâîû',
                                      'ABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZQWXÂÎÛ')
                                  || translate(substr(part, 2),
                                      'ABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZQWXÂÎÛ',
                                      'abcçdefgğhıijklmnoöprsştuüvyzqwxâîû')
                        END, '' ORDER BY ord), '')
                    FROM regexp_matches(regexp_replace(btrim(input), '\s+', ' ', 'g'), '[^ -]+|[ -]', 'g')
                         WITH ORDINALITY AS m(parts, ord),
                         LATERAL (SELECT parts[1] AS part) p
                $fn$;
                """);
            migrationBuilder.Sql("""
                UPDATE students
                SET first_name = pg_temp.tr_title(first_name),
                    last_name = pg_temp.tr_title(last_name),
                    updated_at = now()
                WHERE first_name IS DISTINCT FROM pg_temp.tr_title(first_name)
                   OR last_name IS DISTINCT FROM pg_temp.tr_title(last_name);
                """);
            migrationBuilder.Sql("DROP FUNCTION pg_temp.tr_title(text);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Özgün yazım saklanmadığı için geri alınamaz; ad biçimlendirmesi veri kaybı değildir.
        }
    }
}
