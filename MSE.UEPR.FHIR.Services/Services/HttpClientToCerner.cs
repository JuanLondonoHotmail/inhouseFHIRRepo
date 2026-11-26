using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Abstractions;
using Microsoft.ApplicationInsights;
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
        TelemetryClient _telemetryClient,
        AppSettings appSettings) : IHttpClientToCerner

    {
        // MAIN method to GET a FHIR resource by ID
        public async Task<string> GetResourceByIdAsync(Entity resourceType, string id, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(resourceType);
            ArgumentNullException.ThrowIfNull(id);
            // Thread-safe token acquisition step if needed before execution
            await authentication.EnsureHttpClientAuthenticatedAsync(httpClient);

            return await ExecuteWithRetry(async () =>
            {
                var response = await httpClient.GetAsync(appSettings.Cerner_Settings.FhirServerRootUrl + string.Format("{0}/{1}", resourceType.ToString(), id));

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                // Read error content if available to provide a richer exception context
                var errorContent = await response.Content.ReadAsStringAsync();
                var innerException = new HttpRequestException($"FHIR server returned {(int)response.StatusCode} ({response.StatusCode}). Details: {errorContent}");
                var customEx = new ServiceUnavailableException("Failed to search Async.", innerException);

                // Log BEFORE throwing; otherwise, logging code is unreachable
                logger.LogError(customEx, "Failed to retrieve {ResourceType} demographics. Status code: {StatusCode}", resourceType, response.StatusCode);

                _telemetryClient.TrackException(customEx);

                throw customEx;
            }, logger);
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
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(resourceType);

            // Ensure the client has valid, unexpired authentication headers
            await authentication.EnsureHttpClientAuthenticatedAsync(httpClient);

            // Build query safely; use standard collection-based URL building if 'query' needs encoding
            var url = $"{appSettings.Cerner_Settings.FhirServerRootUrl}{resourceType}?{query}";

            return await ExecuteWithRetry(async () =>
            {
                // Use HttpCompletionOption.ResponseHeadersRead for better performance and memory efficiency
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                // Read error content if available to provide a richer exception context
                var errorContent = await response.Content.ReadAsStringAsync();
                var innerException = new HttpRequestException($"FHIR server returned {(int)response.StatusCode} ({response.StatusCode}). Details: {errorContent}");
                var customEx = new ServiceUnavailableException("Failed to search Async.", innerException);

                // Log BEFORE throwing; otherwise, logging code is unreachable
                logger.LogError(customEx, "Failed to retrieve {ResourceType} demographics. Status code: {StatusCode}", resourceType, response.StatusCode);

                _telemetryClient.TrackException(customEx);

                throw customEx;
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
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(query);

            await authentication.EnsureHttpClientAuthenticatedAsync(httpClient);

            return await ExecuteWithRetry(async () =>
            {
                var response = await httpClient.GetAsync(query);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                // Read error content if available to provide a richer exception context
                var errorContent = await response.Content.ReadAsStringAsync();
                var innerException = new HttpRequestException($"FHIR server returned {(int)response.StatusCode} ({response.StatusCode}). Details: {errorContent}");
                var customEx = new ServiceUnavailableException("Failed to search Async.", innerException);

                // Log BEFORE throwing; otherwise, logging code is unreachable
                logger.LogError(customEx, "Failed to retrieve demographics. Status code: {StatusCode}", response.StatusCode);

                _telemetryClient.TrackException(customEx);

                throw customEx;
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
                        logger.LogError(ex, $"Failed to retrieve query. error code: {System.Net.HttpStatusCode.Unauthorized}");
                        var customEx = new ServiceUnavailableException("Failed to retrieve the FHIR access token due to a network error.", ex);
                        _telemetryClient.TrackException(customEx);
                        throw customEx;
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests || ex.Message.Contains("429"))
                {
                    await Task.Delay(500 * attempt);
                    if (attempt == 3)
                    {
                        logger.LogError(ex, $"Failed to retrieve query. error code: {System.Net.HttpStatusCode.TooManyRequests}");
                        throw;
                    }
                }
            }

            logger.LogError("Failed to retrieve query. Too many attempts");
            throw new Exception("Unexpected error during FHIR execution routing.");
        }
    }
}