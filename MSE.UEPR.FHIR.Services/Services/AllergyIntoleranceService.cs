using AutoMapper;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

using Microsoft.Extensions.Logging;

using MSE.UEPR.FHIR.CORE.Config;
using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Helpers;
using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.CORE.Models.InHouse;
using MSE.UEPR.FHIR.CORE.Redis;
using MSE.UEPR.FHIR.CORE.Security;
using MSE.UEPR.FHIR.Services.Interfaces;

namespace MSE.UEPR.FHIR.Services.Services
{
    public class AllergyIntoleranceService(ILogger<AllergyIntoleranceService> logger,
            IMapper mapper,
            AppSettings appSettings,
            Authentication authentication,
            IRedisCache redisCache) : IAllergyIntoleranceService
    {
        /// <summary>
        /// Retrieves all recorded allergy information for a specified patient from the FHIR server.
        /// It is a record of a clinical assessment of a propensity, or a potential risk, to an individual to have an adverse reaction on future exposure to a specified substance or class of substance.
        /// </summary>
        /// <remarks>This method performs an asynchronous search for <c>AllergyIntolerance</c> resources
        /// associated with the specified patient. If no allergy records are found, a null value is returned. Errors
        /// encountered during the FHIR operation or general execution are logged using the provided logger. </remarks>
        /// <param name="resourceType">The FHIR resource type to use for the search. Typically, this should be set to the patient entity type.</param>
        /// <param name="id">The unique identifier of the patient whose allergy records are to be retrieved. Cannot be null or empty.</param>
        /// <returns>A  List<AllergyIntolerance> representation of the FHIR <c>Bundle</c> containing the patient's allergy records if any are found;
        /// otherwise, Null.</returns>
        public async Task<List<AllergyInhouse>?> GetAllergiesByPatient(string patientId, AllergyCategories category, bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientId))
            {
                return null;
            }

            var cacheKey = $"{Entity.AllergyIntolerance}_PatientID:{patientId}_{Entity.AllergyIntolerance}_{category}";

            try
            {
                using (logger.BeginScope("GetAllergiesByPatient"))
                {

                    if (!bypassCache)
                    {
                        var cachedData = await redisCache.GetCacheDataAsync<List<AllergyInhouse>>(cacheKey);
                        if (cachedData != null)
                        {
                            logger.LogInformation("Returning cached allergy data for ID: {patientId}", patientId);
                            return cachedData;
                        }
                    }

                    var fhirClient = await authentication.GetFhirClientAsync().ConfigureAwait(false);
                    var searchParams = new SearchParams().Add("patient", patientId);

                    var bundle = await fhirClient.SearchAsync<AllergyIntolerance>(searchParams).ConfigureAwait(false);

                    if (bundle?.Entry is null || bundle.Entry.Count == 0)
                    {
                        logger.LogInformation("No allergy records found for patient {PatientId}", patientId);
                        return null;
                    }

                    // Optimization: Filter out matching entries without duplicate casting or missing reference evaluation exceptions
                    if (category != AllergyCategories.all)
                    {
                        var targetCategoryStr = category.ToString();

                        bundle.Entry = bundle.Entry
                            .Where(e => e.Resource is AllergyIntolerance allergy
                                        && allergy.Category is not null
                                        && allergy.Category.Any(c => c is not null && StringHelper.Matches(c.ToString()!, targetCategoryStr)))
                            .ToList();

                        bundle.Total = bundle.Entry.Count;
                    }

                    if (bundle?.Entry?.Count is null or 0)
                    {
                        logger.LogInformation("No observation records found for patient {PatientId} with category {Category}", patientId, category);
                        return [];
                    }

                    var allergies = bundle.Entry
                        .Select(e => e.Resource)
                        .OfType<AllergyIntolerance>()
                        .ToList();

                    var allergiesInhouse = mapper.Map<List<AllergyInhouse>>(allergies);

                    if (allergiesInhouse != null)
                    {
                        await redisCache.SetCacheDataAsync(cacheKey, allergiesInhouse, DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.AllergyCacheDurationInMinutes));
                    }

                    return allergiesInhouse;

                  
                }
            }
            catch (FhirOperationException foe)
            {
                // Fixed Structured Logging: Passing exception natively as the contextual argument
                logger.LogError(foe, "FHIR operation failed while fetching allergies for patient {PatientId}", patientId);
                return null;
            }
            catch (Exception ex)
            {
                // Fixed Structured Logging: Fixed exception swallowing anti-pattern to prevent untraceable bugs
                logger.LogError(ex, "Fatal failure retrieving patient allergy dataset layout for reference {PatientId}", patientId);
                throw;
            }
        }
    }
}