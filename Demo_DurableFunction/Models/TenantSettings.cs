using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace Demo_DurableFunction.Models
{
    public class CountrySanctions
    {
        [JsonProperty("sourcecountrycode")]
        public string SourceCountryCode { get; set; }
        [JsonProperty("destinationcountrycode")]
        public string DestinationCountryCode { get; set; }

        public IEnumerable<string> DestinationCountries => !string.IsNullOrEmpty(DestinationCountryCode) ? DestinationCountryCode.Split(",") : Enumerable.Empty<string>();
        public IEnumerable<string> SourceCountries => !string.IsNullOrEmpty(SourceCountryCode) ? SourceCountryCode.Split(",") : Enumerable.Empty<string>();
    }

    public class TenantWrapper
    {
        [JsonProperty("tenantsettings")]
        public IEnumerable<TenantSettings> TenantSettings { get; set; }
    }

    public class TenantSettings
    {
        [JsonProperty("tenantid")]
        public string TenantId { get; set; }
        [JsonProperty("velocitylimits")]
        public VelocityLimit VelocityLimits { get; set; }
        [JsonProperty("thresholds")]
        public Threshold Thresholds { get; set; }
        [JsonProperty("countrysanctions")]
        public CountrySanctions CountrySanctions { get; set; }
    }

    public class Threshold
    {
        [JsonProperty("pertransaction")]
        public string PerTransaction { get; set; }
    }

    public class VelocityLimit
    {
        [JsonProperty("daily")]
        public string Daily { get; set; }
    }


}
