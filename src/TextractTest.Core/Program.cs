using Amazon.Textract;
using Amazon.Textract.Model;
using TextractTest.Core.Services;
using TextractTest.Shared.Models;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run <document_name> [output_directory]");
            Console.WriteLine("Example: dotnet run document.pdf ./output");
            return;
        }

        var document = args[0];
        var outputDirectory = args.Length > 1 ? args[1] : null;
        var bucketName = Environment.GetEnvironmentVariable("INPUT_BUCKET_NAME");
        var region = Environment.GetEnvironmentVariable("AWS_REGION");

        if (string.IsNullOrEmpty(bucketName))
        {
            Console.WriteLine("Error: INPUT_BUCKET_NAME environment variable is not set");
            Console.WriteLine("Please set the following environment variables:");
            Console.WriteLine("  - INPUT_BUCKET_NAME: The name of your S3 bucket");
            Console.WriteLine("  - AWS_REGION: (Optional) The AWS region (defaults to eu-central-1)");
            Console.WriteLine("\nNote: AWS credentials will be loaded from your AWS CLI profile");
            return;
        }

        try
        {
            // Initialize processor with configuration
            var processor = new DocumentProcessor(
                bucket: bucketName,
                document: document,
                region: region,
                outputDirectory: outputDirectory
            );

            // Process document with Analysis type
            await processor.ProcessDocumentAsync(ProcessType.Analysis);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
