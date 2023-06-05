using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Newtonsoft.Json;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOrderReserverConfiguration _orderReserverConfiguration;
    private readonly IDeliveryOrderProcessorConfiguration _deliveryOrderProcessorConfiguration;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IHttpClientFactory httpClientFactory,
        IOrderReserverConfiguration orderReserverConfiguration,
        IDeliveryOrderProcessorConfiguration deliveryOrderProcessorConfiguration)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _httpClientFactory = httpClientFactory;
        _orderReserverConfiguration = orderReserverConfiguration;
        _deliveryOrderProcessorConfiguration = deliveryOrderProcessorConfiguration;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);

        await ReserveOrderItemsAsync(order);
        await SendDeliveryOrderRequestAsync(order);
    }

    private async Task ReserveOrderItemsAsync(Order order)
    {
        var orderItems = order.OrderItems
            .Select(item => new OrderItemsReserverRequest
            {
                ItemId = item.Id.ToString(),
                Quantity = item.Units
            })
            .Select(JsonConvert.SerializeObject)
            .Select(Encoding.UTF8.GetBytes)
            .ToList();

        await using var serviceBusClient = new ServiceBusClient(_orderReserverConfiguration.ServiceBusConnectionString);
        var sender = serviceBusClient.CreateSender(_orderReserverConfiguration.ServiceBusTopicName);

        var sendMessageRequests = new List<Task>(orderItems.Count);
        foreach (var item in orderItems)
        {
            var serviceBusMessage = new ServiceBusMessage(item);
            sendMessageRequests.Add(sender.SendMessageAsync(serviceBusMessage));
        }

        await Task.WhenAll(sendMessageRequests);
    }

    private async Task SendDeliveryOrderRequestAsync(Order order)
    {
        using var httpClient = _httpClientFactory.CreateClient();

        var deliveryProcessRequest = new DeliveryProcessRequest
        {
            Id = order.Id.ToString(),
            FinalPrice = order.Total(),
            Items = order.OrderItems
        };

        var deliveryProcessRequestJson = JsonConvert.SerializeObject(deliveryProcessRequest);
        var deliveryProcessRequestContent = new StringContent(deliveryProcessRequestJson);

        await httpClient.PostAsync(_deliveryOrderProcessorConfiguration.DeliveryOrderProcessorUrl, deliveryProcessRequestContent);
    }
}

public interface IOrderReserverConfiguration
{
    public string ServiceBusConnectionString { get; set; }
    public string ServiceBusTopicName { get; set; }
}

public class OrderReserverConfiguration : IOrderReserverConfiguration
{
    public string ServiceBusConnectionString { get; set; }
    public string ServiceBusTopicName { get; set; }
}

public interface IDeliveryOrderProcessorConfiguration
{
    public string DeliveryOrderProcessorUrl { get; set; }
}

public class DeliveryOrderProcessorConfiguration : IDeliveryOrderProcessorConfiguration
{
    public string DeliveryOrderProcessorUrl { get; set; }
}

public class OrderItemsReserverRequest
{
    [JsonProperty("itemId")]
    public string ItemId { get; set; }

    [JsonProperty("quantity")]
    public int Quantity { get; set; }
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
