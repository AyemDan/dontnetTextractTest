using System.Text.Json.Serialization;

namespace TextractTest.Models;

public class BankStatementData
{
    [JsonPropertyName("summary")]
    public Dictionary<string, string> Summary { get; set; } = new();
    
    [JsonPropertyName("transactions")]
    public List<Transaction> Transactions { get; set; } = new();
}

public class Transaction
{
    [JsonPropertyName("Date")]
    public string Date { get; set; } = string.Empty;
    
    [JsonPropertyName("Reference")]
    public string Reference { get; set; } = string.Empty;
    
    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("Value Date")]
    public string ValueDate { get; set; } = string.Empty;
    
    [JsonPropertyName("Deposit")]
    public string Deposit { get; set; } = string.Empty;
    
    [JsonPropertyName("Withdrawal")]
    public string Withdrawal { get; set; } = string.Empty;
    
    [JsonPropertyName("Balance")]
    public string Balance { get; set; } = string.Empty;
}

public class Table
{
    public List<List<string>> Rows { get; set; } = new();
    public int Page { get; set; }
} 