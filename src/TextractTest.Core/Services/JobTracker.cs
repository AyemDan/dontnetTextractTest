using System.Text.Json;

namespace TextractTest.Core.Services;

public class JobTracker
{
    private readonly string _jobsFile;
    private List<JobInfo> _jobs;

    public JobTracker(string baseDirectory)
    {
        // Use the current working directory for jobs.json
        _jobsFile = Path.Combine(Directory.GetCurrentDirectory(), "jobs.json");
        LoadJobs();
    }

    private void LoadJobs()
    {
        if (File.Exists(_jobsFile))
        {
            var json = File.ReadAllText(_jobsFile);
            _jobs = JsonSerializer.Deserialize<List<JobInfo>>(json) ?? new List<JobInfo>();
        }
        else
        {
            _jobs = new List<JobInfo>();
        }
    }

    private void SaveJobs()
    {
        var json = JsonSerializer.Serialize(_jobs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_jobsFile, json);
        Console.WriteLine($"Jobs saved to {_jobsFile}");
    }

    public void AddJob(string jobId, string documentName, string bucketName)
    {
        var job = new JobInfo
        {
            JobId = jobId,
            DocumentName = documentName,
            BucketName = bucketName,
            Status = "SUBMITTED",
            CreatedAt = DateTime.UtcNow
        };

        _jobs.Add(job);
        SaveJobs();
        Console.WriteLine($"Added job {jobId} for document {documentName}");
    }

    public JobInfo? GetJob(string jobId)
    {
        return _jobs.FirstOrDefault(j => j.JobId == jobId);
    }

    public JobInfo? GetJobByDocument(string documentName)
    {
        return _jobs.FirstOrDefault(j => j.DocumentName == documentName);
    }

    public void UpdateJobStatus(string jobId, string status)
    {
        var job = GetJob(jobId);
        if (job != null)
        {
            job.Status = status;
            job.UpdatedAt = DateTime.UtcNow;
            SaveJobs();
        }
    }

    public IEnumerable<JobInfo> GetAllJobs()
    {
        return _jobs.OrderByDescending(j => j.CreatedAt);
    }

    public IEnumerable<JobInfo> GetPendingJobs()
    {
        return _jobs
            .Where(j => j.Status != "SUCCEEDED" && j.Status != "FAILED")
            .OrderBy(j => j.CreatedAt);
    }

    public void RemoveJob(string jobId)
    {
        var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
        if (job != null)
        {
            _jobs.Remove(job);
            SaveJobs();
            Console.WriteLine($"Removed job {jobId}");
        }
    }

    public void CleanupOldJobs(int daysToKeep = 30)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
        var oldJobs = _jobs
            .Where(j => j.CreatedAt < cutoffDate && (j.Status == "SUCCEEDED" || j.Status == "FAILED"))
            .ToList();

        if (oldJobs.Any())
        {
            foreach (var job in oldJobs)
            {
                _jobs.Remove(job);
            }
            SaveJobs();
            Console.WriteLine($"Cleaned up {oldJobs.Count} old jobs");
        }
    }
}

public class JobInfo
{
    public string JobId { get; set; } = "";
    public string DocumentName { get; set; } = "";
    public string BucketName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
} 