using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using TextractTest.Core.Models;
using TextractTest.Core.Services;

namespace TextractTest.Core.Services;

public class DocumentProcessor
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonTextract _textractClient;
    private readonly JobTracker _jobTracker;
    private readonly string _bucketName;
    private readonly TextractTableProcessor _tableProcessor;

    public DocumentProcessor(
        IAmazonS3 s3Client,
        IAmazonTextract textractClient,
        string bucketName)
    {
        _s3Client = s3Client;
        _textractClient = textractClient;
        _bucketName = bucketName;
        _tableProcessor = new TextractTableProcessor();
        
        // Initialize JobTracker with the current directory
        _jobTracker = new JobTracker(Directory.GetCurrentDirectory());
    }

    public async Task<string> ProcessDocument(string documentName)
    {
        try
        {
            // Check if document exists in S3
            var exists = await DocumentExistsInS3(documentName);
            if (!exists)
            {
                throw new Exception($"Document {documentName} not found in bucket {_bucketName}");
            }

            // Check for existing job
            var existingJob = _jobTracker.GetJobByDocument(documentName);
            if (existingJob != null)
            {
                // Check if we already have output for this job
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var outputDir = Path.Combine(documentsPath, "TextractOutput");
                
                if (Directory.Exists(outputDir))
                {
                    // Look for any file matching the pattern documentName_jobId-*.json
                    var existingFiles = Directory.GetFiles(outputDir, $"{documentName}_{existingJob.JobId}-*.json");
                    if (existingFiles.Length > 0)
                    {
                        Console.WriteLine($"Document {documentName} has already been processed.");
                        Console.WriteLine($"Output file exists at: {existingFiles[0]}");
                        return existingJob.JobId;
                    }
                }
            }

            // Start Textract job
            var jobId = await StartTextractJob(documentName);
            
            // Save job info
            _jobTracker.AddJob(jobId, documentName, _bucketName);

            // Wait for job completion and process results immediately
            var jobStatusChecker = new JobStatusChecker(_textractClient);
            var status = await jobStatusChecker.WaitForJobCompletionAsync(jobId);
            
            if (status == "SUCCEEDED")
            {
                Console.WriteLine("Job completed successfully. Processing results...");
                
                // Extract tables from the document
                await _tableProcessor.ExtractFromJobId(_textractClient, jobId);
                var tables = _tableProcessor.ExtractTablesFromBlocks();
                
                // Format the data
                var bankData = _tableProcessor.FormatBankStatementData(tables);
                
                // Save results
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var outputDir = Path.Combine(documentsPath, "TextractOutput");
                Directory.CreateDirectory(outputDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var outputFile = Path.Combine(outputDir, $"{documentName}_{jobId}-{timestamp}.json");
                
                _tableProcessor.SaveResults(bankData, outputFile);
            }
            else
            {
                Console.WriteLine($"Job failed with status: {status}");
            }

            return jobId;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error processing document: {ex.Message}", ex);
        }
    }

    private async Task<bool> DocumentExistsInS3(string documentName)
    {
        try
        {
            var response = await _s3Client.GetObjectMetadataAsync(_bucketName, documentName);
            return true;
        }
        catch (Amazon.S3.AmazonS3Exception ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;
            throw;
        }
    }

    private async Task<string> StartTextractJob(string documentName)
    {
        var request = new StartDocumentAnalysisRequest
        {
            DocumentLocation = new DocumentLocation
            {
                S3Object = new Amazon.Textract.Model.S3Object
                {
                    Bucket = _bucketName,
                    Name = documentName
                }
            },
            FeatureTypes = new List<string> { "TABLES" }
        };

        var response = await _textractClient.StartDocumentAnalysisAsync(request);
        return response.JobId;
    }
} 