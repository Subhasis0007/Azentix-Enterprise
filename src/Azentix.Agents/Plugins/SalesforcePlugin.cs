using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Plugins;

public class SalesforcePlugin
{
    private readonly HttpClient _http;
    private readonly SalesforceConfiguration _cfg;
    private readonly ILogger<SalesforcePlugin> _log;
    private string? _token;
    private string? _instanceUrl;

    public SalesforcePlugin(HttpClient http, SalesforceConfiguration cfg,
        ILogger<SalesforcePlugin> log)
    { _http = http; _cfg = cfg; _log = log; }

    [KernelFunction("salesforce_get_product")]
    [Description("Get a Salesforce Product2 by name or ID.")]
    public async Task<string> GetProductAsync(
        [Description("Product name or Salesforce ID")] string identifier,
        [Description("true if a Salesforce record ID")] bool isId = false)
    {
        _log.LogInformation("Salesforce GetProduct start | Identifier={Identifier} | IsId={IsId}", identifier, isId);
        await EnsureAuthAsync();
        var url = isId
            ? $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Product2/{identifier}?fields=Id,Name,ProductCode,IsActive"
            : $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString($"SELECT Id,Name,ProductCode,IsActive FROM Product2 WHERE Name LIKE '%{identifier}%' LIMIT 5")}";

        var resp = await _http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();
        _log.LogInformation("Salesforce GetProduct response | Status={Status} | Identifier={Identifier} | BodySnippet={BodySnippet}",
            (int)resp.StatusCode,
            identifier,
            TruncateForLog(body));

