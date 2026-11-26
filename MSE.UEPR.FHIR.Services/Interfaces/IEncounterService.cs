using MSE.UEPR.FHIR.CORE.Models;

namespace MSE.UEPR.FHIR.Services.Interfaces;

public interface IEncounterService
{
    Task<List<EncounterGateway>?> SearchInpatientEncountersAsync(
        string patientId,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        string? locationId = null);
}
