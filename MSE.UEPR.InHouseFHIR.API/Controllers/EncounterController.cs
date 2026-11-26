using Asp.Versioning;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.Services.Interfaces;

using Swashbuckle.AspNetCore.Annotations;

namespace MSE.UEPR.FHIR.API.Controllers;

[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
[ApiVersion("1")]
[Route("api/v1/[controller]")]
[ApiController]
public class EncounterController(
    ILogger<EncounterController> logger,
    IPatientService patientService,
    IEncounterService encounterService) : ControllerBase
{
    [HttpGet("inpatient")]
    [SwaggerOperation(
        Summary = "Search inpatient encounters by NHS or MRN number",
        Description = "Returns inpatient EncounterGateway records for a patient, optionally filtered by encounter date range and location.")]
    [ProducesResponseType(typeof(List<EncounterGateway>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetInpatientEncounters(
        [FromQuery(Name = "NHS_number"), SwaggerParameter("Patient NHS number; provide this or MRN_number")]
        string? nhsNumber,
        [FromQuery(Name = "MRN_number"), SwaggerParameter("Patient MRN number; provide this or NHS_number")]
        string? mrnNumber,
        [FromQuery(Name = "start_date"), SwaggerParameter("Optional inclusive encounter start date")]
        DateTimeOffset? startDate,
        [FromQuery(Name = "end_date"), SwaggerParameter("Optional inclusive encounter end date")]
        DateTimeOffset? endDate,
        [FromQuery(Name = "LocationId"), SwaggerParameter("Optional FHIR Location resource ID")]
        string? locationId)
    {
        if (string.IsNullOrWhiteSpace(nhsNumber) && string.IsNullOrWhiteSpace(mrnNumber))
        {
            return BadRequest("Provide either NHS_number or MRN_number.");
        }

        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            return BadRequest("start_date must be earlier than or equal to end_date.");
        }

        try
        {
            var patient = !string.IsNullOrWhiteSpace(nhsNumber)
                ? await patientService.GetPatientByNhsNumberAsync(nhsNumber).ConfigureAwait(false)
                : null;

            if (patient is null && !string.IsNullOrWhiteSpace(mrnNumber))
            {
                patient = await patientService.GetPatientByMrnNumberAsync(mrnNumber).ConfigureAwait(false);
            }

            if (patient is null || string.IsNullOrWhiteSpace(patient.PatientId))
            {
                return NoContent();
            }

            var encounterGateways = await encounterService.SearchInpatientEncountersAsync(
                patient.PatientId,
                startDate,
                endDate,
                locationId).ConfigureAwait(false);

            return encounterGateways is { Count: > 0 } ? Ok(encounterGateways) : NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve inpatient encounters for the supplied patient identifier.");
            return Problem(
                detail: "An unexpected error occurred while retrieving inpatient encounters.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

}
