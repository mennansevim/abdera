using Abdera.Api.Modules.People.Features;

namespace Abdera.Api.Modules.People;

public static class PeopleModule
{
    public static void MapPeopleModule(this WebApplication app)
    {
        app.MapInstruments();
        app.MapStudents();
        app.MapGuardians();
        app.MapLinkGuardianToStudent();
        app.MapTeachers();
        app.MapEnrollments();
        app.MapGuardianAuth();
        app.MapGuardianPortal();
        app.MapGuardianPortalData();
        app.MapAttentionNeededStudents();
        app.MapInstrumentMaintenance();
    }
}
