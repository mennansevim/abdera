namespace Abdera.Api.Modules.People.Domain;

// Aidat tutarını belirleyen tek eksen. Okulun ücret tarifesi enstrümana veya ders
// süresine göre değişmiyor - yalnızca dersin birebir mi grup mu olduğuna göre değişiyor
// (Birebir 4 ders 6.000 TL, Grup 4 ders 4.500 TL). Bu yüzden fiyat Instrument'a değil
// buraya bağlanır; ileride "grup piyano" veya "birebir resim" açılırsa model kırılmaz.
public enum CourseKind
{
    Individual,
    Group,
}
