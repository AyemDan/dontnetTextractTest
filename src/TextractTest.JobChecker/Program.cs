﻿using Amazon.Textract;
using TextractTest.Core.Services;

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

var parseBlocksArgIndex = Array.IndexOf(args, "--parse-blocks");
if (parseBlocksArgIndex >= 0 && args.Length > parseBlocksArgIndex + 1)
{
    var blocksFile = args[parseBlocksArgIndex + 1];
    var outputArgIndex = Array.IndexOf(args, "--output");
    string outputFile = null;
    if (outputArgIndex >= 0 && args.Length > outputArgIndex + 1)
    {
        outputFile = args[outputArgIndex + 1];
    }
    else
    {
        // Default output file in TextractOutput/parsed/
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var outputDir = Path.Combine(documentsPath, "TextractOutput", "parsed");
        Directory.CreateDirectory(outputDir);
        var baseName = Path.GetFileNameWithoutExtension(blocksFile);
        outputFile = Path.Combine(outputDir, baseName + "_parsed.json");
    }
    tableProcessor.ParseBlocksFile(blocksFile, outputFile);
    Console.WriteLine($"Parsed output saved to: {outputFile}");
    return;
}

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
        await tableProcessor.ExtractFromJobId(textractClient, jobId, job.DocumentName);
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
