using MSE.UEPR.FHIR.CORE.Models;
using MSE.UEPR.FHIR.CORE.Models.Indexes;

namespace MSE.UEPR.FHIR.Services.Interfaces
{
    public interface IOrganizationService
    {
        Task<OrganisationGateway?> GetOrganizationByCodeAsync(string codes, bool bypassCache = false);

        Task<OrganisationGateway?> GetOrganizationByIdAsync(int id, bool bypassCache = false);

        Task<OrganisationIndex?> GetOrganizationsAsync(string nextBatchUrl, bool bypassCache = false);

        Task<List<OrganisationGateway>?> GetOrganizationsByCodesAsync(string codesList, bool bypassCache = false);

        Task<List<OrganisationGateway>?> GetOrganizationsByNameAsync(string name, bool bypassCache = false);
    }
}