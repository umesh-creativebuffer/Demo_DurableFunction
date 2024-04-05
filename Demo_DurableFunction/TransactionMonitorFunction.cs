using Demo_DurableFunction.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Demo_DurableFunction
{
    public static class TransactionMonitorFunction
    {
        [FunctionName("TransactionMonitorFunction")]
        public static async Task<bool> RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            // Retrieve incoming message from the message bus
            var transaction = context.GetInput<TransactionModel>();

            // Load tenant specific settings
            var tenantSettings = await context.CallActivityAsync<TenantSettings>("LoadTenantSettings", transaction.TenantId);

            // Assess the incoming message against the restrictions defined in the Tenant settings
            var violations = AssessRestrictions(transaction, tenantSettings);

            if (violations.Any())
            {
                // Send the payment to a holding queue for assessment
                await context.CallActivityAsync("SendToHoldingQueue", transaction);
                // Raise an event for the violations
                await context.CallActivityAsync("RaiseViolationEvent", violations);
            }
            else
            {
                // Send the payment to processing queue if no tenant settings are violated
                await context.CallActivityAsync("SendToProcessingQueue", transaction);
            }
            return violations.Any();
        }

        /// <summary>
        /// send transaction to holding queue
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="log"></param>
        [FunctionName("SendToHoldingQueue")]
        public static void SendToHoldingQueue([ActivityTrigger] TransactionModel transaction, ILogger log)
        {
            log.LogInformation($"Sending transaction {transaction.TransactionId} to holding queue for assessment.");
        }

        /// <summary>
        /// raise violation event for a failed/restricted transaction
        /// </summary>
        /// <param name="violations"></param>
        /// <param name="log"></param>
        [FunctionName("RaiseViolationEvent")]
        public static void RaiseViolationEvent([ActivityTrigger] List<string> violations, ILogger log)
        {
            log.LogWarning($"Violations detected: {string.Join(", ", violations)}");
        }

        /// <summary>
        /// send transaction to processing queue
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="log"></param>
        [FunctionName("SendToProcessingQueue")]
        public static void SendToProcessingQueue([ActivityTrigger] TransactionModel transaction, ILogger log)
        {
            log.LogInformation($"Sending transaction {transaction.TransactionId} to processing queue.");
        }

        /// <summary>
        ///  load tenant settings based on tenant id 
        /// </summary>
        /// <param name="tenantId"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("LoadTenantSettings")]
        public static TenantSettings LoadTenantSettings([ActivityTrigger] string tenantId, ILogger log)
        {
            // Specify the path to your JSON file
            string filePath = string.Format("{0}/Content/TenantSettings.json", Directory.GetCurrentDirectory()) ;

            // Check if the file exists
            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found.");
                return null;
            }
            else
            {
                // Read the JSON file
                string tenantSettingsJson = File.ReadAllText(filePath);

                // deserialize object
                var tenantSettings = JsonConvert.DeserializeObject<TenantWrapper>(tenantSettingsJson);

                return tenantSettings.TenantSettings.FirstOrDefault(x => x.TenantId == tenantId);
            }
        }

        /// <summary>
        /// check for the violations based on transaction & tenant settings
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="tenantSettings"></param>
        /// <returns></returns>
        private static List<string> AssessRestrictions(TransactionModel transaction, TenantSettings tenantSettings)
        {
            var violations = new List<string>();

            decimal perTransactionLimit = Convert.ToDecimal(tenantSettings.Thresholds.PerTransaction);
            decimal transactionAmount = Convert.ToDecimal(transaction.Amount);

            if (transactionAmount > perTransactionLimit)
            {
                violations.Add($"Threshold limit exceeded. Per Transaction limit: {perTransactionLimit}. Transaction amount: {transactionAmount}");
            }

            if (!tenantSettings.CountrySanctions.SourceCountries.Contains(transaction.SourceAccount.CountryCode))
            {
                violations.Add("Transaction cannot be done from an unauthorised source country.");
            }

            if (!tenantSettings.CountrySanctions.DestinationCountries.Contains(transaction.DestinationAccount.CountryCode))
            {
                violations.Add("Transaction cannot be done to an unauthorised destination country.");
            }

            return violations;
        }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="req"></param>
        ///// <param name="starter"></param>
        ///// <param name="log"></param>
        ///// <returns></returns>
        //[FunctionName("TransactionMonitorFunction_HttpStart")]
        //public static async Task<HttpResponseMessage> HttpStart(
        //[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
        //[DurableClient] IDurableOrchestrationClient starter, ILogger log)
        //{

        //    string json = @"{ ""correlationId"" : ""0EC1D320-3FDD-43A0-84B8-3CF8972CDCD8"", ""tenantId"" : ""345"", ""transactionId"" : ""eyJpZCI6ImE2NDUzYTZlLTk1NjYtNDFmOC05ZjAzLTg3ZDVmMWQ3YTgxNSIsImlzIjoiU3RhcmxpbmciLCJydCI6InBheW1lbnQifQ"", ""transactionDate"" : ""2024-02-15 11:36:22"", ""direction"": ""Credit"", ""amount"" : ""345.87"", ""currency"" : ""EUR"", ""description"" : ""Mr C A Woods"", ""sourceaccount"": { ""accountno"" : ""44421232"", ""sortcode"" : ""30-23-20"", ""countrycode"" : ""GBR"" }, ""destinationaccount"": { ""accountno"" : ""87285552"", ""sortcode"" : ""10-33-12"", ""countrycode"" : ""HKG"" } }";

        //    // Read the JSON file
        //    var tenantSettingsJson = JsonConvert.DeserializeObject<TransactionModel>(json);

        //    // Function input comes from the request content.
        //    string instanceId = await starter.StartNewAsync("TransactionMonitorFunction", tenantSettingsJson);

        //    log.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);

        //    return starter.CreateCheckStatusResponse(req, instanceId);
        //}

        /// <summary>
        /// Porcess transaction 
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="starter"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("ProcessTransactionMessage")]
        public static async Task ProcessTransactionMessage(
            [ServiceBusTrigger("transaction-topic", "subscription", Connection = "ServiceBusConnectionString")] TransactionModel transaction,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Start the orchestration
            string instanceId = await starter.StartNewAsync("TransactionMonitorFunction", transaction);
            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

    }
}