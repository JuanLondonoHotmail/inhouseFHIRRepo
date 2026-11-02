namespace MSE.UEPR.FHIR.Services
{
    public class APISettings
    {
        /// <summary>
        /// Address for identity provider
        /// </summary>
        public string Authority { get; set; } = string.Empty;

        /// <summary>
        /// ClientId to pass to authority
        /// </summary>
        public string client_id { get; set; } = string.Empty;

        /// <summary>
        /// Client Secret to pass to authority
        /// </summary>
        public string client_secret { get; set; } = string.Empty;

        /// <summary>
        /// Scope setting to pass to fhirserverroot_url
        /// </summary>
        public string fhirserverroot_url { get; set; } = string.Empty;

        /// <summary>
        /// Service account grant type for the api
        /// </summary>
        public string grant_type { get; set; } = string.Empty;
    }
}