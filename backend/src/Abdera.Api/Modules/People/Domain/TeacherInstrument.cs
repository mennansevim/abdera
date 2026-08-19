namespace Abdera.Api.Modules.People.Domain;

// docs/03-erd.md - People > teacher_instruments. Kompozit anahtar - bir öğretmen birden
// fazla enstrüman çalabilir (nadiren), bir enstrümanı birden fazla öğretmen öğretebilir.
public class TeacherInstrument
{
    public Guid TeacherId { get; private set; }
    public Guid InstrumentId { get; private set; }

    private TeacherInstrument() { }

    public static TeacherInstrument Create(Guid teacherId, Guid instrumentId) =>
        new() { TeacherId = teacherId, InstrumentId = instrumentId };
}
