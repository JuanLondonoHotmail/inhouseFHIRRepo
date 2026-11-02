using Hl7.Fhir.Model;

using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Models.InHouse;

namespace MSE.UEPR.FHIR.Services.Interfaces
{
    public interface IAllergyIntoleranceService
    {
        Task<List<AllergyInhouse>?> GetAllergiesByPatient(string id, AllergyCategories category, bool bypassCache = false);
    }
}