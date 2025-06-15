using System.Text.Json;
using TextractTest.Core.Models;

namespace TextractTest.Core.Services;

public class JobRecordService
{
    private readonly string _jobRecordsFile;
    private List<JobRecord> _jobRecords;

    public JobRecordService(string? outputDirectory = null)
    {
        var baseDir = outputDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        _jobRecordsFile = Path.Combine(baseDir, "job_records.json");
        _jobRecords = LoadJobRecords();
    }

    private List<JobRecord> LoadJobRecords()
    {
        if (!File.Exists(_jobRecordsFile))
        {
            return new List<JobRecord>();
        }

        try
        {
            var json = File.ReadAllText(_jobRecordsFile);
            return JsonSerializer.Deserialize<List<JobRecord>>(json) ?? new List<JobRecord>();
        }
        catch (Exception)
        {
            return new List<JobRecord>();
        }
    }

    private void SaveJobRecords()
    {
        var json = JsonSerializer.Serialize(_jobRecords, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_jobRecordsFile, json);
    }

    public void AddJobRecord(string jobId, string documentName, string status, string outputFile)
    {
        _jobRecords.Add(new JobRecord
        {
            JobId = jobId,
            DocumentName = documentName,
            Status = status,
            ProcessedDate = DateTime.UtcNow,
            OutputFile = outputFile
        });
        SaveJobRecords();
    }

    public void UpdateJobStatus(string jobId, string status)
    {
        var record = _jobRecords.FirstOrDefault(r => r.JobId == jobId);
        if (record != null)
        {
            record.Status = status;
            SaveJobRecords();
        }
    }

    public JobRecord? GetJobRecord(string jobId)
    {
        return _jobRecords.FirstOrDefault(r => r.JobId == jobId);
    }

    public bool HasBeenProcessed(string documentName)
    {
        return _jobRecords.Any(r =>
            r.DocumentName == documentName &&
            r.Status == "COMPLETED" &&
            File.Exists(r.OutputFile));
    }

    public List<JobRecord> GetAllJobRecords()
    {
        return _jobRecords.ToList();
    }
}