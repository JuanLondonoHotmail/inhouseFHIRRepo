using AutoMapper;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using MSE.UEPR.FHIR.CORE.Config;
using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.CORE.Models.Indexes;
using MSE.UEPR.FHIR.CORE.Redis;
using MSE.UEPR.FHIR.CORE.Security;
using MSE.UEPR.FHIR.Services.Interfaces;

namespace MSE.UEPR.FHIR.Services.Services
{
    public class OrganizationService(
        ILogger<OrganizationService> logger,
            AppSettings appSettings,
            IMapper mapper,
            IRedisCache redisCache,
            IHttpClientToCerner client,
            Authentication authentication) : IOrganizationService
    {
        public async Task<OrganisationGateway?> GetOrganizationByCodeAsync(string code, bool bypassCache = false)
        {
            var cacheKey = $"{Entity.Organization}_OrganizationCode:{code}";

            using (logger.BeginScope(new Dictionary<string, object> { ["Method"] = nameof(GetOrganizationByCodeAsync), ["Code"] = code }))
            {
                if (!bypassCache)
                {
                    var cachedData = await redisCache.GetCacheDataAsync<OrganisationGateway>(cacheKey);
                    if (cachedData != null)
                    {
                        logger.LogInformation("Returning cached organization data for code: {Code}", code);
                        return cachedData;
                    }
                }

                try
                {
                    var fhirClient = await authentication.GetFhirClientAsync();

                    // Search parameters using specific identifier format "system|code"
                    var searchParams = new SearchParams().Where($"identifier=9063349|{code}");

                    var bundle = await fhirClient.SearchAsync<Organization>(searchParams);

                    if (bundle?.Entry.FirstOrDefault()?.Resource is not Organization fhirOrganization)
                    {
                        logger.LogWarning("No organization found in FHIR server for code: {Code}", code);
                        return null;
                    }

                    // Map FHIR model to Gateway Domain model
                    var organizationGateway = mapper.Map<OrganisationGateway>(fhirOrganization);

                    // Cache the result if found
                    if (organizationGateway != null)
                    {
                        await redisCache.SetCacheDataAsync(cacheKey,
                            organizationGateway,
                            DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.OrganizationCacheDurationInMinutes));
                    }

                    return organizationGateway;
                }
                catch (FhirOperationException fex)
                {
                    logger.LogError(fex, "FHIR operation failed while fetching organization for code: {Code}. Status: {Status}", code, fex.Status);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An unexpected error occurred while fetching organization for code: {Code}", code);
                    throw;
                }
            }
        }

