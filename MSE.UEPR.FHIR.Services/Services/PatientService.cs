using System.Text.Json;
using System.Text.Json.Nodes;

using AutoMapper;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

using Microsoft.Extensions.Logging;

using MSB.Common.Model;

using MSE.UEPR.FHIR.CORE.Config;
using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Helpers;
using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.CORE.Models.Indexes;
using MSE.UEPR.FHIR.CORE.Models.InHouse;
using MSE.UEPR.FHIR.CORE.Redis;
using MSE.UEPR.FHIR.CORE.Security;
using MSE.UEPR.FHIR.Services.Interfaces;

using FhirPatient = Hl7.Fhir.Model.Patient;
using Task = System.Threading.Tasks.Task;

namespace MSE.UEPR.FHIR.Services.Services
{
    public class PatientService(
     ILogger<PatientService> logger,
     IMapper mapper,
     AppSettings appSettings,
     Authentication authentication,
     IOrganizationService organizationService,
     IAllergyIntoleranceService allergyIntoleranceService,
     IRedisCache redisCache,
     IHttpClientToCerner client)
     : IPatientService
    {

      

        /// <summary>
        /// Gets the patient of type FhirPatient from the Cerner number which is stored in the identifier field of the patient resource in Cerner.
        /// The identifier system is defined by the OID specified in the application settings. The method first checks the cache for a patient with the given NHS number and returns it
        /// if found. If not found or if bypassing the cache,
        /// it queries the FHIR server for a patient with the specified NHS number, caches the result, and returns it. If no patient is found, it returns null.
        /// </summary>
        /// <param name="patientMrnNumber"></param>
        /// <param name="bypassCache"></param>
        /// <returns></returns>
        /// <exception cref="ApplicationException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<PatientInHouse?> GetPatientByCernerNumberAsync(string patientCernerNumber, bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientCernerNumber))
            {
                return null;
            }

            var cacheKey = $"{Entity.Patient}_PatientCernerNumber:{patientCernerNumber}";
            var oid = appSettings.Cerner_Settings.OID_Cerner_Patients;

            try
            {
                using (logger.BeginScope("GetPatientByCernerNumberAsync"))
                {
                    if (!bypassCache)
                    {
                        var jsonString = await redisCache.GetCacheDataAsync<string>(cacheKey).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(jsonString))
                        {
                            return JsonSerializer.Deserialize<PatientInHouse>(jsonString);
                        }
                    }

                    var bundleString = await client.SearchAsync(Entity.Patient, $"identifier={oid}|{patientCernerNumber}", logger)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(bundleString))
                    {
                        return null;
                    }

                    var parser = new FhirJsonParser();
                    var bundle = parser.Parse<Bundle>(bundleString);

                    var fhirPatient = bundle?.Entry?
                        .Select(e => e.Resource)
                        .OfType<FhirPatient>()
                        .FirstOrDefault();

                    if (fhirPatient is null)
                    {
                        return null;
                    }

                    try
                    {
                        var patientGateway = await PopulatePatientGateway(fhirPatient, bypassCache).ConfigureAwait(false);
                        if (patientGateway is null)
                        {
                            return null;
                        }

                        var jsonString = JsonSerializer.Serialize(patientGateway);
                        var duration = DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.PatientCacheDurationInMinutes);

                        await redisCache.SetCacheDataAsync(cacheKey, jsonString, duration).ConfigureAwait(false);

                        return patientGateway;
                    }
                    catch (AutoMapperMappingException ex)
                    {
                        logger.LogError(ex, "An AutoMapper runtime mapping error occurred processing patient profile mapping context");
                        throw new ApplicationException("Data processing failed due to mapping error.", ex);
                    }
                }
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                logger.LogError(ex, "Error occurred executing GetPatientByCernerNumberAsync for identifier {PatientCernerNumber}", patientCernerNumber);
                throw;
            }
        }

        public async Task<PatientInHouse?> GetPatientByIdAsync(string patientId, bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientId))
            {
                return null;
            }

            var cacheKey = $"{Entity.Patient}_PatientID:{patientId}";

            try
            {
                using (logger.BeginScope("GetPatientByIdAsync"))
                {
                    if (!bypassCache)
                    {
                        var jsonString = await redisCache.GetCacheDataAsync<string>(cacheKey).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(jsonString))
                        {
                            return JsonSerializer.Deserialize<PatientInHouse>(jsonString);
                        }
                    }

                    var bundleString = await client.SearchAsync(Entity.Patient, $"_id={patientId}&_include=Patient:generalPractitioner", logger)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(bundleString))
                    {
                        return null;
                    }

                    var parser = new FhirJsonParser();
                    var bundle = parser.Parse<Bundle>(bundleString);

                    var fhirPatient = bundle?.Entry?
                        .Select(e => e.Resource)
                        .OfType<FhirPatient>()
                        .FirstOrDefault();

                    if (fhirPatient is null)
                    {
                        return null;
                    }

                    try
                    {
                        var patientGateway = await PopulatePatientGateway(fhirPatient, bypassCache).ConfigureAwait(false);
                        if (patientGateway is null)
                        {
                            return null;
                        }

                        var jsonString = JsonSerializer.Serialize(patientGateway);
                        var duration = DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.PatientCacheDurationInMinutes);

                        await redisCache.SetCacheDataAsync(cacheKey, jsonString, duration).ConfigureAwait(false);

                        return patientGateway;
                    }
                    catch (AutoMapperMappingException ex)
                    {
                        logger.LogError(ex, "An AutoMapper runtime mapping error occurred processing patient ID resource conversion");
                        throw new ApplicationException("Data processing failed due to mapping error.", ex);
                    }
                }
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                logger.LogError(ex, "Error occurred executing GetPatientByIdAsync for identity reference {PatientId}", patientId);
                throw;
            }
        }

        /// <summary>
        /// Gets the patient of type FhirPatient from the MRN number which is stored in the identifier field of the patient resource in Cerner.
        /// The identifier system is defined by the OID specified in the application settings. The method first checks the cache for a patient with the given NHS number and returns it
        /// if found. If not found or if bypassing the cache,
        /// it queries the FHIR server for a patient with the specified NHS number, caches the result, and returns it. If no patient is found, it returns null.
        /// </summary>
        /// <param name="patientMrnNumber"></param>
        /// <param name="bypassCache"></param>
        /// <returns></returns>
        /// <exception cref="ApplicationException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<PatientInHouse?> GetPatientByMrnNumberAsync(string patientMrnNumber, bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientMrnNumber))
            {
                return null;
            }

            var cacheKey = $"{Entity.Patient}_PatientMrnNumber:{patientMrnNumber}";
            var oid = appSettings.Cerner_Settings.OID_MRN_Patients;

            try
            {
                using (logger.BeginScope("GetPatientByMrnNumberAsync"))
                {
                    if (!bypassCache)
                    {
                        var jsonString = await redisCache.GetCacheDataAsync<string>(cacheKey).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(jsonString))
                        {
                            return JsonSerializer.Deserialize<PatientInHouse>(jsonString);
                        }
                    }

                    var bundleString = await client.SearchAsync(Entity.Patient, $"identifier={oid}|{patientMrnNumber}", logger)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(bundleString))
                    {
                        return null;
                    }

                    var parser = new FhirJsonParser();
                    var bundle = parser.Parse<Bundle>(bundleString);

                    var fhirPatient = bundle?.Entry?
                        .Select(e => e.Resource)
                        .OfType<FhirPatient>()
                        .FirstOrDefault();

                    if (fhirPatient is null)
                    {
                        return null;
                    }

                    try
                    {
                        var patientGateway = await PopulatePatientGateway(fhirPatient, bypassCache).ConfigureAwait(false);
                        if (patientGateway is null)
                        {
                            return null;
                        }

                        var jsonString = JsonSerializer.Serialize(patientGateway);
                        var duration = DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.PatientCacheDurationInMinutes);

                        await redisCache.SetCacheDataAsync(cacheKey, jsonString, duration).ConfigureAwait(false);

                        return patientGateway;
                    }
                    catch (AutoMapperMappingException ex)
                    {
                        logger.LogError(ex, "An AutoMapper runtime mapping error occurred processing patient MRN profile conversion");
                        throw new ApplicationException("Data processing failed due to mapping error.", ex);
                    }
                }
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                logger.LogError(ex, "Error occurred executing GetPatientByMrnNumberAsync for MRN identifier {PatientMrnNumber}", patientMrnNumber);
                throw;
            }
        }

        /// <summary>
        /// Gets the patient of type FhirPatient from the NHS number which is stored in the identifier field of the patient resource in Cerner.
        /// The identifier system is defined by the OID specified in the application settings. The method first checks the cache for a patient with the given NHS number and returns it
        /// if found. If not found or if bypassing the cache,
        /// it queries the FHIR server for a patient with the specified NHS number, caches the result, and returns it. If no patient is found, it returns null.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="bypassCache"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<PatientInHouse?> GetPatientByNhsNumberAsync(string patientNhsNumber, bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientNhsNumber))
            {
                return null;
            }

            var cacheKey = $"{Entity.Patient}_PatientNhsNumber:{patientNhsNumber}";
            var oid = appSettings.Cerner_Settings.OID_NewPatients;

            try
            {
                using (logger.BeginScope("GetPatientByNhsNumberAsync"))
                {
                    if (!bypassCache)
                    {
                        var jsonString = await redisCache.GetCacheDataAsync<string>(cacheKey).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(jsonString))
                        {
                            return JsonSerializer.Deserialize<PatientInHouse>(jsonString);
                        }
                    }

                    var bundleString = await client.SearchAsync(Entity.Patient, $"identifier={oid}|{patientNhsNumber}", logger)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(bundleString))
                    {
                        return null;
                    }

                    var parser = new FhirJsonParser();
                    var bundle = parser.Parse<Bundle>(bundleString);

                    var fhirPatient = bundle?.Entry?
                        .Select(e => e.Resource)
                        .OfType<FhirPatient>()
                        .FirstOrDefault();

                    if (fhirPatient is null)
                    {
                        return null;
                    }

                    try
                    {
                        var patientGateway = await PopulatePatientGateway(fhirPatient, bypassCache).ConfigureAwait(false);
                        if (patientGateway is null)
                        {
                            return null;
                        }

                        var jsonString = JsonSerializer.Serialize(patientGateway);
                        var duration = DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.PatientCacheDurationInMinutes);

                        await redisCache.SetCacheDataAsync(cacheKey, jsonString, duration).ConfigureAwait(false);

                        return patientGateway;
                    }
                    catch (AutoMapperMappingException ex)
                    {
                        logger.LogError(ex, "An AutoMapper runtime mapping error occurred processing patient NHS profile conversion");
                        throw new ApplicationException("Data processing failed due to mapping error.", ex);
                    }
                }
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                logger.LogError(ex, "Error occurred executing GetPatientByNhsNumberAsync for NHS identifier {PatientNhsNumber}", patientNhsNumber);
                throw;
            }
        }

        /// <summary>
        /// Retrieves a list of patients matching the specified unit number, searching by multiple identifier types.
        /// </summary>
        /// <remarks>The method attempts to locate patients using the provided unit number as an
        /// identifier, searching by patient ID and NHS number. If a matching patient is found for each identifier type,
        /// it is added to the result list. The MRN number is also checked, but currently not included in the results to
        /// avoid potential duplication.</remarks>
        /// <param name="UnitNumber">The unit number to search for. This value is used as an identifier to locate patients by patient ID, NHS
        /// number, and MRN number.</param>
        /// <param name="ProviderCodes">An optional comma-separated list of provider codes to filter the search. If not specified, all providers are
        /// considered.</param>
        /// <param name="bypassCache">A value indicating whether to bypass the cache when retrieving patient information. Set to <see
        /// langword="true"/> to force fresh data retrieval; otherwise, <see langword="false"/> uses cached data if
        /// available.</param>
        /// <returns>A list of <see cref="MSB.Common.Model.Patient"/> objects matching the specified unit number. The list may
        /// contain zero or more patients depending on the search results.</returns>
        public async Task<List<PatientInHouse>> GetPatientByUnitNumber(string unitNumber, string providerCodes = "", bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(unitNumber))
            {
                return [];
            }

            try
            {
                var fhirIdTask = GetPatientByIdAsync(unitNumber, bypassCache);
                var nhsNumberTask = GetPatientByNhsNumberAsync(unitNumber, bypassCache);
                var mrnNumberTask = GetPatientByMrnNumberAsync(unitNumber, bypassCache);

                await Task.WhenAll(fhirIdTask, nhsNumberTask, mrnNumberTask).ConfigureAwait(false);

                var patients = new List<PatientInHouse>();

                if (fhirIdTask.Result is not null)
                {
                    patients.Add(fhirIdTask.Result);
                }

                if (nhsNumberTask.Result is not null && !patients.Any(p => p.PatientId == nhsNumberTask.Result.PatientId))
                {
                    patients.Add(nhsNumberTask.Result);
                }

                if (mrnNumberTask.Result is not null && !patients.Any(p => p.PatientId == mrnNumberTask.Result.PatientId))
                {
                    patients.Add(mrnNumberTask.Result);
                }

                return patients;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred processing unit number aggregator workflow for identifier {UnitNumber}", unitNumber);
                throw;
            }
        }

       

       public async Task<PatientsInHouseIndex?> GetPatientsAdvancedSearch(
       string providerCodes,
       bool currentInpatientOnly,
       PatientSearchValues searchValue,
       bool soundEx,
       bool bypassCache = false)
        {
            if (searchValue is null)
            {
                return null;
            }

            var cacheKey = $"{Entity.Patient}_LastSearch";
            List<string> providerCodeList = StringHelper.DeserializeList(providerCodes) ?? [];

            try
            {
                using (logger.BeginScope("GetPatientsAdvancedSearch"))
                {
                    string patientJson;

                    if (string.IsNullOrWhiteSpace(searchValue.NextBatchUrl))
                    {
                        var searchParams = new List<string>();

                        if (!string.IsNullOrWhiteSpace(searchValue.Surname))
                        {
                            searchParams.Add($"family={searchValue.Surname}");
                        }

                        if (!string.IsNullOrWhiteSpace(searchValue.PatientId))
                        {
                            searchParams.Add($"_id={searchValue.PatientId}");
                        }

                        if (!string.IsNullOrWhiteSpace(searchValue.Forename))
                        {
                            searchParams.Add($"given={searchValue.Forename}");
                        }

                        if (!string.IsNullOrWhiteSpace(searchValue.Forename2))
                        {
                            searchParams.Add($"given={searchValue.Forename2}");
                        }

                        if (!string.IsNullOrWhiteSpace(searchValue.Gender))
                        {
                            var gender = searchValue.Gender.Equals("m", StringComparison.OrdinalIgnoreCase) ? "male" : "female";
                            searchParams.Add($"gender={gender}");
                        }

                        var recordCount = searchValue.Count > 0 ? searchValue.Count : 10;
                        searchParams.Add($"_count={recordCount}");

                        if (searchValue.YearPartDOB > 0 && searchValue.MonthPartDOB > 0 && searchValue.DayPartDOB > 0)
                        {
                            searchParams.Add($"birthdate={searchValue.YearPartDOB:D4}-{searchValue.MonthPartDOB:D2}-{searchValue.DayPartDOB:D2}");
                        }
                        else if (searchValue.YearPartDOB > 0)
                        {
                            searchParams.Add($"birthdate=ge{searchValue.YearPartDOB:D4}-01-01");
                            searchParams.Add($"birthdate=le{searchValue.YearPartDOB:D4}-12-31");
                        }

                        if (searchParams.Count == 1)
                        {
                            return null;
                        }

                        patientJson = await client.SearchAsync(Entity.Patient, string.Join("&", searchParams), logger).ConfigureAwait(false);
                    }
                    else
                    {
                        patientJson = await client.SearchAsync(searchValue.NextBatchUrl, logger).ConfigureAwait(false);
                    }

                    if (string.IsNullOrWhiteSpace(patientJson))
                    {
                        return null;
                    }

                    var parser = new FhirJsonParser();
                    var bundle = parser.Parse<Bundle>(patientJson);

                    var fhirPatients = bundle?.Entry?
                        .Select(e => e.Resource)
                        .OfType<FhirPatient>() ?? [];

                    try
                    {
                        var populationTasks = fhirPatients.Select(p => PopulatePatientGateway(p, bypassCache));
                        var mappedResults = await Task.WhenAll(populationTasks).ConfigureAwait(false);

                        var patientGatewayList = mappedResults.Where(p => p is not null).ToList();

                        if (patientGatewayList.Count == 0)
                        {
                            return null;
                        }

                       

                        return new PatientsInHouseIndex
                        {
                            PrevPageUrl = bundle?.PreviousLink?.ToString(),
                            NextPageUrl = bundle?.NextLink?.ToString(),
                            Patients = patientGatewayList
                        };
                    }
                    catch (AutoMapperMappingException ex)
                    {
                        logger.LogError(ex, "An AutoMapper runtime mapping error occurred within Advanced Search mapping evaluation");
                        throw new ApplicationException("Data processing failed due to mapping error.", ex);
                    }
                }
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                logger.LogError(ex, "Fatal system trace performing advanced parameters query matching array filtering profiles");
                throw;
            }
        }

       


        public async Task<PatientInHouse?> PopulatePatientGateway(FhirPatient? fhirPatient, bool bypassCache = false)
        {
            var serializer = new FhirJsonSerializer(new SerializerSettings());
            if (fhirPatient is null)
            {
                return null;
            }

            PatientInHouse? patientGateway = mapper.Map<PatientInHouse>(fhirPatient);

            string orgId = patientGateway.PractitionersCards?
                .Where(p => p.Type == PractitionareCardType.organisation).FirstOrDefault()?.Id ?? "0";

            var organization = await organizationService.GetOrganizationByIdAsync(Int32.Parse( orgId));
            if (organization is not null)
            {
                patientGateway.GPAddress = organization.Address;
            }

            var allergies = await allergyIntoleranceService.GetAllergiesByPatient(patientGateway.PatientId, AllergyCategories.all, bypassCache);

            patientGateway.Allergies = allergies;
            return patientGateway;
        }

       

       
    }
}