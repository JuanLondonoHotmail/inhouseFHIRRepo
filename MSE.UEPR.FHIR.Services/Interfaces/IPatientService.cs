using Hl7.Fhir.Model;

using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.CORE.Models.Indexes;
using MSE.UEPR.FHIR.CORE.Models.InHouse;

namespace MSE.UEPR.FHIR.Services
{
    public interface IPatientService
    {

        Task<PatientInHouse?> GetPatientByCernerNumberAsync(string patientCernerNumber, bool bypassCache = false);

        Task<PatientInHouse?> GetPatientByIdAsync(string id, bool bypassCache = false);

        Task<PatientInHouse?> GetPatientByMrnNumberAsync(string patientMrnNumber, bool bypassCache = false);

        Task<PatientInHouse?> GetPatientByNhsNumberAsync(string id, bool bypassCache = false);

        Task<List<PatientInHouse>> GetPatientByUnitNumber(string UnitNumber, string ProviderCodes = "", bool bypassCache = false);

        Task<PatientsInHouseIndex?> GetPatientsAdvancedSearch(string providerCodes, bool currentInpatientOnly, PatientSearchValues SearchValue, bool SoundEx, bool bypassCache = false);

        Task<PatientInHouse?> PopulatePatientGateway(Patient? fhirPatient, bool bypassCache = false);

    }
}