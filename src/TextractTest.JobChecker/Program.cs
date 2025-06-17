using Amazon.Textract;
using TextractTest.Core.Services;

if (args.Length > 0 && args[0] == "--reparse")
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: dotnet run -- --reparse <blocksFile> [outputFile]");
        return;
    }
    var blocksFile = args[1];
    var outputFile = args.Length > 2 ? args[2] : null;
    var reparseTableProcessor = new TextractTableProcessor();
    reparseTableProcessor.LoadBlocksFromFile(blocksFile);
    var tables = reparseTableProcessor.ExtractTablesFromBlocks();
    var bankData = reparseTableProcessor.FormatBankStatementData(tables);
    if (string.IsNullOrEmpty(outputFile))
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var outputDir = Path.Combine(documentsPath, "TextractOutput");
        Directory.CreateDirectory(outputDir);
        var baseName = Path.GetFileNameWithoutExtension(blocksFile); // e.g. DocumentName_JobId_blocks
        // Try to extract original file name and jobId
        string originalFileName = baseName;
        string jobIdFromFile = "reparsed";
        var parts = baseName.Split('_');
        if (parts.Length >= 2)
        {
            originalFileName = parts[0];
            jobIdFromFile = parts[1];
        }
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        outputFile = Path.Combine(outputDir, $"{originalFileName}_{jobIdFromFile}-{timestamp}.json");
    }
    reparseTableProcessor.SaveResults(bankData, outputFile);
    Console.WriteLine($"Re-parsed and saved output to: {outputFile}");
    return;
}

var jobId = args.Length > 0 ? args[0] : null;

if (string.IsNullOrEmpty(jobId))
{
    Console.WriteLine("Please provide a job ID as a command line argument.");
    return;
}

var textractClient = new AmazonTextractClient();
var jobTracker = new JobTracker();
var jobStatusChecker = new JobStatusChecker(textractClient);
var tableProcessor = new TextractTableProcessor();

try
{
    // Get job info
    var job = jobTracker.GetJob(jobId);
    if (job == null)
    {
        Console.WriteLine($"No job found with ID: {jobId}");
        return;
    }

    // Check if output file already exists
    var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    var outputDir = Path.Combine(documentsPath, "TextractOutput");
    var expectedOutputFile = Path.Combine(outputDir, $"{job.DocumentName}_{jobId}.json");

    if (File.Exists(expectedOutputFile))
    {
        Console.WriteLine($"Output file already exists at: {expectedOutputFile}");
        Console.WriteLine("No need to process the job again.");
        return;
    }

    Console.WriteLine($"Checking status for job: {jobId}");
    Console.WriteLine($"Document: {job.DocumentName}");
    Console.WriteLine($"Current status: {job.Status}");

    // Check job status
    var status = await jobStatusChecker.WaitForJobCompletionAsync(jobId);

    // Update status in tracker
    jobTracker.UpdateJobStatus(jobId, status);

    Console.WriteLine($"Job status: {status}");

    if (status == "SUCCEEDED")
    {
        Console.WriteLine("Job completed successfully. Processing results...");

        // Extract tables from the document
        await tableProcessor.ExtractFromJobId(textractClient, jobId);
        var tables = tableProcessor.ExtractTablesFromBlocks();

        // Format the data
        var bankData = tableProcessor.FormatBankStatementData(tables);

        // Save results
        tableProcessor.SaveResults(bankData, expectedOutputFile);
    }
    else if (status == "FAILED")
    {
        Console.WriteLine("Job failed. Please check AWS Console for more details.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error checking job status: {ex.Message}");
}
