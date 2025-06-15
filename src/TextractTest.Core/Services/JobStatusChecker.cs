using Amazon.Textract;
using Amazon.Textract.Model;

namespace TextractTest.Core.Services;

public class JobStatusChecker
{
    private readonly IAmazonTextract _textractClient;

    public JobStatusChecker(IAmazonTextract textractClient)
    {
        _textractClient = textractClient;
    }

    public async Task<string> CheckJobStatus(string jobId)
    {
        try
        {
            var request = new GetDocumentAnalysisRequest { JobId = jobId };
            var response = await _textractClient.GetDocumentAnalysisAsync(request);
            return response.JobStatus.Value;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error checking job status: {ex.Message}", ex);
        }
    }

    public async Task<string> WaitForJobCompletionAsync(string jobId, int maxAttempts = 60, int delaySeconds = 5)
    {
        var attempts = 0;
        while (attempts < maxAttempts)
        {
            var status = await CheckJobStatus(jobId);

            switch (status)
            {
                case "SUCCEEDED":
                case "FAILED":
                    return status;
                case "IN_PROGRESS":
                    Console.WriteLine($"Job {jobId} is still in progress. Waiting {delaySeconds} seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    attempts++;
                    break;
                default:
                    throw new Exception($"Unknown job status: {status}");
            }
        }

        throw new Exception($"Job {jobId} did not complete within {maxAttempts * delaySeconds} seconds");
    }
}