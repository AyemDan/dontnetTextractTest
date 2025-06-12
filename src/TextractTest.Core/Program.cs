using Amazon.S3;
using Amazon.Textract;
using TextractTest.Core.Services;

try
{
    if (args.Length == 0)
    {
        Console.WriteLine("Please provide a document name");
        return;
    }

    var documentName = args[0];
    var forceReprocess = args.Length > 1 && args[1] == "--force";

    // Initialize AWS clients
    var s3Client = new AmazonS3Client();
    var textractClient = new AmazonTextractClient();
    
    // Get bucket name from environment variable
    var bucketName = Environment.GetEnvironmentVariable("TEXTRACT_BUCKET_NAME");
    if (string.IsNullOrEmpty(bucketName))
    {
        Console.WriteLine("Please set the TEXTRACT_BUCKET_NAME environment variable");
        return;
    }

    Console.WriteLine($"Using bucket: {bucketName}");
    Console.WriteLine($"Document name: {documentName}");

    // Process document
    var processor = new DocumentProcessor(s3Client, textractClient, bucketName);
    var jobId = await processor.ProcessDocument(documentName);
    
    Console.WriteLine($"Started Textract job: {jobId}");
    Console.WriteLine("Use the JobChecker project to check the job status and extract results");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Environment.Exit(1);
}
