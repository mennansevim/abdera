namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/05-state-models.md. ALTERNATIVE_PROPOSED/PARENT_* durumları WhatsApp veli etkileşimi
// gerektirir (Phase 5) - şimdilik yalnızca PENDING->APPROVED/REJECTED yolu use-case'lerle
// desteklenir; diğerleri şemada hazır bekliyor (docs/10-decisions.md).
public enum LessonChangeRequestStatus
{
    Pending,
    Approved,
    Rejected,
    AlternativeProposed,
    ParentConfirmationPending,
    ParentAccepted,
    ParentRejected,
}
