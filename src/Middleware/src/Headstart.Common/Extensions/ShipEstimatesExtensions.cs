using Headstart.Common.Constants;
using Headstart.Common.Services.ShippingIntegration.Models;
using Headstart.Models.Headstart;
using ordercloud.integrations.exchangerates;
using OrderCloud.SDK;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Headstart.Common.Extensions
{
    public static class ShipEstimatesExtensions
    {
        public static IList<HSShipEstimate> CheckForEmptyRates(this IList<HSShipEstimate> estimates, decimal noRatesCost, int noRatesTransitDays)
        {
            // if there are no rates for a set of line items then return a mocked response so user can check out
            // this order will additionally get marked as needing attention

            foreach (var shipEstimate in estimates)
            {
                if (!shipEstimate.ShipMethods.Any())
                {
                    shipEstimate.ShipMethods = new List<HSShipMethod>()
                    {
                        new HSShipMethod
                        {
                            ID = ShippingConstants.NoRatesID,
                            Name = "No shipping rates",
                            Cost = noRatesCost,
                            EstimatedTransitDays = noRatesTransitDays,
                            xp = new ShipMethodXP
                            {
                                OriginalCost = noRatesCost
                            }
                        }
                    };
                }
            }
            return estimates;
        }

        public static async Task<IList<HSShipEstimate>> ApplyShippingLogic(this IList<HSShipEstimate> shipEstimates, HSOrderWorksheet orderWorksheet, IOrderCloudClient _oc, int freeShippingTransitDays)
        {
            var updatedEstimates = new List<HSShipEstimate>();
            var supplierIDs = orderWorksheet.LineItems.Select(li => li.SupplierID);
            var suppliers = await _oc.Suppliers.ListAsync<HSSupplier>(filters: $"ID={string.Join("|", supplierIDs)}");
            
            foreach (var shipEstimate in shipEstimates)
            {
                var supplierID = orderWorksheet.LineItems.FirstOrDefault(li => li.ID == shipEstimate.ShipEstimateItems.FirstOrDefault()?.LineItemID)?.SupplierID;
                var supplier = suppliers.Items.FirstOrDefault(s => s.ID == supplierID);
                var supplierLineItems = orderWorksheet.GetBuyerLineItemsBySupplierID(supplier?.ID);
                var supplierSubTotal = supplierLineItems?.Select(li => li.LineSubtotal).Sum();
 
                updatedEstimates.Add(shipEstimate);
            }

            // Filter out any rates that are _not_ Fedex Ground, Fedex 2 day, and Fedex Standard Overnight
            updatedEstimates = shipEstimates.Select(estimate => FilterDownFedexShippingRates(estimate)).ToList();
            return updatedEstimates;
        }
        public static async Task<IList<HSShipEstimate>> ConvertCurrency(this IList<HSShipEstimate> shipEstimates, CurrencySymbol shipperCurrency, CurrencySymbol buyerCurrency, IExchangeRatesCommand _exchangeRates)
        {
            // If the Buyer's currency is USD, do not convert rates.
            if (buyerCurrency == CurrencySymbol.USD) { return shipEstimates; };

            var rates = (await _exchangeRates.Get(buyerCurrency)).Rates;
            var conversionRate = rates.Find(r => r.Currency == shipperCurrency).Rate;
            return shipEstimates.Select(estimate =>
            {
                estimate.ShipMethods = estimate.ShipMethods.Select(method =>
                {
                    method.xp.OriginalCurrency = shipperCurrency;
                    method.xp.OrderCurrency = buyerCurrency;
                    method.xp.ExchangeRate = conversionRate;
                    if (conversionRate != null) method.Cost /= (decimal)conversionRate;
                    return method;
                }).ToList();
                return estimate;
            }).ToList();
        }

        #region Helper Methods
        public static HSShipEstimate FilterDownFedexShippingRates(HSShipEstimate estimate)
        {
            estimate.ShipMethods = estimate.ShipMethods.Where(method => (method.ID != null && method.ID.Contains("FREE_SHIPPING")) || method?.ID == ShippingConstants.NoRatesID || method?.xp?.Carrier == "USPS" || method.Name == "FEDEX_GROUND" || method.Name == "FEDEX_2_DAY" || method.Name == "STANDARD_OVERNIGHT").ToList();
            return estimate;
        }
        #endregion
    }
}
