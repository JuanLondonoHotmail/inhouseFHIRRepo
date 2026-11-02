using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MSE.UEPR.FHIR.CORE.Enums;

namespace MSE.UEPR.FHIR.Services.Interfaces
{
    public interface IHttpClientToCerner
    {
        Task<string> GetResourceByIdAsync(Entity resourceType, string id, ILogger logger);

        Task<string> SearchAsync(Entity resourceType, string query, ILogger logger);

        Task<string> SearchAsync(string query, ILogger logger);
    }
}