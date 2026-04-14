
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

    public SapPlugin(
        IHttpClientFactory httpFactory,
        SapConfiguration cfg,
        ILogger<SapPlugin> log)
    {
        _cfg = cfg;
        _log = log;

        _http = httpFactory.CreateClient("SAP");
        _http.BaseAddress = new Uri(cfg.BaseUrl);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("APIKey", cfg.ApiKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    [KernelFunction("sap_get_material")]
    [Description("Get SAP material master data.")]
    public async Task<string> GetMaterialAsync(string materialNumber)
    {
        try
        {
            var resp = await _http.GetAsync(
                $"/sap/opu/odata/sap/API_PRODUCT_SRV/A_Product('{materialNumber}')");

            if (resp.IsSuccessStatusCode)
                return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning("SAP material fallback: {Message}", ex.Message);
        }

        return JsonSerializer.Serialize(new
        {
            Product = materialNumber,
            Description = "SAP Sandbox Product",
            BaseUnit = "EA",
            _source = "mock"
        });
    }
}
