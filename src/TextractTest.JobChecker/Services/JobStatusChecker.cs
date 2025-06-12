using Amazon.Textract;
using Amazon.Textract.Model;

namespace TextractTest.JobChecker.Services;

public class JobStatusChecker
{
    private readonly IAmazonTextract _textract;
    private int _dots;

    public JobStatusChecker(IAmazonTextract textract)
    {
        _textract = textract;
    }

    public async Task<string> CheckJobStatusAsync(string jobId)
    {
        try
        {
            var response = await _textract.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest
            {
                JobId = jobId
            });

            var status = response.JobStatus.Value;
            Console.WriteLine($"Job {jobId} status: {status}");

            if (status == "SUCCEEDED")
            {
                Console.WriteLine($"Pages processed: {response.DocumentMetadata.Pages}");
            }
            else if (status == "FAILED")
            {
                var errorMessage = response.StatusMessage ?? "No error message available";
                Console.WriteLine($"Error message: {errorMessage}");
            }

            return status;
        }
        catch (InvalidJobIdException)
        {
            Console.WriteLine($"Error: Job {jobId} not found");
            return "NOT_FOUND";
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error checking job status: {e}");
            return "ERROR";
        }
    }

    public async Task<string> WaitForJobCompletionAsync(string jobId, int checkIntervalSeconds = 5)
    {
        Console.WriteLine($"Waiting for job {jobId} to complete...");
        _dots = 0;

        while (true)
        {
            var status = await CheckJobStatusAsync(jobId);

            if (status is "SUCCEEDED" or "FAILED" or "NOT_FOUND" or "ERROR")
            {
                return status;
            }

            // Print progress dots
            if (_dots < 40)
            {
                Console.Write(".");
                await Console.Out.FlushAsync();
                _dots++;
            }
            else
            {
                Console.WriteLine();
                _dots = 0;
            }

            await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds));
        }
    }
} 