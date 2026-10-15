using Abdera.Api.Modules.People.Features;
using Abdera.Api.Shared.Modules;

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
        app.MapDeletePerson();
        app.MapStudentDeletionRequests();
        app.MapEnrollments();

        // Veli girişi ve /parent portalının uçları "parent-portal" modülüdür. Velinin kendisi
        // (iletişim bilgisi, öğrenci bağı) çekirdekte kalır: WhatsApp bildirimleri ona gider.
        var parentPortal = app.MapModule(ModuleKeys.ParentPortal);
        parentPortal.MapGuardianAuth();
        parentPortal.MapGuardianPortal();
        parentPortal.MapGuardianPortalData();
        app.MapAttentionNeededStudents();
        app.MapInstrumentMaintenance();
    }
}
