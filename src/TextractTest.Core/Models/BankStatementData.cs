using System.Text.Json.Serialization;

namespace TextractTest.Core.Models;

public class BankStatementData
{
    [JsonPropertyName("summary")]
    public Dictionary<string, string> Summary { get; set; } = new();

    [JsonPropertyName("transactions")]
    public List<Dictionary<string, string>> Transactions { get; set; } = new();
} 