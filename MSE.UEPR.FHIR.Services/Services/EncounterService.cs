using System.Globalization;

using AutoMapper;

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

using Microsoft.Extensions.Logging;

using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.Services.Interfaces;

namespace MSE.UEPR.FHIR.Services.Services;

public class EncounterService(
    ILogger<EncounterService> logger,
    IHttpClientToCerner client,
    IMapper mapper) : IEncounterService
{
    public async Task<List<EncounterGateway>?> SearchInpatientEncountersAsync(
        string patientId,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        string? locationId = null)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return [];
        }

        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            throw new ArgumentException("startDate must be earlier than or equal to endDate.");
        }

        try
        {
            var searchTerms = new List<string>
            {
                $"patient={Uri.EscapeDataString(patientId)}",
                $"class={Uri.EscapeDataString("IMP")}" // FHIR ActCode for inpatient encounter.
            };

            if (startDate.HasValue)
            {
                searchTerms.Add($"date={Uri.EscapeDataString($"ge{FormatFhirDate(startDate.Value)}")}");
            }

            if (endDate.HasValue)
            {
                searchTerms.Add($"date={Uri.EscapeDataString($"le{FormatFhirDate(endDate.Value)}")}");
            }

            if (!string.IsNullOrWhiteSpace(locationId))
            {
                searchTerms.Add($"location={Uri.EscapeDataString(locationId)}");
            }

            var response = await client.SearchAsync(Entity.Encounter, string.Join("&", searchTerms), logger)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
            {
                return [];
            }

            var bundle = new FhirJsonParser().Parse<Bundle>(response);
            var encounters = bundle.Entry?
                .Select(entry => entry.Resource)
                .OfType<Encounter>()
                .ToList();

            return encounters is { Count: > 0 }
                ? mapper.Map<List<EncounterGateway>>(encounters)
                : [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search inpatient encounters.");
            throw;
        }
    }

    private static string FormatFhirDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
