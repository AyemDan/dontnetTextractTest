using System.Text.Json.Serialization;

namespace TextractTest.Core.Models;

public class JobRecord
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ProcessedDate { get; set; }
    public string OutputFile { get; set; } = string.Empty;
} 