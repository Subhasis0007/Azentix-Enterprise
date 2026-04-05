using Microsoft.SemanticKernel;
using System.ComponentModel;
using Azentix.Agents.Rag;

namespace Azentix.Agents.Plugins;

public class RagPlugin
{
    private readonly IRagAgent _rag;
    public RagPlugin(IRagAgent rag) { _rag = rag; }

    [KernelFunction("rag_search")]
    [Description("Search the Supabase pgvector knowledge base for relevant documents. Use for: sync governance rules, approval thresholds, KB articles, policies.")]
    public Task<string> SearchAsync(
        [Description("Natural language search query")] string query,
        [Description("Collection: sap-salesforce-sync | servicenow-kb | stripe-policies | default")] string collection = "default",
        [Description("Number of results (1-10)")] int topK = 5)
        => _rag.SearchAsync(query, collection, topK);
}
