using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OrderItemsReserver
{
    public static class OrderItemsReserverFunction
    {
        private readonly static string ContainerName = Environment.GetEnvironmentVariable("ContainerName");
        private readonly static string SasSignature = Environment.GetEnvironmentVariable("SasSignature");
        private readonly static Uri StorageAccountUri = new Uri(Environment.GetEnvironmentVariable("StorageAccountUri"));

        [FunctionName("OrderItemsReserver")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request, ILogger logger)
        {
            logger.LogInformation("Start request processing");

            BlobOrderInfo blobOrderInfo;
            try
            {
                var requestBody = await request.ReadAsStringAsync();
                logger.LogInformation($"Request information: {requestBody}");
                var orderInfo = JsonConvert.DeserializeObject<OrderInfo>(requestBody);
                blobOrderInfo = await SaveOrderInfoAsync(orderInfo);
                logger.LogInformation($"The order sucessfully saved (Blob name: {blobOrderInfo?.Name})");
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }

            return new OkObjectResult(blobOrderInfo);
        }

        private static async Task<BlobOrderInfo> SaveOrderInfoAsync(OrderInfo order)
        {
            var sasCredential = new AzureSasCredential(SasSignature);
            var blobClient = new BlobServiceClient(StorageAccountUri, sasCredential);
            var blobContainerClient = blobClient.GetBlobContainerClient(ContainerName);

            var blobName = $"{Guid.NewGuid()}.json";
            var blobJson = JsonConvert.SerializeObject(order);
            var blobBytes = Encoding.UTF8.GetBytes(blobJson);
            using var blobStream = new MemoryStream(blobBytes);
            await blobContainerClient.UploadBlobAsync(blobName, blobStream);

            return new BlobOrderInfo(blobName, order);
        }

        public record OrderInfo
        {
            [JsonProperty("itemId")]
            public string ItemId { get; set; }

            [JsonProperty("quantity")]
            public int Quantity { get; set; }
        }

        public record BlobOrderInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("order")]
            public OrderInfo Order { get; set; }

            public BlobOrderInfo(string name, OrderInfo order)
                => (Name, Order) = (name, order);
        }
    }
}
