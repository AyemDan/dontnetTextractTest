namespace TextractTest.JobChecker.Services;

public class JobTracker
{
    private readonly Dictionary<string, JobInfo> _jobs = new();

    public void AddJob(string jobId, string documentName)
    {
        _jobs[jobId] = new JobInfo
        {
            JobId = jobId,
            DocumentName = documentName,
            Status = "STARTED",
            StartTime = DateTime.UtcNow
        };
        Console.WriteLine($"Added job {jobId} for document {documentName}");
    }

    public void UpdateJobStatus(string jobId, string status)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = status;
            job.LastUpdateTime = DateTime.UtcNow;
            if (status is "SUCCEEDED" or "FAILED")
            {
                job.EndTime = DateTime.UtcNow;
            }
            Console.WriteLine($"Updated job {jobId} status to {status}");
        }
    }

    private class JobInfo
    {
        public string JobId { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
} 