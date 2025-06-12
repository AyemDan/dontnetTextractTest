using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.Textract;
using TextractTest.Shared.Services;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: dotnet run <job_id>");
            Console.WriteLine("Example: dotnet run 1234567890");
            return;
        }

        var jobId = args[0];
        var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "eu-central-1";

        try
        {
            // Initialize AWS credentials from profile
            var chain = new CredentialProfileStoreChain();
            AWSCredentials awsCredentials;

            if (!chain.TryGetAWSCredentials(Environment.GetEnvironmentVariable("AWS_PROFILE") ?? "default", out awsCredentials))
            {
                throw new Exception("Failed to load AWS credentials from profile. Please ensure you're logged in with 'aws sso login'");
            }

            // Initialize AWS Textract client
            var textractClient = new AmazonTextractClient(awsCredentials, RegionEndpoint.GetBySystemName(region));
            var checker = new JobStatusChecker(textractClient);

            // Wait for job completion
            var status = await checker.WaitForJobCompletionAsync(jobId);
            Console.WriteLine($"Final job status: {status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
