using Microsoft.Extensions.Logging;

using MSE.UEPR.FHIR.CORE.Config;
using MSE.UEPR.FHIR.CORE.Enums;
using MSE.UEPR.FHIR.CORE.Security;
using MSE.UEPR.FHIR.Services.Interfaces;

using static System.Net.WebRequestMethods;

namespace MSE.UEPR.FHIR.Services.Services
{
    public class HttpClientToCerner(
        HttpClient httpClient,
        Authentication authentication,
        AppSettings appSettings) : IHttpClientToCerner

    {
        // MAIN method to GET a FHIR resource by ID
        public async Task<string> GetResourceByIdAsync(Entity resourceType, string id, ILogger _logger)
        {
            // Thread-safe token acquisition step if needed before execution
            await authentication.EnsureHttpClientAuthenticatedAsync(httpClient);

            return await ExecuteWithRetry(async () =>
            {
                var response = await httpClient.GetAsync(appSettings.Cerner_Settings.FhirServerRootUrl + string.Format("{0}/{1}", resourceType.ToString(), id));

                // Throws exception if status code != 2xx
                //response.EnsureSuccessStatusCode();
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return json;
                }
                else
                {
                    _logger.LogError($"Failed to retrieve {resourceType} demographics. Status code: " + response.StatusCode.ToString());
                    return "";
                }
            }, _logger);
        }

        /// <summary>
        /// Used for searching FHIR Cerner DB for the for any Entity and a query. It returns the json string of the bundle.
        /// </summary>
        /// <param name="resourceType"></param>
        /// <param name="query"></param>
        /// <param name="_logger"></param>
        /// <returns></returns>
        public async Task<string> SearchAsync(Entity resourceType, string query, ILogger logger)
        {
            //httpClient = await authentication.GetHttpClient();
            await authentication.EnsureHttpClientAuthenticatedAsync(httpClient);

            return await ExecuteWithRetry(async () =>
            {
                var url = $"{appSettings.Cerner_Settings.FhirServerRootUrl}{resourceType}?{query}";
                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                logger.LogError("Failed to retrieve {ResourceType} demographics. Status code: {StatusCode}", resourceType, response.StatusCode);
                return string.Empty;
            }, logger);
        }

        /// <summary>
        /// Used for searching FHIR Cerner DB for the Previous or Next pagination
        /// </summary>
        /// <param name="query">full URL query</param>
        /// <param name="_logger"></param>
        /// <returns></returns>
        public async Task<string> SearchAsync(string query, ILogger logger)
        {
            await authentication.EnsureHttpClientAuthenticatedAsync(httpClient);

            return await ExecuteWithRetry(async () =>
            {
                var response = await httpClient.GetAsync(query);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                logger.LogError("Failed to retrieve query. Status code: {StatusCode}", response.StatusCode);
                return string.Empty;
            }, logger);
        }

        private async Task<string> ExecuteWithRetry(Func<Task<string>> func, ILogger logger)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    return await func();
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized || ex.Message.Contains("401"))
                {
                    authentication.Invalidate(); // Force refresh token on next attempt
                    if (attempt == 3)
                    {
                        logger.LogError($"Failed to retrieve query. error code: {System.Net.HttpStatusCode.Unauthorized}");
                        throw;
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests || ex.Message.Contains("429"))
                {
                    await Task.Delay(500 * attempt);
                    if (attempt == 3)
                    {
                        logger.LogError($"Failed to retrieve query. error code: {System.Net.HttpStatusCode.TooManyRequests}");
                        throw;
                    }
                }
            }

            logger.LogError("Failed to retrieve query. Too many attempts");
            throw new Exception("Unexpected error during FHIR execution routing.");
        }
    }
}