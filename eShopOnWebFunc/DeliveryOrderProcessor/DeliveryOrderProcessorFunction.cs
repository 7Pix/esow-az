using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeliveryOrderProcessor
{
    public static class DeliveryOrderProcessorFunction
    {
        private readonly static string CosmosDbUrl = Environment.GetEnvironmentVariable("CosmosDbUrl");
        private readonly static string CosmosPrimaryKey = Environment.GetEnvironmentVariable("CosmosPrimaryKey");
        private readonly static string DatabaseId = Environment.GetEnvironmentVariable("CosmosDatabaseId");
        private readonly static string ContainerId = Environment.GetEnvironmentVariable("CosmosContainerId");
        private readonly static string PartitionKeyPath = Environment.GetEnvironmentVariable("CosmosPartitionKeyPath");

        [FunctionName("DeliveryOrderProcessor")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request, ILogger logger)
        {
            logger.LogInformation("Start request processing");
            var requestBody = await request.ReadAsStringAsync();
            var deliveryRequest = JsonConvert.DeserializeObject<DeliveryProcessRequest>(requestBody);

            using var cosmosClient = new CosmosClient(CosmosDbUrl, CosmosPrimaryKey);
            var database = cosmosClient.GetDatabase(DatabaseId);
            var container = await database.CreateContainerIfNotExistsAsync(ContainerId, PartitionKeyPath);
            var createdItem = await container.Container.CreateItemAsync(deliveryRequest, new PartitionKey(deliveryRequest.Id));

            logger.LogInformation($"Delivery request: Id: {deliveryRequest.Id}; Status Code: {createdItem.StatusCode}");
            return new OkResult();
        }
    }

    public class DeliveryProcessRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("finalPrice")]
        public decimal FinalPrice { get; set; }

        [JsonProperty("items")]
        public IReadOnlyCollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public class OrderItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("itemOrdered")]
        public CatalogItemOrdered ItemOrdered { get; set; }

        [JsonProperty("unitPrice")]
        public decimal UnitPrice { get; set; }

        [JsonProperty("units")]
        public int Units { get; set; }
    }

    public class CatalogItemOrdered
    {
        [JsonProperty("catalogItemId")]
        public int CatalogItemId { get; set; }

        [JsonProperty("productName")]
        public string ProductName { get; set; }

        [JsonProperty("pictureUri")]
        public string PictureUri { get; set; }
    }
}
