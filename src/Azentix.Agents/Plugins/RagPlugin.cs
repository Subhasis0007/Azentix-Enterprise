using Microsoft.SemanticKernel;
using System.ComponentModel;
using Azentix.Agents.Rag;

namespace Azentix.Agents.Plugins;

public class RagPlugin
{
    private readonly IRagAgent _rag;

    public RagPlugin(IRagAgent rag) { _rag = rag; }

    [KernelFunction("rag_search")]
    [Description("Search the Supabase pgvector knowledge base for relevant documents. " +
                 "Use for: SAP-Salesforce sync rules, ServiceNow KB articles, approval thresholds, governance policies.")]
    public Task<string> SearchAsync(
        [Description("Natural language search query")] string query,
        [Description("Collection to search: sap-salesforce-sync, servicenow-kb, stripe-policies, default")] string collection = "default",
        [Description("Number of results to return")] int topK = 5)
        => _rag.SearchAsync(query, collection, topK);
}