        public async Task<OrganisationGateway?> GetOrganizationByIdAsync(int id, bool bypassCache = false)
        {
            var cacheKey = $"{Entity.Organization}_OrganizationId:{id}";

            using (logger.BeginScope(new Dictionary<string, object> { ["Method"] = nameof(GetOrganizationByIdAsync), ["Id"] = id }))
            {
                try
                {
                    if (!bypassCache)
                    {
                        var cachedData = await redisCache.GetCacheDataAsync<OrganisationGateway>(cacheKey);
                        if (cachedData != null)
                        {
                            logger.LogInformation("Returning cached organization data for ID: {Id}", id);
                            return cachedData;
                        }
                    }

                    var organizationJson = await client.GetResourceByIdAsync(Entity.Organization, id.ToString(), logger);

                    if (string.IsNullOrEmpty(organizationJson))
                    {
                        logger.LogWarning("No organization JSON payload returned from Cerner client for ID: {Id}", id);
                        return null;
                    }

                    var parser = new FhirJsonParser();
                    var parsedModel = parser.Parse<Organization>(organizationJson);

                    if (parsedModel == null)
                    {
                        logger.LogWarning("Failed to parse JSON into FHIR Organization model for ID: {Id}", id);
                        return null;
                    }

                    var organizationGateway = mapper.Map<OrganisationGateway>(parsedModel);

                    if (organizationGateway != null)
                    {
                        await redisCache.SetCacheDataAsync(cacheKey, organizationGateway, DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.OrganizationCacheDurationInMinutes));
                    }

                    return organizationGateway;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while fetching organization by ID: {Id}", id);
                    throw; // Preserves the original stack trace
                }
            }
        }

        public async Task<OrganisationIndex?> GetOrganizationsAsync(string nextBatchUrl, bool bypassCache = false)
        {
            const string DefaultSearchQuery = "_count=100";

            using (logger.BeginScope(new Dictionary<string, object>
            {
                ["Method"] = nameof(GetOrganizationsAsync),
                ["IsPaging"] = !string.IsNullOrEmpty(nextBatchUrl)
            }))
            {
                try
                {
                    string organisationListJson;

                    if (string.IsNullOrEmpty(nextBatchUrl))
                    {
                        organisationListJson = await client.SearchAsync(Entity.Organization, DefaultSearchQuery, logger);
                    }
                    else
                    {
                        organisationListJson = await client.SearchAsync(nextBatchUrl, logger);
                    }

                    if (string.IsNullOrWhiteSpace(organisationListJson))
                    {
                        logger.LogWarning("Empty response returned from FHIR search payload.");
                        return null;
                    }

                    var parser = new FhirJsonParser();
                    var bundle = parser.Parse<Bundle>(organisationListJson);

                    if (bundle == null)
                    {
                        logger.LogWarning("Failed to parse the payload into a FHIR Bundle resource.");
                        return null;
                    }

                    var bundleOrganisations = bundle.Entry
                        .Select(e => e.Resource)
                        .OfType<Organization>()
                        .ToList();

                    var organizationsGateway = mapper.Map<List<OrganisationGateway>>(bundleOrganisations);

                    return new OrganisationIndex
                    {
                        PrevPageUrl = bundle.PreviousLink?.ToString(),
                        NextPageUrl = bundle.NextLink?.ToString(),
                        Organisations = organizationsGateway
                    };
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while paginating or searching FHIR organizations.");
                    throw; // Retain full contextual stack traces
                }
            }
        }

        public async Task<List<OrganisationGateway>?> GetOrganizationsByCodesAsync(string codesList, bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(codesList))
            {
                return [];
            }

            using (logger.BeginScope(new Dictionary<string, object> { ["Method"] = nameof(GetOrganizationsByCodesAsync) }))
            {
                try
                {
                    // Parse into arrays instantly to avoid multiple iterations or overhead over string arrays
                    var codes = codesList.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                         .Distinct(StringComparer.OrdinalIgnoreCase)
                                         .ToArray();

                    if (codes.Length == 0)
                    {
                        return [];
                    }

                    // Optional: Limit maximum degree of parallelism if the input list can be huge (e.g., max 10 concurrent requests)
                    // Using Parallel.ForEachAsync optimizes ThreadPool utilization compared to Task.WhenAll on large lists
                    var options = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 10
                    };

                    var organizations = new System.Collections.Concurrent.ConcurrentBag<OrganisationGateway>();

                    await Parallel.ForEachAsync(codes, options, async (code, cancellationToken) =>
                    {
                        var org = await GetOrganizationByCodeAsync(code, bypassCache).ConfigureAwait(false);
                        if (org != null)
                        {
                            organizations.Add(org);
                        }
                    });

                    return organizations.ToList();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while fetching organizations for batch list: {CodesList}", codesList);
                    throw;
                }
            }
        }

        public async Task<List<OrganisationGateway>> GetOrganizationsByNameAsync(string name, bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return [];
            }

            var cacheKey = $"{Entity.Organization}_Name:{name.Trim()}";

            using (logger.BeginScope(new Dictionary<string, object> { ["Method"] = nameof(GetOrganizationsByNameAsync), ["SearchName"] = name }))
            {
                try
                {
                    if (!bypassCache)
                    {
                        var cachedData = await redisCache.GetCacheDataAsync<List<OrganisationGateway>>(cacheKey);
                        if (cachedData != null)
                        {
                            logger.LogInformation("Returning cached organization list matching name: {SearchName}", name);
                            return cachedData;
                        }
                    }

                    var fhirClient = await authentication.GetFhirClientAsync();
                    var query = new SearchParams().Where($"name={name}");

                    var allOrgs = new List<Organization>();
                    Bundle? bundle = await fhirClient.SearchAsync<Organization>(query);

                    while (bundle != null)
                    {
                        // Extract resources safely (ignoring potential null entries or non-Organization resources)
                        var pageOrgs = bundle.Entry.Select(e => e.Resource).OfType<Organization>();
                        allOrgs.AddRange(pageOrgs);

                        // Move to the next page automatically using Hl7.Fhir client mechanics
                        bundle = await fhirClient.ContinueAsync(bundle);
                    }

                    if (allOrgs.Count == 0)
                    {
                        logger.LogWarning("No organization records found matching name: {SearchName}", name);
                        return [];
                    }

                    var organizationsGateway = mapper.Map<List<OrganisationGateway>>(allOrgs);

                    if (organizationsGateway != null && organizationsGateway.Count > 0)
                    {
                        await redisCache.SetCacheDataAsync(cacheKey, organizationsGateway, DateTimeOffset.Now.AddMinutes(appSettings.Redis_Settings.OrganizationCacheDurationInMinutes));
                    }

                    return organizationsGateway ?? [];
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while trying to query organizations by name: {SearchName}", name);
                    throw;
                }
            }
        }
    }
}