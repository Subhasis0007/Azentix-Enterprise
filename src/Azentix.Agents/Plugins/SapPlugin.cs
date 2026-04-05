using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class SapPlugin
{
    private readonly HttpClient _http;
    private readonly SapConfiguration _cfg;
    private readonly ILogger<SapPlugin> _log;

    public SapPlugin(HttpClient http, SapConfiguration cfg, ILogger<SapPlugin> log)
    {
        _http = http; _cfg = cfg; _log = log;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("APIKey", cfg.ApiKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    [KernelFunction("sap_get_material")]
    [Description("Get SAP material master data. Returns description, unit, material group.")]
    public async Task<string> GetMaterialAsync(
        [Description("SAP material number e.g. MAT-001234")] string materialNumber)
    {
        try
        {
            var resp = await _http.GetAsync(
                $"{_cfg.BaseUrl}/sap/opu/odata/sap/API_PRODUCT_SRV/A_Product('{materialNumber}')" +
                "?$select=Product,ProductDescription,BaseUnit,MaterialGroup");
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { _log.LogWarning("SAP material fallback: {M}", ex.Message); }
        return JsonSerializer.Serialize(new {
            Product = materialNumber, ProductDescription = "SAP Sandbox Product",
            BaseUnit = "EA", MaterialGroup = "FERT", _source = "mock" });
    }

    [KernelFunction("sap_get_price")]
    [Description("Get SAP pricing conditions. Returns price, currency, validity dates.")]
    public async Task<string> GetPriceAsync(
        [Description("SAP material number")] string materialNumber,
        [Description("Sales organisation e.g. GB01")] string salesOrg = "GB01")
    {
        try
        {
            var filter = Uri.EscapeDataString(
                $"Material eq '{materialNumber}' and SalesOrganization eq '{salesOrg}'");
            var resp = await _http.GetAsync(
                $"{_cfg.BaseUrl}/sap/opu/odata/sap/API_SLSPRICINGCONDITIONRECORD_SRV/" +
                $"A_SlsPrcgCndnRecdValidity?$filter={filter}" +
                "&$select=Material,SalesOrganization,ConditionRateValue,ConditionRateValueUnit," +
                "ValidityStartDate,ValidityEndDate&$orderby=ValidityStartDate desc&$top=1");
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { _log.LogWarning("SAP price fallback: {M}", ex.Message); }
        return JsonSerializer.Serialize(new {
            Material = materialNumber, ConditionRateValue = 249.99m,
            ConditionRateValueUnit = "GBP", SalesOrganization = salesOrg,
            ValidityStartDate = "2024-01-01", _source = "mock" });
    }

    [KernelFunction("sap_get_inventory")]
    [Description("Get real-time SAP stock levels for a material.")]
    public async Task<string> GetInventoryAsync(
        [Description("SAP material number")] string materialNumber,
        [Description("Plant code e.g. PL01")] string plant = "PL01")
    {
        try
        {
            var filter = Uri.EscapeDataString(
                $"Material eq '{materialNumber}' and Plant eq '{plant}'");
            var resp = await _http.GetAsync(
                $"{_cfg.BaseUrl}/sap/opu/odata/sap/API_MATERIAL_STOCK_SRV/A_MatlStkInAcctMod" +
                $"?$filter={filter}&$select=Material,Plant,MatlWrhsStkQtyInMatBaseUnit,BaseUnit");
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { _log.LogWarning("SAP inventory fallback: {M}", ex.Message); }
        return JsonSerializer.Serialize(new {
            Material = materialNumber, Plant = plant,
            MatlWrhsStkQtyInMatBaseUnit = 150.0m, BaseUnit = "EA", _source = "mock" });
    }

    [KernelFunction("sap_compare_prices")]
    [Description("Compare SAP price vs Salesforce price. Returns discrepancy analysis and recommended action.")]
    public Task<string> ComparePricesAsync(
        [Description("SAP material number")] string materialNumber,
        [Description("SAP price as decimal")] decimal sapPrice,
        [Description("Salesforce current price as decimal")] decimal sfPrice,
        [Description("Currency code e.g. GBP")] string currency = "GBP")
    {
        var diff = sapPrice - sfPrice;
        var pct  = sfPrice > 0 ? Math.Abs(diff / sfPrice * 100m) : 0m;
        return Task.FromResult(JsonSerializer.Serialize(new {
            materialNumber, sapPrice, sfPrice, currency,
            discrepancy = Math.Round(diff, 2),
            discrepancyPercent = Math.Round(pct, 2),
            syncRequired = pct > 0.01m,
            approvalLevel = pct < 1m ? "auto"
                          : pct < 10m ? "finance_manager"
                          : "vp_sales",
            recommendation = pct < 1m ? "AUTO_SYNC"
                           : pct < 10m ? "SYNC_WITH_APPROVAL"
                           : "ESCALATE_VP"
        }));
    }
}
