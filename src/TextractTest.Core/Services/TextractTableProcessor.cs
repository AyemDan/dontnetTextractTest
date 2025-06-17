using Amazon.Textract;
using Amazon.Textract.Model;
using System.Text.Json;
using TextractTest.Core.Models;

namespace TextractTest.Core.Services;

public class TextractTableProcessor
{
    private List<Block> _blocks;

    private static readonly Dictionary<string, string[]> HeaderMappings = new()
    {
        ["Date"] = new[] { "Date", "Transaction Date", "Value Date", "Tran Date", "Create Date" },
        ["Reference"] = new[] { "Reference", "Reference No", "Ref No", "Transaction ID", "Trans ID", "Trans Ref" },
        ["Description"] = new[] { "Description", "Narration", "Transaction Description", "Details", "Particulars", "Description/Payee/Memo" },
        ["Value Date"] = new[] { "Value Date", "Val Date", "Settlement Date", "Create Date" },
        ["Credit"] = new[] { "Credit", "Deposit", "Credit Amount", "Amount (CR)", "Deposits", "Lodgements" },
        ["Debit"] = new[] { "Debit", "Withdrawal", "Debit Amount", "Amount (DR)", "Withdrawals" },
        ["Balance"] = new[] { "Balance", "Running Balance", "Closing Balance", "Current Balance" }
    };
    private Dictionary<string, int> MapHeaders(List<string> actualHeaders)
    {
        var headerMap = new Dictionary<string, int>();
        Console.WriteLine($"\nDebug - Found headers: {string.Join(", ", actualHeaders)}");

        foreach (var mapping in HeaderMappings)
        {
            var standardHeader = mapping.Key;
            var possibleNames = mapping.Value;
            var matchedHeader = FindMatchingHeader(actualHeaders, possibleNames);

            if (matchedHeader != null)
            {
                var index = actualHeaders.IndexOf(matchedHeader);
                headerMap[standardHeader] = index;
                Console.WriteLine($"Mapped '{matchedHeader}' to '{standardHeader}'");
            }
        }

        return headerMap;
    }
    private string? FindMatchingHeader(List<string> headers, string[] possibleNames)
    {
        foreach (var header in headers)
        {
            var trimmedHeader = header.Trim();
            if (possibleNames.Any(possible =>
                trimmedHeader.Contains(possible, StringComparison.OrdinalIgnoreCase)))
            {
                return trimmedHeader;
            }
        }
        return null;
    }

    public TextractTableProcessor()
    {
        _blocks = new List<Block>();
    }

    public async Task ExtractFromJobId(IAmazonTextract client, string jobId)
    {
        var request = new GetDocumentAnalysisRequest { JobId = jobId };
        var response = await client.GetDocumentAnalysisAsync(request);

        Console.WriteLine($"Pages processed: {response.DocumentMetadata.Pages}");

        _blocks.Clear();
        _blocks.AddRange(response.Blocks);

        while (response.NextToken != null)
        {
            Console.WriteLine($"Processing next batch with {response.Blocks.Count} blocks");
            request.NextToken = response.NextToken;
            response = await client.GetDocumentAnalysisAsync(request);
            _blocks.AddRange(response.Blocks);
        }
    }

    public List<TableData> ExtractTablesFromBlocks()
    {
        var tables = new List<TableData>();
        TableData? currentTable = null;

        foreach (var block in _blocks)
        {
            if (block.BlockType == BlockType.TABLE)
            {
                // Add the previous table (if any) before starting a new one
                if (currentTable != null)
                {
                    tables.Add(currentTable);
                }

                currentTable = new TableData
                {
                    Page = block.Page,
                    Rows = new List<List<string>>()
                };

                Console.WriteLine($"Found table on page {block.Page}");
            }
            else if (block.BlockType == BlockType.CELL && currentTable != null)
            {
                // Get cell text
                var text = GetCellText(block);

                var rowIndex = block.RowIndex - 1;
                var colIndex = block.ColumnIndex - 1;

                // Ensure row exists
                while (currentTable.Rows.Count <= rowIndex)
                {
                    currentTable.Rows.Add(new List<string>());
                }

                // Ensure column exists
                while (currentTable.Rows[rowIndex].Count <= colIndex)
                {
                    currentTable.Rows[rowIndex].Add(string.Empty);
                }

                // Set cell value
                currentTable.Rows[rowIndex][colIndex] = text.Trim();
            }
        }

        // ✅ Add the last table after loop ends
        if (currentTable != null)
        {
            tables.Add(currentTable);
        }

        Console.WriteLine($"Extracted {tables.Count} tables from {_blocks.Count(b => b.BlockType == BlockType.TABLE)} TABLE blocks");

        // Optional: Show how many tables were found per page
        var tablesByPage = tables.GroupBy(t => t.Page)
                                 .ToDictionary(g => g.Key, g => g.Count());

        foreach (var page in tablesByPage.OrderBy(p => p.Key))
        {
            Console.WriteLine($"Page {page.Key}: {page.Value} tables");
        }

        return tables;
    }

