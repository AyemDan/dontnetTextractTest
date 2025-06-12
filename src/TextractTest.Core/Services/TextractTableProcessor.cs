using System.Text.Json;
using Amazon.Textract.Model;
using TextractTest.Core.Models;

namespace TextractTest.Core.Services;

public class TextractTableProcessor
{
    private readonly List<Block> _blocks;
    private static readonly Dictionary<string, string[]> HeaderMappings = new()
    {
        ["Date"] = new[] { "Date", "Transaction Date", "Value Date", "Tran Date", "Create Date" },
        ["Reference"] = new[] { "Reference", "Reference No", "Ref No", "Transaction ID", "Trans ID", "Trans Ref" },
        ["Description"] = new[] { "Description", "Narration", "Transaction Description", "Details", "Particulars", "Description/Payee/Memo" },
        ["Value Date"] = new[] { "Value Date", "Val Date", "Settlement Date", "Create Date" },
        ["Deposit"] = new[] { "Deposit", "Credit", "Credit Amount", "Amount (CR)", "Deposits" },
        ["Withdrawal"] = new[] { "Withdrawal", "Debit", "Debit Amount", "Amount (DR)", "Withdrawals" },
        ["Balance"] = new[] { "Balance", "Running Balance", "Closing Balance", "Current Balance" }
    };

    public TextractTableProcessor(List<Block> blocks)
    {
        _blocks = blocks;
    }

    public List<List<List<string>>> ExtractTablesFromBlocks()
    {
        var tables = new List<List<List<string>>>();
        List<List<string>>? currentTable = null;

        foreach (var block in _blocks)
        {
            if (block.BlockType == "TABLE")
            {
                if (currentTable != null)
                {
                    tables.Add(currentTable);
                }
                currentTable = new List<List<string>>();
            }
            else if (block.BlockType == "CELL" && currentTable != null)
            {
                // Get cell text
                var text = GetCellText(block);

                // Add cell to table
                var rowIndex = block.RowIndex - 1;
                var colIndex = block.ColumnIndex - 1;

                // Ensure row exists
                while (currentTable.Count <= rowIndex)
                {
                    currentTable.Add(new List<string>());
                }

                // Ensure column exists
                while (currentTable[rowIndex].Count <= colIndex)
                {
                    currentTable[rowIndex].Add(string.Empty);
                }

                // Add cell text
                currentTable[rowIndex][colIndex] = text.Trim();
            }
        }

        // Add last table if exists
        if (currentTable != null)
        {
            tables.Add(currentTable);
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
                        if (childBlock?.BlockType == "WORD")
                        {
                            text.Add(childBlock.Text);
                        }
                    }
                }
            }
        }
        return string.Join(" ", text);
    }

    public BankStatementData FormatBankStatementData(List<List<List<string>>> tables)
    {
        var result = new BankStatementData();

        foreach (var table in tables)
        {
            if (table.Count == 0 || table[0].Count == 0) continue;

            var firstRow = table[0];

            // Check if this is a summary/account info table
            if (firstRow.Any(cell => cell.ToLower().Contains("account") || 
                                   cell.ToLower().Contains("currency") || 
                                   cell.ToLower().Contains("balance:") || 
                                   cell.ToLower().Contains("period") || 
                                   cell.ToLower().Contains("statement") || 
                                   cell.ToLower().Contains("branch")))
            {
                Console.WriteLine("Found summary table");
                foreach (var row in table.Skip(1))
                {
                    if (row.Count >= 2)
                    {
                        var key = row[0].Trim().TrimEnd(':');
                        var value = row[1].Trim();
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        {
                            result.Summary[key] = value;
                        }
                    }
                }
                continue;
            }

            // Try to identify if this is a transaction table
            var headerMap = MapHeaders(firstRow);
            Console.WriteLine($"Found {headerMap.Count} matching headers");

            if (headerMap.Count >= 4)
            {
                Console.WriteLine("Processing as transaction table");
                foreach (var row in table.Skip(1))
                {
                    if (row.Count != firstRow.Count) continue;

                    var transaction = new Dictionary<string, string>();
                    foreach (var standardHeader in HeaderMappings.Keys)
                    {
                        if (headerMap.ContainsKey(standardHeader))
                        {
                            var idx = headerMap[standardHeader];
                            transaction[standardHeader] = idx < row.Count ? row[idx].Trim() : "";
                        }
                        else
                        {
                            transaction[standardHeader] = "";
                        }
                    }

                    if (transaction.Values.Any(v => !string.IsNullOrEmpty(v)))
                    {
                        result.Transactions.Add(transaction);
                    }
                }
            }
        }

        return result;
    }

    private Dictionary<string, int> MapHeaders(List<string> actualHeaders)
    {
        var headerMap = new Dictionary<string, int>();
        Console.WriteLine($"\nDebug - Found headers: {string.Join(", ", actualHeaders)}");

        foreach (var mapping in HeaderMappings)
        {
            var standardHeader = mapping.Key;
            var possibleNames = mapping.Value;

            var matchedHeaderIndex = actualHeaders.FindIndex(header =>
                possibleNames.Any(possible => 
                    header.Contains(possible, StringComparison.OrdinalIgnoreCase)));

            if (matchedHeaderIndex >= 0)
            {
                headerMap[standardHeader] = matchedHeaderIndex;
                Console.WriteLine($"Mapped '{actualHeaders[matchedHeaderIndex]}' to '{standardHeader}'");
            }
        }

        return headerMap;
    }

    public void SaveResults(BankStatementData data, string outputFile)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(outputFile, json);

        Console.WriteLine($"Results saved to {outputFile}");
        Console.WriteLine($"Found {data.Summary.Count} summary fields and {data.Transactions.Count} transactions");
    }
} 