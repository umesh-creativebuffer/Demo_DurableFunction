using Azure.Storage.Blobs;
using Demo_DurableFunction.Models;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // Load Tenant Velocity
            var tenantVelocities = await context.CallActivityAsync<TenantVelocity>("ReadFromBlob", transaction);

            // Assess the incoming message against the restrictions defined in the Tenant settings
            var violations = AssessRestrictions(transaction, tenantSettings, tenantVelocities);

            if (violations.Any())
            {
                // Send the payment to a holding queue for assessment
                await context.CallActivityAsync("SaveToBlob", transaction);
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
            string filePath = string.Format("{0}/Content/TenantSettings.json", Directory.GetCurrentDirectory());

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
        private static List<string> AssessRestrictions(TransactionModel transaction, TenantSettings tenantSettings, TenantVelocity tenantVelocity)
        {
            var violations = new List<string>();


            decimal dailyTransactionLimit = Convert.ToDecimal(tenantSettings.VelocityLimits.Daily);
            decimal dailyTansactionProcessed = Convert.ToDecimal(tenantVelocity?.PaymentProcessed ?? string.Empty);

            decimal perTransactionLimit = Convert.ToDecimal(tenantSettings.Thresholds.PerTransaction);
            decimal transactionAmount = Convert.ToDecimal(transaction.Amount);

            if (transactionAmount > perTransactionLimit)
            {
                violations.Add($"Per Transaction limit exceeded. Per Transaction limit: {perTransactionLimit}. Transaction amount: {transactionAmount}");
            }

            if (tenantVelocity != null && (dailyTansactionProcessed + transactionAmount) > dailyTransactionLimit)
            {
                violations.Add($"Daily Transaction limit exceeded. Daily Transaction limit: {dailyTransactionLimit}. Transaction amount already processed: {dailyTansactionProcessed}");
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

        [FunctionName("SaveToBlob")]
        public static async Task SaveToBlob([ActivityTrigger] TransactionModel transaction, ILogger log)
        {
            string blobName = "exampleBlob.txt";

            var tenantVelocityWrapper = await ReadFromBlobAsync(blobName);

            tenantVelocityWrapper = tenantVelocityWrapper ?? new TenantVelocityWrapper();

            if (tenantVelocityWrapper.TenantVelocities.Any(x => x.TenantId == transaction.TenantId && x.TransactionDate.HasValue && x.TransactionDate == transaction.TransactionDate))
            {
                foreach (var item in tenantVelocityWrapper.TenantVelocities.Where(x => x.TenantId == transaction.TenantId && x.TransactionDate.HasValue && x.TransactionDate == transaction.TransactionDate))
                {
                    item.PaymentProcessed = item.PaymentProcessed + transaction.Amount;
                }
            }
            else
            {
                tenantVelocityWrapper.TenantVelocities.Add(new TenantVelocity
                {
                    TenantId = transaction.TenantId,
                    Date = transaction.TransactionDate.ToString("yyyy-MM-dd"),
                    PaymentProcessed = transaction.Amount
                });
            }

            await SaveToBlobAsync(JsonConvert.SerializeObject(tenantVelocityWrapper), blobName);
        }

        public static async Task SaveToBlobAsync(string data, string blobName)
        {
            string connectionString = "<your_storage_account_connection_string>";
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("<container_name>");
            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (StreamWriter writer = new StreamWriter(memoryStream))
                {
                    await writer.WriteAsync(data);
                    await writer.FlushAsync();
                    memoryStream.Position = 0;
                    await blobClient.UploadAsync(memoryStream, true);
                }
            }
        }

        [FunctionName("ReadFromBlob")]
        public static async Task<TenantVelocity> ReadFromBlob([ActivityTrigger] TransactionModel model, ILogger log)
        {
            string blobName = "exampleBlob.txt";
            var tenantVelocities = await ReadFromBlobAsync(blobName);
            return tenantVelocities.TenantVelocities.FirstOrDefault(x => x.TenantId == model.TenantId && x.TransactionDate.HasValue && x.TransactionDate == model.TransactionDate);
        }

        public static async Task<TenantVelocityWrapper> ReadFromBlobAsync(string blobName)
        {
            string connectionString = "<your_storage_account_connection_string>";
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("<container_name>");
            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            string content = string.Empty;
            var response = await blobClient.DownloadAsync();
            using (var streamReader = new StreamReader(response.Value.Content))
            {
                content = await streamReader.ReadToEndAsync();
            }
            // deserialize object
            return JsonConvert.DeserializeObject<TenantVelocityWrapper>(content);
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