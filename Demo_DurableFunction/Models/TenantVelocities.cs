using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Demo_DurableFunction.Models
{
    public class TenantVelocityWrapper
    {
        [JsonProperty("tenantVelocities")]
        public List<TenantVelocity> TenantVelocities { get; set; }
    }

    public class TenantVelocity
    {

        [JsonProperty("tenantid")]
        public string TenantId { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }
        public DateTime? TransactionDate => string.IsNullOrEmpty(Date) ? null : Convert.ToDateTime(Date);

        [JsonProperty("paymentProcessed")]
        public string PaymentProcessed { get; set; }
    }
}
