﻿using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace eShop.Ordering.API.Application.Commands;

using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

// Regular CommandHandler
public class CreateOrderCommandHandler
    : IRequestHandler<CreateOrderCommand, bool>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IIdentityService _identityService;
    private readonly IMediator _mediator;
    private readonly IOrderingIntegrationEventService _orderingIntegrationEventService;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly Counter<long> _orderPlacedCounter;
    private readonly Histogram<double> _totalPurchaseAmountHistogram;
    private readonly UpDownCounter<int> _activeOrdersGauge;
    private readonly Histogram<double> _orderValueHistogram;
    private readonly Counter<double> _totalRevenueCounter;
    // private readonly Counter<int> _orderItemQuantityCounter;
    private readonly Counter<int> _OrdersByUserCounter;

    // Using DI to inject infrastructure persistence Repositories
    public CreateOrderCommandHandler(IMediator mediator,
        IOrderingIntegrationEventService orderingIntegrationEventService,
        IOrderRepository orderRepository,
        IIdentityService identityService,
        ILogger<CreateOrderCommandHandler> logger,
        Counter<long> orderPlacedCounter,
        Histogram<double> totalPurchaseAmountHistogram,
        UpDownCounter<int> activeOrdersGauge,
        Histogram<double> orderValueHistogram,
        Counter<double> totalRevenueCounter,
        // Counter<int> orderItemQuantityCounter,
        Counter<int> OrdersByUserCounter)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _orderingIntegrationEventService = orderingIntegrationEventService ?? throw new ArgumentNullException(nameof(orderingIntegrationEventService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orderPlacedCounter = orderPlacedCounter ?? throw new ArgumentNullException(nameof(orderPlacedCounter));
        _totalPurchaseAmountHistogram = totalPurchaseAmountHistogram ?? throw new ArgumentNullException(nameof(totalPurchaseAmountHistogram)); // Assign histogram
        _activeOrdersGauge = activeOrdersGauge ?? throw new ArgumentNullException(nameof(activeOrdersGauge));
        _orderValueHistogram = orderValueHistogram ?? throw new ArgumentNullException(nameof(orderValueHistogram));
        _totalRevenueCounter = totalRevenueCounter ?? throw new ArgumentNullException(nameof(totalRevenueCounter));
        // _orderItemQuantityCounter = orderItemQuantityCounter ?? throw new ArgumentNullException(nameof(orderItemQuantityCounter));
        _OrdersByUserCounter = OrdersByUserCounter ?? throw new ArgumentNullException(nameof(OrdersByUserCounter));
    }

    // Helpers to mask sensitive data
    private static string MaskUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return userId;

        // If it's a UUID/GUID format, keep first and last components
        if (Guid.TryParse(userId, out _))
        {
            var parts = userId.Split('-');
            if (parts.Length >= 5)
            {
                return $"{parts[0]}-****-****-****-{parts[4]}";
            }
        }

        // If it's an email, mask the local part except first character
        if (userId.Contains('@'))
        {
            var parts = userId.Split('@');
            if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]))
            {
                return $"{parts[0][0]}***@{parts[1]}";
            }
        }

        // For other formats, mask the middle portion
        if (userId.Length > 6)
        {
            int visibleChars = Math.Max(2, userId.Length / 4);
            return $"{userId.Substring(0, visibleChars)}****{userId.Substring(userId.Length - visibleChars)}";
        }

        // For short IDs, just return a generic mask
        return "****";
    }

    // Mask String helper, if string is equal or lower than 3 chars it masks all but the first char
    private static string MaskString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Length <= 3)
        {
            return value[0] + new string('*', value.Length - 1);
        }

        return value.Substring(0, 3) + new string('*', value.Length - 3);
    }

    public async Task<bool> Handle(CreateOrderCommand message, CancellationToken cancellationToken)
    {
        // Add Integration event to clean the basket
        var orderStartedIntegrationEvent = new OrderStartedIntegrationEvent(message.UserId);
        await _orderingIntegrationEventService.AddAndSaveEventAsync(orderStartedIntegrationEvent);

        // Add/Update the Buyer AggregateRoot
        // DDD patterns comment: Add child entities and value-objects through the Order Aggregate-Root
        // methods and constructor so validations, invariants and business logic 
        // make sure that consistency is preserved across the whole aggregate
        var address = new Address(message.Street, message.City, message.State, message.Country, message.ZipCode);
        var order = new Order(message.UserId, message.UserName, address, message.CardTypeId, message.CardNumber, message.CardSecurityNumber, message.CardHolderName, message.CardExpiration);

        double totalAmount = 0;
        int itemCount = 0;

        foreach (var item in message.OrderItems)
        {
            order.AddOrderItem(item.ProductId, item.ProductName, item.UnitPrice, item.Discount, item.PictureUrl, item.Units);
            totalAmount += (double)(item.UnitPrice - item.Discount) * item.Units;
            itemCount += item.Units;
            // _orderItemQuantityCounter.Add(item.Units, new KeyValuePair<string, object>("itemName", item.ProductName));

        }

        _logger.LogInformation("Creating Order - Order: {@Order}", order);
        
        _orderPlacedCounter.Add(1, new KeyValuePair<string, object>("userId", message.UserId));
        _activeOrdersGauge.Add(1);
        _orderValueHistogram.Record(totalAmount, new KeyValuePair<string, object>("userId", MaskString(message.UserId)));
        _totalRevenueCounter.Add((long)totalAmount, new KeyValuePair<string, object>("userId",  MaskString(message.UserId)));
        _OrdersByUserCounter.Add(1, new KeyValuePair<string, object>("userName", MaskString(message.UserName)));
        
        _totalPurchaseAmountHistogram.Record(totalAmount, new KeyValuePair<string, object>("userId", MaskString(message.UserId)));
        _logger.LogInformation("Total Purchase Amount Recorded: {TotalAmount}", totalAmount);

        _orderRepository.Add(order);

        return await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
    }
}


// Use for Idempotency in Command process
public class CreateOrderIdentifiedCommandHandler : IdentifiedCommandHandler<CreateOrderCommand, bool>
{
    public CreateOrderIdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<CreateOrderCommand, bool>> logger)
        : base(mediator, requestManager, logger)
    {
    }

    protected override bool CreateResultForDuplicateRequest()
    {
        return true; // Ignore duplicate requests for creating order.
    }
}
