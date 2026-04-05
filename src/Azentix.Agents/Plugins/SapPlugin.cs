using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class SapPlugin
{
    private readonly HttpClient _http;
    private readonly ILogger<SapPlugin> _logger;
    private readonly SapConfiguration _config;

    public SapPlugin(HttpClient http, ILogger<SapPlugin> logger, SapConfiguration config)
    {
        _http = http; _logger = logger; _config = config;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("APIKey", config.ApiKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    [KernelFunction("sap_get_material")]
    [Description("Get product/material master data from SAP. Returns description, unit, material group.")]
    public async Task<string> GetMaterialAsync(
        [Description("SAP material number e.g. MAT-001234")] string materialNumber)
    {
        _logger.LogInformation("SAP GetMaterial: {Mat}", materialNumber);
        try {
            var resp = await _http.GetAsync(
                $"{_config.BaseUrl}/sap/opu/odata/sap/API_PRODUCT_SRV/A_Product('{materialNumber}')" +
                "?$select=Product,ProductDescription,BaseUnit,MaterialGroup");
            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new {
                    Product = materialNumber, ProductDescription = "Sandbox Demo Product",
                    BaseUnit = "EA", MaterialGroup = "FERT", source = "SAP_MOCK" });
            return await resp.Content.ReadAsStringAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "SAP GetMaterial failed");
            return JsonSerializer.Serialize(new { error = ex.Message, materialNumber });
        }
    }

    [KernelFunction("sap_get_price")]
    [Description("Get current pricing conditions from SAP for a material. Returns price, currency, validity dates.")]
    public async Task<string> GetPriceAsync(
        [Description("SAP material number")] string materialNumber,
        [Description("Sales org e.g. GB01")] string salesOrg = "GB01")
    {
        _logger.LogInformation("SAP GetPrice: {Mat} | {Org}", materialNumber, salesOrg);
        try {
            var filter = Uri.EscapeDataString(
                "Material eq '" + materialNumber + "' and SalesOrganization eq '" + salesOrg + "'");
            var resp = await _http.GetAsync(
                $"{_config.BaseUrl}/sap/opu/odata/sap/API_SLSPRICINGCONDITIONRECORD_SRV/" +
                $"A_SlsPrcgCndnRecdValidity?$filter={filter}" +
                "&$select=Material,SalesOrganization,ConditionRateValue,ConditionRateValueUnit," +
                "ValidityStartDate,ValidityEndDate&$orderby=ValidityStartDate desc&$top=1");
            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new {
                    Material = materialNumber, ConditionRateValue = 249.99m,
                    ConditionRateValueUnit = "GBP", SalesOrganization = salesOrg,
                    ValidityStartDate = "2024-01-01", source = "SAP_MOCK" });
            return await resp.Content.ReadAsStringAsync();
        } catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                Material = materialNumber, ConditionRateValue = 249.99m,
                ConditionRateValueUnit = "GBP", note = "SAP_EXCEPTION_MOCK" });
        }
    }

    [KernelFunction("sap_get_inventory")]
    [Description("Get real-time stock levels for a material from SAP across all storage locations.")]
    public async Task<string> GetInventoryAsync(
        [Description("SAP material number")] string materialNumber,
        [Description("Plant code e.g. PL01")] string plant = "PL01")
    {
        try {
            var filter = Uri.EscapeDataString("Material eq '" + materialNumber + "' and Plant eq '" + plant + "'");
            var resp = await _http.GetAsync(
                $"{_config.BaseUrl}/sap/opu/odata/sap/API_MATERIAL_STOCK_SRV/A_MatlStkInAcctMod?" +
                $"$filter={filter}&$select=Material,Plant,StorageLocation,MatlWrhsStkQtyInMatBaseUnit,BaseUnit");
            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { Material = materialNumber, Plant = plant,
                    MatlWrhsStkQtyInMatBaseUnit = 150.0m, BaseUnit = "EA", source = "SAP_MOCK" });
            return await resp.Content.ReadAsStringAsync();
        } catch (Exception ex) {
            return JsonSerializer.Serialize(new { error = ex.Message, materialNumber });
        }
    }

    [KernelFunction("sap_compare_prices")]
    [Description("Compare SAP price with Salesforce price. Returns discrepancy analysis and recommended action.")]
    public Task<string> ComparePricesAsync(
        [Description("SAP material number")] string materialNumber,
        [Description("SAP price")] decimal sapPrice,
        [Description("Salesforce current price")] decimal salesforcePrice,
        [Description("Currency code e.g. GBP")] string currency = "GBP")
    {
        var diff = sapPrice - salesforcePrice;
        var pct = salesforcePrice > 0 ? Math.Abs(diff / salesforcePrice * 100) : 0;
        return Task.FromResult(JsonSerializer.Serialize(new {
            materialNumber, sapPrice, salesforcePrice, currency,
            discrepancy = diff, discrepancyPercent = Math.Round(pct, 2),
            syncRequired = pct > 0.01m,
            approvalLevel = pct < 1 ? "auto" : pct < 10 ? "finance_manager" : "vp_sales",
            recommendation = pct < 1 ? "AUTO_SYNC" : pct < 10 ? "SYNC_WITH_APPROVAL" : "ESCALATE_VP"
        }));
    }
}