        if (resp.IsSuccessStatusCode)
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                resource = "Product2",
                queryType = isId ? "by_id" : "by_name",
                identifier,
                statusCode = (int)resp.StatusCode,
                data = ParseJsonOrRaw(body)
            });
        }

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                errorCode = "product_not_found",
                resource = "Product2",
                queryType = isId ? "by_id" : "by_name",
                identifier,
                statusCode = (int)resp.StatusCode,
                details = ParseJsonOrRaw(body)
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = false,
            errorCode = "salesforce_api_error",
            resource = "Product2",
            queryType = isId ? "by_id" : "by_name",
            identifier,
            statusCode = (int)resp.StatusCode,
            details = ParseJsonOrRaw(body)
        });
    }

    [KernelFunction("salesforce_get_pricebook")]
    [Description("Get the Standard Pricebook entry for a product. Returns UnitPrice and currency.")]
    public async Task<string> GetPricebookAsync(
        [Description("Salesforce Product2 ID")] string productId)
    {
        _log.LogInformation("Salesforce GetPricebook start | ProductId={ProductId}", productId);
        await EnsureAuthAsync();
        var q = $"SELECT Id,UnitPrice,CurrencyIsoCode,IsActive FROM PricebookEntry WHERE Product2Id='{productId}' AND Pricebook2.IsStandard=true AND IsActive=true LIMIT 1";
        var resp = await _http.GetAsync($"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString(q)}");
        var body = await resp.Content.ReadAsStringAsync();
        _log.LogInformation("Salesforce GetPricebook response | Status={Status} | ProductId={ProductId} | BodySnippet={BodySnippet}",
            (int)resp.StatusCode,
            productId,
            TruncateForLog(body));

        if (!resp.IsSuccessStatusCode)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                errorCode = "salesforce_api_error",
                resource = "PricebookEntry",
                productId,
                statusCode = (int)resp.StatusCode,
                details = ParseJsonOrRaw(body)
            });
        }

        JsonElement payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                errorCode = "invalid_salesforce_response",
                resource = "PricebookEntry",
                productId,
                statusCode = (int)resp.StatusCode,
                details = body
            });
        }

        var totalSize = payload.TryGetProperty("totalSize", out var totalSizeElement)
            ? totalSizeElement.GetInt32()
            : 0;

        _log.LogInformation("Salesforce GetPricebook parsed | ProductId={ProductId} | TotalSize={TotalSize}",
            productId,
            totalSize);

        if (totalSize == 0)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                errorCode = "pricebook_entry_not_found",
                resource = "PricebookEntry",
                productId,
                statusCode = (int)resp.StatusCode,
                message = "No active Standard PricebookEntry found for the provided Product2 ID.",
                data = payload
            });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            resource = "PricebookEntry",
            productId,
            statusCode = (int)resp.StatusCode,
            data = payload
        });
    }

    [KernelFunction("salesforce_update_price")]
    [Description("Update a PricebookEntry UnitPrice. Used for SAP→Salesforce sync.")]
    public async Task<string> UpdatePriceAsync(
        [Description("PricebookEntry ID")] string pricebookEntryId,
        [Description("New unit price")] decimal newPrice,
        [Description("Currency ISO code")] string currency = "GBP",
        [Description("SAP material number for audit")] string? sapMaterial = null)
    {
        _log.LogInformation("Salesforce UpdatePrice start | PricebookEntryId={PricebookEntryId} | NewPrice={NewPrice} | Currency={Currency} | SapMaterial={SapMaterial}",
            pricebookEntryId,
            newPrice,
            currency,
            sapMaterial ?? "");
        await EnsureAuthAsync();
        var body = JsonSerializer.Serialize(new {
            UnitPrice = newPrice,
            Azentix_Last_Synced__c = DateTime.UtcNow.ToString("o"),
            Azentix_SAP_Material__c = sapMaterial ?? ""
        });
        var req = new HttpRequestMessage(HttpMethod.Patch,
            $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/PricebookEntry/{pricebookEntryId}");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        var responseBody = await resp.Content.ReadAsStringAsync();
        _log.LogInformation("Salesforce UpdatePrice response | Status={Status} | PricebookEntryId={PricebookEntryId} | BodySnippet={BodySnippet}",
            (int)resp.StatusCode,
            pricebookEntryId,
            TruncateForLog(responseBody));
        return JsonSerializer.Serialize(new {
            success = resp.IsSuccessStatusCode,
            pricebookEntryId, newPrice, currency, sapMaterial,
            statusCode = (int)resp.StatusCode,
            details = ParseJsonOrRaw(responseBody),
            syncedAt = DateTime.UtcNow });
    }

    [KernelFunction("salesforce_get_lead")]
    [Description("Get a Salesforce Lead by ID or email.")]
    public async Task<string> GetLeadAsync(
        [Description("Lead ID or email address")] string identifier,
        [Description("true if email address")] bool isEmail = false)
    {
        await EnsureAuthAsync();
        var url = isEmail
            ? $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString($"SELECT Id,FirstName,LastName,Email,Company,Status FROM Lead WHERE Email='{identifier}' LIMIT 1")}"
            : $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Lead/{identifier}?fields=Id,FirstName,LastName,Email,Company,Status";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    [KernelFunction("salesforce_update_lead")]
    [Description("Update a Salesforce Lead with enriched data.")]
    public async Task<string> UpdateLeadAsync(
        [Description("Lead ID")] string leadId,
        [Description("JSON object with fields to update")] string fieldsJson)
    {
        await EnsureAuthAsync();
        var req = new HttpRequestMessage(HttpMethod.Patch,
            $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Lead/{leadId}");
        req.Content = new StringContent(fieldsJson, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return JsonSerializer.Serialize(new {
            success = resp.IsSuccessStatusCode, leadId, updatedAt = DateTime.UtcNow });
    }

    [KernelFunction("salesforce_get_opportunity")]
    [Description("Get a Salesforce Opportunity by ID or account name.")]
    public async Task<string> GetOpportunityAsync(
        [Description("Opportunity ID or account name")] string identifier,
        [Description("true to search by account name")] bool byAccount = false)
    {
        await EnsureAuthAsync();
        var url = byAccount
            ? $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/query?q={Uri.EscapeDataString($"SELECT Id,Name,StageName,Amount,CloseDate FROM Opportunity WHERE Account.Name LIKE '%{identifier}%' LIMIT 5")}"
            : $"{_instanceUrl}/services/data/{_cfg.ApiVersion}/sobjects/Opportunity/{identifier}?fields=Id,Name,StageName,Amount,CloseDate";
        var resp = await _http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task EnsureAuthAsync()
    {
        if (_token is not null) return;

        var authBaseUrl = ResolveAuthBaseUrl();
        _log.LogInformation("Salesforce auth start | AuthBaseUrl={AuthBaseUrl} | InstanceUrl={InstanceUrl} | Username={Username}",
            authBaseUrl,
            _cfg.InstanceUrl,
            _cfg.Username);
        var resp = await _http.PostAsync($"{authBaseUrl}/services/oauth2/token",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"]    = "password",
                ["client_id"]     = _cfg.ClientId,
                ["client_secret"] = _cfg.ClientSecret,
                ["username"]      = _cfg.Username,
                ["password"]      = _cfg.Password
            }));
        var body = await resp.Content.ReadAsStringAsync();
        _log.LogInformation("Salesforce auth response | Status={Status} | BodySnippet={BodySnippet}",
            (int)resp.StatusCode,
            TruncateForLog(body));

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Salesforce authentication failed with status {(int)resp.StatusCode}. Response: {body}");
        }

        JsonElement json;
        try
        {
            json = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Salesforce authentication returned a non-JSON response. Body: {body}", ex);
        }

        if (!json.TryGetProperty("access_token", out var tokenElement))
        {
            throw new InvalidOperationException(
                $"Salesforce authentication response does not contain access_token. Response: {body}");
        }

        _token = tokenElement.GetString();
        _instanceUrl = json.TryGetProperty("instance_url", out var iu)
            ? iu.GetString() : _cfg.InstanceUrl;

        if (string.IsNullOrWhiteSpace(_token) || string.IsNullOrWhiteSpace(_instanceUrl))
        {
            throw new InvalidOperationException(
                "Salesforce authentication returned an empty token or instance URL.");
        }

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        _log.LogInformation("Salesforce auth complete | InstanceUrl={InstanceUrl}", _instanceUrl);
    }

    private string ResolveAuthBaseUrl()
    {
        if (!Uri.TryCreate(_cfg.InstanceUrl, UriKind.Absolute, out var uri))
            return "https://login.salesforce.com";

        var host = uri.Host;
        if (host.Contains("sandbox", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("cs", StringComparison.OrdinalIgnoreCase))
            return "https://test.salesforce.com";

        return "https://login.salesforce.com";
    }

    private static object ParseJsonOrRaw(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(value);
        }
        catch
        {
            return value;
        }
    }

    private static string TruncateForLog(string? text, int max = 400)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= max
            ? normalized
            : normalized[..max] + "...";
    }
}
