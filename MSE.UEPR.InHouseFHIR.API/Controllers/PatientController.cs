using Asp.Versioning;

using AutoMapper;

using Hl7.Fhir.Model;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

using MSE.UEPR.FHIR.CORE.Config;
using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Helpers;
using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.CORE.Models.Indexes;
using MSE.UEPR.FHIR.Services;
using MSE.UEPR.FHIR.Services.Interfaces;

using Swashbuckle.AspNetCore.Annotations;

namespace MSE.UEPR.FHIR.API.Controllers
{
    /// <summary>
    /// Constructor:
    /// For testing purposes on DEV you can use id="12742400", "12724067 (has address
    /// Or on Prod_test: MRN 80000267, (PatientId 2901198), 80000152 (2897531), 80000210 (2900856)
    /// 2897535 has Allergies
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="patientService"></param>
    /// <param name="conditionService"></param>
    /// <param name="patientFavouritesService"></param>
    /// <param name="mapper"></param>
    /// <param name="appSettings"></param>
    [RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
    [ApiVersion("1")]
    [Route("api/v1/[controller]")]
    [ApiController]
    public class PatientController(
     ILogger<PatientController> logger,
     IPatientService patientService,
     IMapper mapper,
     AppSettings appSettings)
     : ControllerBase
    {
        [SwaggerOperation(
            Summary = "ACP Advanced Search",
            Description = "Get a list of PatientGateway objects based on forename, middlename, surname, DoB, and inpatient status.\n\nWIP: SoundEx and ProviderCodes features."
        )]
        [HttpGet("details")]
        [ProducesResponseType(typeof(List<PatientGateway>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Details(
            [FromQuery] PatientSearchValues searchValue,
            [SwaggerParameter("Search for InPatients only")][FromQuery] bool currentInpatientOnly = false,
            [SwaggerParameter("ProviderCodes separated by character '~'")][FromQuery] string providerCodes = "",
            [FromQuery] bool soundEx = false,
            [FromQuery] bool bypassCache = false)
        {
            if (searchValue == null)
            {
                logger.LogWarning("Details search initiated with null search values.");
                return BadRequest("Search parameters cannot be null.");
            }

            var patientsIndex = await patientService.GetPatientsAdvancedSearch(
                providerCodes,
                currentInpatientOnly,
                searchValue,
                soundEx,
                bypassCache
            ).ConfigureAwait(false);

            if (patientsIndex?.Patients is not { Count: > 0 })
            {
                return NoContent();
            }

            return Ok(patientsIndex.Patients);
        }

             

        [SwaggerOperation(
            Summary = "Retrieve patient details by Cerner number",
            Description = "Fetches patient demographic information using their Cerner (5000xxxx) reference number and links their General Practitioner (GP) details if available."
        )]
        [HttpGet("CernerNumber")]
        [ProducesResponseType(typeof(PatientGateway), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientByCernerNumberAsync(
        [FromQuery, SwaggerParameter("Patient's Cerner Number", Required = true)] string patientCernerNumber,
        [FromQuery, SwaggerParameter("Bypass cache if data is likely to be outdated", Required = false)] bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientCernerNumber))
            {
                return BadRequest("Patient Cerner number cannot be null or empty.");
            }

            try
            {
                var patientGateway = await patientService.GetPatientByCernerNumberAsync(patientCernerNumber, bypassCache)
                    .ConfigureAwait(false);

                if (patientGateway is null)
                {
                    return NoContent();
                }

                return Ok(patientGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching patient records by Cerner number {CernerNumber} in PatientController", patientCernerNumber);

                return Problem(
                    detail: "An unexpected error occurred while retrieving the patient record.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        /// <summary>
        /// Retrieves patient details by their unique identifier, optionally bypassing cached data.
        /// </summary>
        /// <remarks>This method fetches the patient's information and, if available, links their General
        /// Practitioner (GP) details to the result. Returns a 404 response if no patient is found for the specified
        /// identifier.</remarks>
        /// <param name="patientId"></param>
        /// <param name="bypassCache"><see langword="true"/> to bypass cached data and fetch the latest patient information; otherwise, <see
        /// langword="false"/> to use cached data if available. Optional.</param>
        /// <returns>An <see cref="IActionResult"/> containing a <see cref="PatientGateway"/> object with patient and GP details
        /// if found; otherwise, a 404 Not Found response.</returns>
        [SwaggerOperation(
             Summary = "Retrieve patient details by ID",
             Description = "Fetches patient demographic information using their unique patient ID and links their General Practitioner (GP) details if available. Automatically attempts a live bypass fetch if the initial cached fetch returns empty."
         )]
        [HttpGet]
        [ProducesResponseType(typeof(PatientGateway), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientById(
            [FromQuery(Name = "patient"), SwaggerParameter("Unique Patient ID", Required = true)] string patientId,
            [FromQuery, SwaggerParameter("Bypass cache if data is likely to be outdated", Required = false)] bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientId))
            {
                return BadRequest("Patient ID cannot be null or empty.");
            }

            try
            {
                var patientGateway = await patientService.GetPatientByIdAsync(patientId, bypassCache)
                    .ConfigureAwait(false);

                if (patientGateway is null && !bypassCache)
                {
                    patientGateway = await patientService.GetPatientByIdAsync(patientId, bypassCache: true)
                        .ConfigureAwait(false);
                }

                if (patientGateway is null)
                {
                    return NoContent();
                }

                return Ok(patientGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching patient records by ID {PatientId} in PatientController", patientId);

                return Problem(
                    detail: "An unexpected error occurred while retrieving the patient record.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        [SwaggerOperation(
            Summary = "Retrieve patient details by MRN number",
            Description = "Fetches patient demographic information using their Medical Record Number (MRN) and links their General Practitioner (GP) details if available."
        )]
        [HttpGet("MrnNumber")]
        [ProducesResponseType(typeof(PatientGateway), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientByMrnNumberAsync(
            [FromQuery, SwaggerParameter("Patient's MRN Number", Required = true)] string patientMrnNumber,
            [FromQuery, SwaggerParameter("Bypass cache if data is likely to be outdated", Required = false)] bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientMrnNumber))
            {
                return BadRequest("Patient MRN number cannot be null or empty.");
            }

            try
            {
                var patientGateway = await patientService.GetPatientByMrnNumberAsync(patientMrnNumber, bypassCache)
                    .ConfigureAwait(false);

                if (patientGateway is null)
                {
                    return NoContent();
                }

                return Ok(patientGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching patient records by MRN {MrnNumber} in PatientController", patientMrnNumber);

                return Problem(
                    detail: "An unexpected error occurred while retrieving the patient record.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        [SwaggerOperation(
            Summary = "Retrieve patient details by NHS number",
            Description = "Fetches patient demographic information using their 10-digit National Health Service (NHS) number and links their General Practitioner (GP) details if available."
        )]
        [HttpGet("NhsNumber")]
        [ProducesResponseType(typeof(PatientGateway), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientByNhsNumberAsync(
            [FromQuery, SwaggerParameter("Patient's NHS Number", Required = true)] string patientNhsNumber,
            [FromQuery, SwaggerParameter("Bypass cache if data is likely to be outdated", Required = false)] bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientNhsNumber))
            {
                return BadRequest("Patient NHS number cannot be null or empty.");
            }

            try
            {
                var patientGateway = await patientService.GetPatientByNhsNumberAsync(patientNhsNumber, bypassCache)
                    .ConfigureAwait(false);

                if (patientGateway is null)
                {
                    return NoContent();
                }

                return Ok(patientGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching patient records by NHS number {NhsNumber} in PatientController", patientNhsNumber);

                return Problem(
                    detail: "An unexpected error occurred while retrieving the patient record.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        [SwaggerOperation(
             Summary = "Retrieve patient details by NHS or MRN number",
             Description = "Attempts to fetch patient details using the provided identifier as an NHS number. If no matching patient is found, it automatically falls back to searching by Medical Record Number (MRN)."
         )]
        [HttpGet("NhsOrMrnNumber")]
        [ProducesResponseType(typeof(PatientGateway), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientByNhsOrMrnNumberAsync(
            [FromQuery, SwaggerParameter("Patient's NHS or MRN Number", Required = true)] string patientNumber,
            [FromQuery, SwaggerParameter("Bypass cache if data is likely to be outdated", Required = false)] bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(patientNumber))
            {
                return BadRequest("The patient identifier number cannot be null or empty.");
            }

            try
            {
                var patientGateway = await patientService.GetPatientByNhsNumberAsync(patientNumber, bypassCache)
                    .ConfigureAwait(false);

                if (patientGateway is null)
                {
                    patientGateway = await patientService.GetPatientByMrnNumberAsync(patientNumber, bypassCache)
                        .ConfigureAwait(false);
                }

                if (patientGateway is null)
                {
                    return NoContent();
                }

                return Ok(patientGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching patient records by NHS/MRN identifier {PatientNumber} in PatientController", patientNumber);

                return Problem(
                    detail: "An unexpected error occurred while retrieving the patient record.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        [SwaggerOperation(
            Summary = "Retrieve patient details by composite resource ID",
            Description = "Fetches a patient profile using a composite GUID resource value. The unique GUID maps directly to a decrypted database identifier structure."
         )]
        [HttpGet("resource/{resourceId}")]
        [ProducesResponseType(typeof(PatientGateway), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientByResourceIdAsync(
            [FromRoute, SwaggerParameter("Unique Patient's Resource Id (GUID format)", Required = true)] string resourceId,
            [FromQuery, SwaggerParameter("Bypass cache if data is likely to be outdated", Required = false)] bool bypassCache = false)
        {
            if (!Guid.TryParse(resourceId, out var parsedGuid))
            {
                return BadRequest("The provided resource ID must be a valid GUID format.");
            }

            try
            {
                (string _, string id) = MappingHelpers.DecryptFromGuid(parsedGuid, Entity.Patient);

                var patientGateway = await patientService.GetPatientByIdAsync(id, bypassCache)
                    .ConfigureAwait(false);

                if (patientGateway is null)
                {
                    return NoContent();
                }

                return Ok(patientGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing resource search execution for identity hash {ResourceId}", resourceId);

                return Problem(
                    detail: "An error occurred while decrypting or fetching the composite resource.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        [SwaggerOperation(
            Summary = "Retrieve patient details by unit number",
            Description = "Fetches a specific patient collection from the underlying provider records using their unique system Unit Number."
        )]
        [HttpGet("unitnumber/{UnitNumber}")]
        [ProducesResponseType(typeof(List<MSB.Common.Model.Patient>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientByUnitNumberAsync(
            [FromRoute, SwaggerParameter("Unique Patient's Unit Number", Required = true)] string unitNumber,
            [FromQuery, SwaggerParameter("List of Provider Codes ampersand-concatenated", Required = false)] string providerCodes = "",
            [FromQuery, SwaggerParameter("Bypass cache if data is likely to be outdated", Required = false)] bool bypassCache = false)
        {
            if (string.IsNullOrWhiteSpace(unitNumber))
            {
                return BadRequest("Unit number parameter cannot be null or empty.");
            }

            try
            {
                var patientGateway = await patientService.GetPatientByUnitNumber(unitNumber, providerCodes, bypassCache)
                    .ConfigureAwait(false);

                if (patientGateway is not { Count: > 0 })
                {
                    return NoContent();
                }

                return Ok(patientGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed lookup profile matching organizational Unit ID record context {UnitNumber}", unitNumber);

                return Problem(
                    detail: "An internal fault occurred navigating system organization indices.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }


        [SwaggerOperation(
            Summary = "Advanced multi-attribute patient profile registry query lookup",
            Description = "Performs an algorithmic search layout filtering match parameters across demographics such as names, birth records, Soundex variations, and live inpatient statuses."
        )]
        [HttpGet("AdvancedSearch")]
        [ProducesResponseType(typeof(PatientsIndex), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetPatientsAdvancedSearch(
            [FromQuery] PatientSearchValues searchValue,
            [FromQuery, SwaggerParameter("Restrict search results visibility exclusively to currently admitted Inpatients only")] bool currentInpatientOnly = false,
            [FromQuery, SwaggerParameter("Provider code filters separated internally using the tilde character context")] string providerCodes = "",
            [FromQuery, SwaggerParameter("Enable phonetic matching approximations")] bool soundEx = false,
            [FromQuery, SwaggerParameter("Enforce live database lookup bypassing cache arrays")] bool bypassCache = false)
        {
            if (searchValue is null)
            {
                return BadRequest("Advanced searching metrics properties parameters model must be instantiation bounded.");
            }

            try
            {
                var patientsIndex = await patientService.GetPatientsAdvancedSearch(providerCodes, currentInpatientOnly, searchValue, soundEx, bypassCache)
                    .ConfigureAwait(false);

                if (patientsIndex is null)
                {
                    return NoContent();
                }

                return Ok(patientsIndex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fatal system trace performing comprehensive parameters search configuration engine matches evaluation mapping");

                return Problem(
                    detail: "Processing engine failure completing complex query array filtering profiles.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

    
    }
}