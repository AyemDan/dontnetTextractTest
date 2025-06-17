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

        foreach (var table in tables)
        {
            if (table.Rows.Count == 0) continue;

            var headers = table.Rows[0].Select(h => h.Trim()).ToList();
            Console.WriteLine($"\nDebug - Processing table with headers: {string.Join(", ", headers)}");

            // Check if this is a summary/account info table
            if (headers.Any(cell =>
                cell.Contains("account", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("currency", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("balance:", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("period", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("statement", StringComparison.OrdinalIgnoreCase) ||
                cell.Contains("branch", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Found summary table");
            // Try standard row-based key-value
            if (table.Rows.All(r => r.Count == 2))
            {
                foreach (var row in table.Rows)
                {
                    var key = row[0].Trim().TrimEnd(':');
                    var value = row[1].Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
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
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        result.Summary[key] = value;
                    }
                }
            }

                continue;
            }

            // Try to identify if this is a transaction table
            var headerMap = MapHeaders(headers);
            Console.WriteLine($"Found {headerMap.Count} matching headers");

            if (headerMap.Count >= 4)
            {
                Console.WriteLine("Processing as transaction table");

                var type = typeof(Transaction);

                foreach (var row in table.Rows.Skip(1)) // Skip header row
                {
                    if (row.Count != headers.Count) continue;

                    var transaction = new Transaction();

                    foreach (var standardHeader in HeaderMappings.Keys)
                    {
                        if (headerMap.TryGetValue(standardHeader, out int idx))
                        {
                            var value = idx < row.Count ? row[idx].Trim() : "";

                            var prop = type.GetProperties()
                                           .FirstOrDefault(p => string.Equals(p.Name, standardHeader, StringComparison.OrdinalIgnoreCase));

                            if (prop != null && prop.CanWrite)
                            {
                                prop.SetValue(transaction, value);
                            }
                        }
                        else
                        {
                            var prop = type.GetProperties()
                                           .FirstOrDefault(p => string.Equals(p.Name, standardHeader, StringComparison.OrdinalIgnoreCase));

                            if (prop != null && prop.CanWrite)
                            {
                                prop.SetValue(transaction, "");
                            }
                        }
                    }

                    // Add only if there's at least one non-empty field
                    if (type.GetProperties().Any(p =>
                    {
                        var value = p.GetValue(transaction) as string;
                        return !string.IsNullOrWhiteSpace(value);
                    }))
                    {
                        result.Transactions.Add(transaction);
                    }
                }
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
