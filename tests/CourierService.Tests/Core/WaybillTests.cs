using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Orders;

namespace CourierService.Tests.Core;

/// <summary>Повне замовлення з пов'язаними даними — таким його повертає картка замовлення.</summary>
internal static class WaybillTestData
{
    public static Order FullOrder(OrderStatus status = OrderStatus.New, PaymentPayer payer = PaymentPayer.Sender,
        PaymentMethod method = PaymentMethod.Cash)
    {
        Order order = WorkflowTestData.InStatus(status, payer, method);
        order.CreatedAt = new DateTime(2026, 10, 19, 10, 15, 0);
        order.Deadline = new DateTime(2026, 10, 19, 14, 15, 0);
        order.Tariff = new Tariff { Name = "Експрес" };
        order.Client = new Client { Name = "ТОВ «Квіткова крамниця»", Phone = "+380501234567" };
        order.PickupAddress = "вул. Хрещатик, 22";
        order.PickupZone = new Zone { Name = "Шевченківський" };
        order.RecipientName = "Оксана Мельник";
        order.RecipientPhone = "+380931112233";
        order.DeliveryAddress = "просп. Миру, 5, кв. 12";
        order.DeliveryZone = new Zone { Name = "Дніпровський" };
        order.WeightGrams = 1_250;
        order.ChargeableWeightGrams = 1_500;
        order.LengthCm = 30;
        order.WidthCm = 20;
        order.HeightCm = 10;
        return order;
    }
}

public class WaybillTests
{
    [Fact]
    public void From_FillsPartiesDatesAndParcel()
    {
        Waybill waybill = Waybill.From(WaybillTestData.FullOrder());

        Assert.Equal("KV2610190001-2", waybill.TrackingNumber);
        Assert.Equal("Експрес", waybill.Tariff);
        Assert.Equal(new WaybillParty("ТОВ «Квіткова крамниця»", "+380 (50) 123-45-67", "вул. Хрещатик, 22", "Шевченківський"), waybill.Sender);
        Assert.Equal(new WaybillParty("Оксана Мельник", "+380 (93) 111-22-33", "просп. Миру, 5, кв. 12", "Дніпровський"), waybill.Recipient);
        Assert.Equal(["Вага: 1,25 кг (розрахункова 1,5 кг)", "Габарити: 30 × 20 × 10 см"], waybill.Parcel);
        Assert.Equal("Оформлено 19.10.2026 10:15 · доставити до 19.10.2026 14:15", waybill.DatesText);
        Assert.Equal(Money.FromHryvnias(157m), waybill.Price);
        Assert.Null(waybill.Courier);
        Assert.Null(waybill.Comment);
    }

    [Fact]
    public void From_DeclaredValueFragileCommentAndCourier_AreShown()
    {
        Order order = WaybillTestData.FullOrder(OrderStatus.Assigned);
        order.DeclaredValue = Money.FromHryvnias(2_000m);
        order.IsFragile = true;
        order.Comment = "  Дзвонити за 30 хв  ";
        order.Courier = new Courier { User = new User { FullName = "Андрій Ткаченко" } };

        Waybill waybill = Waybill.From(order);

        Assert.Contains($"Оголошена цінність: {Money.FromHryvnias(2_000m)}", waybill.Parcel);
        Assert.Contains("КРИХКА — обережно!", waybill.Parcel);
        Assert.Equal("Дзвонити за 30 хв", waybill.Comment);
        Assert.Equal("Андрій Ткаченко", waybill.Courier);
    }

    [Fact]
    public void From_RecipientNotPaidYet_CourierCollectsOnDelivery()
    {
        Waybill waybill = Waybill.From(WaybillTestData.FullOrder(payer: PaymentPayer.Recipient));

        Assert.Equal(Money.FromHryvnias(157m), waybill.CollectOnDelivery);
        Assert.Equal("Платник: одержувач, готівка — очікує", waybill.PaymentText);
    }

    [Fact]
    public void From_RecipientAlreadyPaid_NothingToCollect()
    {
        Order order = WaybillTestData.FullOrder(payer: PaymentPayer.Recipient, method: PaymentMethod.Card);
        order.Payment!.Status = PaymentStatus.Paid;

        Waybill waybill = Waybill.From(order);

        Assert.Null(waybill.CollectOnDelivery);
        Assert.Equal("Платник: одержувач, картка — оплачено", waybill.PaymentText);
    }

    [Fact]
    public void From_SenderPays_NothingToCollectFromRecipient()
    {
        Waybill waybill = Waybill.From(WaybillTestData.FullOrder(payer: PaymentPayer.Sender));

        Assert.Null(waybill.CollectOnDelivery);
    }

    [Fact]
    public void From_NoPayment_SaysSo()
    {
        Order order = WaybillTestData.FullOrder();
        order.Payment = null;

        Waybill waybill = Waybill.From(order);

        Assert.Equal("Оплату не оформлено", waybill.PaymentText);
        Assert.Null(waybill.CollectOnDelivery);
    }

    [Theory]
    [InlineData(OrderStatus.New, true)]
    [InlineData(OrderStatus.Assigned, true)]
    [InlineData(OrderStatus.PickedUp, true)]
    [InlineData(OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.DeliveryFailed, true)]
    [InlineData(OrderStatus.Returned, true)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void CanPrint_EveryStatusExceptCancelled(OrderStatus status, bool expected)
    {
        Assert.Equal(expected, Waybill.CanPrint(WaybillTestData.FullOrder(status)));
    }

    [Fact]
    public void From_Cancelled_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Waybill.From(WaybillTestData.FullOrder(OrderStatus.Cancelled)));
    }
}
