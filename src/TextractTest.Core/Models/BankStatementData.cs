using System.Text.Json.Serialization;

namespace TextractTest.Core.Models;

public class BankStatementData
{
    [JsonPropertyName("summary")]
    public Dictionary<string, string> Summary { get; set; } = new();

    [JsonPropertyName("transactions")]
    public List<Transaction> Transactions { get; set; } = new();
}

public class AccountInfo
{
    [JsonPropertyName("accountNumber")]
    public string AccountNumber { get; set; } = string.Empty;

    [JsonPropertyName("accountName")]
    public string AccountName { get; set; } = string.Empty;

    [JsonPropertyName("bankName")]
    public string BankName { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("branch")]
    public string Branch { get; set; } = string.Empty;
}

public class StatementSummary
{
    [JsonPropertyName("statementPeriod")]
    public string StatementPeriod { get; set; } = string.Empty;

    [JsonPropertyName("openingBalance")]
    public decimal OpeningBalance { get; set; }

    [JsonPropertyName("closingBalance")]
    public decimal ClosingBalance { get; set; }

    [JsonPropertyName("totalDeposits")]
    public decimal TotalDeposits { get; set; }

    [JsonPropertyName("totalWithdrawals")]
    public decimal TotalWithdrawals { get; set; }

    [JsonPropertyName("totalTransactions")]
    public int TotalTransactions { get; set; }
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

    [JsonPropertyName("Credit")]
    public string Credit { get; set; } = string.Empty;

    [JsonPropertyName("Debit")]
    public string Debit { get; set; } = string.Empty;

    [JsonPropertyName("Balance")]
    public string Balance { get; set; } = string.Empty;
}

public class Table
{
    public List<List<string>> Rows { get; set; } = new();
    public int Page { get; set; }
}