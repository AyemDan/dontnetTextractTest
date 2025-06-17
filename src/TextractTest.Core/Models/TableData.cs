using System.Text.Json.Serialization;

namespace TextractTest.Core.Models;

public class TableData
{
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("rows")]
    public List<List<string>> Rows { get; set; } = new();
    
    public int RowCount => Rows.Count;
    
    public int ColumnCount => Rows.Any() ? Rows[0].Count : 0;
}