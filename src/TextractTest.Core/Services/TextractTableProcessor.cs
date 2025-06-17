using Amazon.Textract;
using Amazon.Textract.Model;
using System.Text.Json;
using TextractTest.Core.Models;

namespace TextractTest.Core.Services;

public class TextractTableProcessor
{
    private List<Block> _blocks;
    private List<(string Key, string Value)> _keyValuePairs = new();

    private static readonly Dictionary<string, string[]> headerMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Date"] = new[] { "Date", "Transaction Date", "Create Date", "Date Posted" },
        ["ValueDate"] = new[] { "Value Date", "Val Date", "Settlement Date", "Effective Date" },
        ["Reference"] = new[] { "Reference", "Reference No", "Ref No", "Transaction ID", "Trans ID", "Trans Ref" },
        ["Description"] = new[] { "Description", "Narration", "Transaction Description", "Details", "Particulars", "Description/Payee/Memo", "Payee", "Memo", "Transaction Details" },
        ["Credit"] = new[] { "Credit", "Deposit", "Credit Amount", "Amount (CR)", "Deposits", "Lodgements" },
        ["Debit"] = new[] { "Debit", "Withdrawal", "Debit Amount", "Amount (DR)", "Withdrawals" },
        ["Balance"] = new[] { "Balance", "Running Balance" }
    };

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var c in key)
        {
            if (!char.IsPunctuation(c) || c == '-') sb.Append(c); // keep hyphens
        }
        return sb.ToString().Trim().ToLowerInvariant();
    }

    private Dictionary<string, int> MapHeaders(List<string> actualHeaders)
    {
        var headerMap = new Dictionary<string, int>();
        Console.WriteLine($"\nDebug - Found headers: {string.Join(", ", actualHeaders)}");

        foreach (var mapping in headerMappings)
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
        // Determine cache file path
        var jobCheckerDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        var cacheFile = Path.Combine(jobCheckerDir, $"{jobId}_blocks.json");

        if (File.Exists(cacheFile))
        {
            Console.WriteLine($"Loading Textract blocks from cache: {cacheFile}");
            var json = await File.ReadAllTextAsync(cacheFile);
            _blocks = JsonSerializer.Deserialize<List<Amazon.Textract.Model.Block>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            // Also extract key-value pairs from cached blocks
            _keyValuePairs = ExtractKeyValuePairs(_blocks);
            Console.WriteLine($"Loaded {_blocks.Count} blocks from cache");
            return;
        }

        var request = new GetDocumentAnalysisRequest { JobId = jobId };
        var response = await client.GetDocumentAnalysisAsync(request);
        Console.WriteLine($"Pages reported by Textract: {response.DocumentMetadata.Pages}");
        _blocks.Clear();
        _blocks.AddRange(response.Blocks);
        int pageCount = response.DocumentMetadata.Pages;
        int totalBlocks = response.Blocks.Count;
        int batch = 1;
        while (response.NextToken != null)
        {
            Console.WriteLine($"Processing next batch {batch} with {response.Blocks.Count} blocks");
            request.NextToken = response.NextToken;
            response = await client.GetDocumentAnalysisAsync(request);
            _blocks.AddRange(response.Blocks);
            batch++;
        }
        // Save blocks to cache
        var blocksJson = JsonSerializer.Serialize(_blocks);
        await File.WriteAllTextAsync(cacheFile, blocksJson);
        Console.WriteLine($"Saved Textract blocks to cache: {cacheFile}");
        // Extract key-value pairs from blocks
        _keyValuePairs = ExtractKeyValuePairs(_blocks);
        Console.WriteLine($"Extracted {_keyValuePairs.Count} key-value pairs from KEY_VALUE_SET blocks");
    }

    private List<(string Key, string Value)> ExtractKeyValuePairs(List<Block> blocks)
    {
        var keyMap = new Dictionary<string, Block>();
        var valueMap = new Dictionary<string, Block>();
        var blockMap = blocks.ToDictionary(b => b.Id, b => b);
        foreach (var block in blocks)
        {
            if (block.BlockType == "KEY_VALUE_SET")
            {
                if (block.EntityTypes.Contains("KEY"))
                    keyMap[block.Id] = block;
                else if (block.EntityTypes.Contains("VALUE"))
                    valueMap[block.Id] = block;
            }
        }
        var keyValuePairs = new List<(string Key, string Value)>();
        foreach (var keyBlock in keyMap.Values)
        {
            string keyText = GetTextForBlock(keyBlock, blockMap);
            string valueText = string.Empty;
            var valueIds = keyBlock.Relationships?.Where(r => r.Type == "VALUE").SelectMany(r => r.Ids) ?? Enumerable.Empty<string>();
            foreach (var valueId in valueIds)
            {
                if (valueMap.TryGetValue(valueId, out var valueBlock))
                {
                    valueText = GetTextForBlock(valueBlock, blockMap);
                }
            }
            if (!string.IsNullOrWhiteSpace(keyText))
                keyValuePairs.Add((keyText, valueText));
        }
        return keyValuePairs;
    }

    private string GetTextForBlock(Block block, Dictionary<string, Block> blockMap)
    {
        var text = new System.Text.StringBuilder();
        if (block.Relationships != null)
        {
            foreach (var rel in block.Relationships)
            {
                if (rel.Type == "CHILD")
                {
                    foreach (var id in rel.Ids)
                    {
                        if (blockMap.TryGetValue(id, out var childBlock))
                        {
                            if (childBlock.BlockType == "WORD" || childBlock.BlockType == "SELECTION_ELEMENT")
                            {
                                text.Append(childBlock.Text);
                                text.Append(" ");
                            }
                        }
                    }
                }
            }
        }
        return text.ToString().Trim();
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
        // Prepare a normalized set of allowed summary keys
        var normalizedSummaryKeys = new HashSet<string>(allowedSummaryKeys.Select(NormalizeKey));
        Console.WriteLine($"Normalized summary keys: {string.Join(", ", normalizedSummaryKeys)}");
        // Prepare a set of summary row indicators (with and without colon)
        var summaryRowIndicators = new HashSet<string>(allowedSummaryKeys.SelectMany(k => new[] { k, k + ":" }), StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            tableIndex++;
            Console.WriteLine($"Table {tableIndex}: {table.Rows.Count} rows");
            if (table.Rows.Count == 0) continue;

            var headers = table.Rows[0].Select(h => h.Trim()).ToList();
            Console.WriteLine($"\nDebug - Processing table with headers: {string.Join(", ", headers)}");

            // --- Universal summary key-value extraction from all tables ---
            foreach (var row in table.Rows)
            {
                if (row.Count == 2)
                {
                    var key = row[0].Trim();
                    var value = row[1].Trim();
                    var normalizedKey = NormalizeKey(key);
                    Console.WriteLine($"Checking key: '{key}' (normalized: '{normalizedKey}')");
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && normalizedSummaryKeys.Contains(normalizedKey))
                    {
                        Console.WriteLine($"  -> Matched summary key: '{key}'");
                        result.Summary[key.TrimEnd(':', '.', ';')] = value;
                    }
                    else
                    {
                        Console.WriteLine($"  -> Not a summary key");
                    }
                }
            }
            // --- End universal summary extraction ---

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

                // Build a header index map for flexible mapping
                var headerIndexMap = new Dictionary<string, int>();
                for (int i = 0; i < headers.Count; i++)
                {
                    foreach (var kvp in headerMappings)
                    {
                        if (kvp.Value.Any(h => string.Equals(headers[i], h, StringComparison.OrdinalIgnoreCase)))
                        {
                            headerIndexMap[kvp.Key] = i;
                            break;
                        }
                    }
                }

                foreach (var row in transactionRows)
                {
                    if (row.Count != headers.Count) continue;
                    // --- If any cell matches a summary key, add to summary and skip as transaction ---
                    int summaryKeyIdx = -1;
                    for (int i = 0; i < row.Count; i++)
                    {
                        var normalizedCell = NormalizeKey(row[i].Trim());
                        if (normalizedSummaryKeys.Contains(normalizedCell))
                        {
                            summaryKeyIdx = i;
                            break;
                        }
                    }
                    if (summaryKeyIdx != -1)
                    {
                        // Find the next non-empty cell after the summary key cell
                        string value = row.Skip(summaryKeyIdx + 1).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(value))
                        {
                            // Fallback: first non-empty cell before the summary key
                            value = row.Take(summaryKeyIdx).Reverse().FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
                        }
                        var key = row[summaryKeyIdx].TrimEnd(':', '.', ';');
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        {
                            Console.WriteLine($"  -> Matched summary key in transaction row: '{key}' with value '{value}'");
                            result.Summary[key] = value;
                        }
                        continue; // Ensure summary rows are never added as transactions
                    }
                    // Only add as transaction if all required headers are present in headerIndexMap
                    var requiredHeaders = new[] { "Date", "Reference", "Description", "ValueDate", "Credit", "Debit", "Balance" };
                    bool hasAllRequiredHeaders = requiredHeaders.All(h => headerIndexMap.ContainsKey(h));
                    if (!hasAllRequiredHeaders) continue;
                    var transaction = new Transaction();
                    // Assign values using only the headers present in this table
                    foreach (var kvp in headerIndexMap)
                    {
                        var property = type.GetProperty(kvp.Key);
                        if (property != null && kvp.Value < row.Count)
                        {
                            property.SetValue(transaction, row[kvp.Value]);
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
                    // --- If row starts with summary key, add to summary and skip as transaction ---
                    var firstCell = row[0].Trim();
                    var normalizedFirstCell = NormalizeKey(firstCell);
                    Console.WriteLine($"Checking generic transaction row first cell: '{firstCell}' (normalized: '{normalizedFirstCell}')");
                    if (normalizedSummaryKeys.Contains(normalizedFirstCell) && row.Count >= 2)
                    {
                        var value = row[1].Trim();
                        if (!string.IsNullOrEmpty(value))
                        {
                            Console.WriteLine($"  -> Matched summary key in generic transaction row: '{firstCell}'");
                            result.Summary[firstCell.TrimEnd(':', '.', ';')] = value;
                        }
                        continue;
                    }
                    // Skip rows that are summary-like (first cell matches summary key)
                    if (summaryRowIndicators.Contains(row[0].Trim())) continue;
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
        // After processing tables, process key-value pairs for summary
        foreach (var (key, value) in _keyValuePairs)
        {
            var normalizedKey = NormalizeKey(key);
            Console.WriteLine($"Checking key-value pair: '{key}' (normalized: '{normalizedKey}')");
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && normalizedSummaryKeys.Contains(normalizedKey))
            {
                Console.WriteLine($"  -> Matched summary key from key-value pair: '{key}'");
                result.Summary[key.TrimEnd(':', '.', ';')] = value;
            }
            else
            {
                Console.WriteLine($"  -> Not a summary key from key-value pair");
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

    public void LoadBlocksFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        _blocks = JsonSerializer.Deserialize<List<Amazon.Textract.Model.Block>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        _keyValuePairs = ExtractKeyValuePairs(_blocks);
        Console.WriteLine($"Loaded {_blocks.Count} blocks from file: {filePath}");
    }
}
