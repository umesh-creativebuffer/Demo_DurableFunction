using Newtonsoft.Json;

namespace Demo_DurableFunction.Models
{
    public class TransactionModel
    {
        [JsonProperty("correlationId")]
        public string CorrelationId { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("transactionId")]
        public string TransactionId { get; set; }

        [JsonProperty("transactionDate")]
        public string TransactionDate { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }

        [JsonProperty("amount")]
        public string Amount { get; set; }

        [JsonProperty("currency")]
        public string Currency { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("sourceaccount")]
        public AccountInfo SourceAccount { get; set; }

        [JsonProperty("destinationaccount")]
        public AccountInfo DestinationAccount { get; set; }
    }

    public class AccountInfo
    {

        [JsonProperty("accountno")]
        public string AccountNo { get; set; }

        [JsonProperty("sortcode")]
        public string SortCode { get; set; }

        [JsonProperty("countrycode")]
        public string CountryCode { get; set; }
    }
}