    private string GetCellText(Block block)
    {
        var text = new List<string>();
        if (block.Relationships != null)
        {
            foreach (var relationship in block.Relationships)
            {
                if (relationship.Type == "CHILD")
                {
                    foreach (var childId in relationship.Ids)
                    {
                        var childBlock = _blocks.FirstOrDefault(b => b.Id == childId);
                        if (childBlock?.BlockType == BlockType.WORD)
                        {
                            text.Add(childBlock.Text);
                        }
                    }
                }
            }
        }
        return string.Join(" ", text);
    }


    public BankStatementData FormatBankStatementData(List<TableData> tables)
    {
        var result = new BankStatementData();
        int tableIndex = 0;
        // Define allowed summary keys
        var allowedSummaryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Account Name", "Account No", "Account Number", "Acct No", "Customer Name",
            "Currency", "Account Type", "For the Period of", "Period", "Address",
            "Cleared Balance", "Available Balance", "UnCleared Balance", "Total Credit", "Total Debit",
            "Branch", "Statement Period", "Opening Balance", "Closing Balance", "Bank Name",
            // User requested additional/variant keys:
            "Begin Balance", "Uncleared Effect", "Summary Statement for", "Begin Balance Date", "Title",
            "NUBAN", "BVN", "Remark", "Balance BF"
        };
        // Prepare a set of summary row indicators (with and without colon)
        var summaryRowIndicators = new HashSet<string>(allowedSummaryKeys.SelectMany(k => new[] { k, k + ":" }), StringComparer.OrdinalIgnoreCase);
        // Flexible header mappings for transaction fields
        var headerMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Description", new[] { "Description", "Narration", "Details", "Transaction Details" } },
            { "Date", new[] { "Date", "Transaction Date", "Value Date" } },
            { "Reference", new[] { "Reference", "Ref", "Ref No", "Reference No" } },
            { "Credit", new[] { "Credit", "Deposit", "Cr" } },
            { "Debit", new[] { "Debit", "Withdrawal", "Dr" } },
            { "Balance", new[] { "Balance", "Running Balance" } },
            // Add more as needed
        };
        foreach (var table in tables)
        {
            tableIndex++;
            Console.WriteLine($"Table {tableIndex}: {table.Rows.Count} rows");
            if (table.Rows.Count == 0) continue;

            var headers = table.Rows[0].Select(h => h.Trim()).ToList();
            Console.WriteLine($"\nDebug - Processing table with headers: {string.Join(", ", headers)}");

            // Check if this is a summary/account info table
            bool isSummaryTable = headers.Any(cell =>
                cell.Contains("account", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("currency", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("balance:", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("period", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("statement", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("branch", StringComparison.OrdinalIgnoreCase));

            if (isSummaryTable)
            {
                Console.WriteLine("Found summary table");
                // Try standard row-based key-value
                if (table.Rows.All(r => r.Count == 2))
                {
                    foreach (var row in table.Rows)
                    {
                        var key = row[0].Trim().TrimEnd(':');
                        var value = row[1].Trim();
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && allowedSummaryKeys.Contains(key))
                        {
                            result.Summary[key] = value;
                        }
                    }
                }
                // Try 2-row key-over-value format (e.g., headers in first row, values in second)
                else if (table.Rows.Count == 2)
                {
                    var keys = table.Rows[0];
                    var values = table.Rows[1];
                    for (int i = 0; i < Math.Min(keys.Count, values.Count); i++)
                    {
                        var key = keys[i].Trim().TrimEnd(':');
                        var value = values[i].Trim();
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && allowedSummaryKeys.Contains(key))
                        {
                            result.Summary[key] = value;
                        }
                    }
                }
                // If not a standard summary table, skip adding all cells as summary fields
                continue;
            }

            // Try to identify if this is a transaction table
            var headerMap = MapHeaders(headers);
            Console.WriteLine($"Found {headerMap.Count} matching headers");
            int transactionsBefore = result.Transactions.Count;
            bool isTransactionTable = headerMap.Count >= 4;

            if (isTransactionTable)
            {
                Console.WriteLine("Processing as transaction table");
                var type = typeof(Transaction);
                var transactionRows = table.Rows.Skip(1).ToList(); // Skip header row

                foreach (var row in transactionRows)
                {
                    if (row.Count != headers.Count) continue;
                    if (summaryRowIndicators.Contains(row[0].Trim())) continue;
                    var transaction = new Transaction();
                    for (int i = 0; i < headers.Count; i++)
                    {
                        var header = headers[i].Trim();
                        // Find the property this header should map to
                        string propertyName = null;
                        foreach (var kvp in headerMappings)
                        {
                            if (kvp.Value.Any(variant => header.Equals(variant, StringComparison.OrdinalIgnoreCase)))
                            {
                                propertyName = kvp.Key;
                                break;
                            }
                        }
                        if (propertyName == null)
                        {
                            // Fallback: use header as property name
                            propertyName = header.Replace(" ", "");
                        }
                        var prop = type.GetProperty(propertyName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (prop != null)
                        {
                            prop.SetValue(transaction, row[i]);
                        }
                    }
                    result.Transactions.Add(transaction);
                }
            }
            else
            {
                // If not a summary or transaction table, treat as generic transaction table
                Console.WriteLine($"Treating table {tableIndex} as generic transaction table");
                var colCount = headers.Count;
                foreach (var row in table.Rows.Skip(1)) // Skip header row
                {
                    if (row.Count != colCount) continue;
                    // Skip rows that are summary-like (first cell matches summary key)
                    var firstCell = row[0].Trim();
                    if (summaryRowIndicators.Contains(firstCell)) continue;
                    var transaction = new Transaction();
                    // Map columns to known fields if possible
                    for (int i = 0; i < row.Count; i++)
                    {
                        var value = row[i].Trim();
                        if (i == 0) transaction.Date = value;
                        else if (i == 1) transaction.Reference = value;
                        else if (i == 2) transaction.Description = value;
                        else if (i == 3) transaction.ValueDate = value;
                        else if (i == 4) transaction.Credit = value;
                        else if (i == 5) transaction.Debit = value;
                        else if (i == 6) transaction.Balance = value;
                    }
                    // Add only if there's at least one non-empty field
                    if (typeof(Transaction).GetProperties().Any(p =>
                    {
                        var value = p.GetValue(transaction) as string;
                        return !string.IsNullOrWhiteSpace(value);
                    }))
                    {
                        result.Transactions.Add(transaction);
                    }
                }
                int transactionsAfter = result.Transactions.Count;
                Console.WriteLine($"Extracted {transactionsAfter - transactionsBefore} generic transactions from table {tableIndex}");
            }
        }
        return result;
    }

    public void SaveResults(BankStatementData data, string outputFile)
    {
        // Get the directory and filename parts
        var dir = Path.GetDirectoryName(outputFile);
        var originalFilename = Path.GetFileNameWithoutExtension(outputFile);

        // Extract the job ID from the filename (assuming format: extraction_jobId-timestamp.json)
        var parts = originalFilename.Split('_');
        var jobId = parts.Length > 1 ? parts[1].Split('-')[0] : "unknown";

        // Get the original file name from the bucket (it should be the first part before _)
        var bucketFileName = parts[0];

        // Create new filename with format: bucketFileName_jobId-timestamp.json
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var newFilename = $"{bucketFileName}_{jobId}-{timestamp}.json";

        // Combine with Documents folder path
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var outputDir = Path.Combine(documentsPath, "TextractOutput");
        Directory.CreateDirectory(outputDir);

        var newOutputFile = Path.Combine(outputDir, newFilename);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(newOutputFile, json);

        Console.WriteLine($"Results saved to {newOutputFile}");
        Console.WriteLine($"Found {data.Summary.Count} summary fields and {data.Transactions.Count} transactions");

        // Print found headers from first transaction if available
        if (data.Transactions.Any())
        {
            Console.WriteLine("\nFields found in transactions:");
            var firstTransaction = data.Transactions[0];
            foreach (var property in firstTransaction.GetType().GetProperties())
            {
                var value = property.GetValue(firstTransaction) as string;
                if (!string.IsNullOrEmpty(value))
                {
                    Console.WriteLine($"- {property.Name}: {value}");
                }
            }
        }
    }
}
