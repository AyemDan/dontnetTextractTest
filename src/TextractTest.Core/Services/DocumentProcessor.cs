using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.Textract;
using Amazon.Textract.Model;
using TextractTest.Core.Services;
using TextractTest.Shared.Models;
using TextractTest.Shared.Services;

namespace TextractTest.Core.Services;

public class DocumentProcessor
{
    private readonly string _bucket;
    private readonly string _document;
    private readonly string _regionName;
    private readonly IAmazonTextract _textract;
    private readonly IAmazonS3 _s3;
    private readonly JobTracker _tracker;
    private string _jobId = string.Empty;
    private readonly string _outputDirectory;

    public DocumentProcessor(string bucket, string document, string? region = null, string? outputDirectory = null)
    {
        _bucket = bucket;
        _document = document;
        _regionName = region ?? Environment.GetEnvironmentVariable("AWS_REGION") ?? "eu-central-1";
        _outputDirectory = outputDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");

        // Create output directory if it doesn't exist
        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        // Initialize AWS clients using AWS CLI credentials
        var regionEndpoint = RegionEndpoint.GetBySystemName(_regionName);
        var chain = new CredentialProfileStoreChain();
        AWSCredentials awsCredentials;

        if (!chain.TryGetAWSCredentials(Environment.GetEnvironmentVariable("AWS_PROFILE") ?? "default", out awsCredentials))
        {
            throw new Exception("Failed to load AWS credentials from profile. Please ensure you're logged in with 'aws sso login'");
        }

        _s3 = new AmazonS3Client(awsCredentials, regionEndpoint);
        _textract = new AmazonTextractClient(awsCredentials, regionEndpoint);
        _tracker = new JobTracker();
    }

    public async Task ProcessDocumentAsync(ProcessType type)
    {
        Console.WriteLine($"Using bucket: {_bucket}");
        Console.WriteLine($"Document name: {_document}");
        Console.WriteLine($"Output directory: {_outputDirectory}");

        try
        {
            // Verify the document exists in S3
            await _s3.GetObjectMetadataAsync(_bucket, _document);
            Console.WriteLine($"Document exists in S3: {_document}");

            // Start appropriate processing type
            if (type == ProcessType.Detection)
            {
                var response = await _textract.StartDocumentTextDetectionAsync(new StartDocumentTextDetectionRequest
                {
                    DocumentLocation = new DocumentLocation
                    {
                        S3Object = new Amazon.Textract.Model.S3Object
                        {
                            Bucket = _bucket,
                            Name = _document
                        }
                    }
                });

                _jobId = response.JobId;
                Console.WriteLine("Processing type: Detection");
            }
            else if (type == ProcessType.Analysis)
            {
                var response = await _textract.StartDocumentAnalysisAsync(new StartDocumentAnalysisRequest
                {
                    DocumentLocation = new DocumentLocation
                    {
                        S3Object = new Amazon.Textract.Model.S3Object
                        {
                            Bucket = _bucket,
                            Name = _document
                        }
                    },
                    FeatureTypes = new List<string> { "TABLES", "FORMS" }
                });

                _jobId = response.JobId;
                Console.WriteLine("Processing type: Analysis");
            }
            else
            {
                throw new ArgumentException("Invalid processing type. Choose Detection or Analysis.");
            }

            Console.WriteLine($"Started Job Id: {_jobId}");
            _tracker.AddJob(_jobId, _document);

            // Poll for job completion
            Console.WriteLine("Waiting for job completion...");
            var checker = new JobStatusChecker(_textract);
            var status = await checker.WaitForJobCompletionAsync(_jobId);

            if (status == "SUCCEEDED")
            {
                await GetResultsAsync(_jobId);
            }

            Console.WriteLine("Done!");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in document processing: {e}");
            if (!string.IsNullOrEmpty(_jobId))
            {
                _tracker.UpdateJobStatus(_jobId, "FAILED");
            }
            throw;
        }
    }

    private async Task GetResultsAsync(string jobId)
    {
        const int maxResults = 1000;
        string? paginationToken = null;
        var finished = false;
        var allBlocks = new List<Block>();

        while (!finished)
        {
            GetDocumentAnalysisResponse response;
            if (paginationToken != null)
            {
                response = await _textract.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest
                {
                    JobId = jobId,
                    MaxResults = maxResults,
                    NextToken = paginationToken
                });
            }
            else
            {
                response = await _textract.GetDocumentAnalysisAsync(new GetDocumentAnalysisRequest
                {
                    JobId = jobId,
                    MaxResults = maxResults
                });
            }

            // Add blocks to our collection
            allBlocks.AddRange(response.Blocks);
            Console.WriteLine($"Processing page {response.Blocks.Count} blocks");

            // Debug logging
            foreach (var block in response.Blocks)
            {
                if (block.BlockType == "TABLE")
                {
                    Console.WriteLine($"\nFound TABLE block with {block.Relationships?.FirstOrDefault(r => r.Type == "CHILD")?.Ids.Count ?? 0} cells");
                }
                else if (block.BlockType == "CELL")
                {
                    var text = block.Relationships?.FirstOrDefault(r => r.Type == "CHILD")?.Ids
                        .Select(id => allBlocks.FirstOrDefault(b => b.Id == id))
                        .Where(b => b?.BlockType == "WORD")
                        .Select(b => b.Text)
                        .FirstOrDefault() ?? "";

                    if (!string.IsNullOrEmpty(text))
                    {
                        Console.WriteLine($"Cell at Row {block.RowIndex}, Col {block.ColumnIndex}: {text}");
                    }
                }
            }

            paginationToken = response.NextToken;
            finished = string.IsNullOrEmpty(paginationToken);
        }

        // Process all blocks using TextractTableProcessor
        var processor = new TextractTableProcessor(allBlocks);
        var tables = processor.ExtractTablesFromBlocks();
        Console.WriteLine($"Found {tables.Count} tables");

        Console.WriteLine("Formatting bank statement data...");
        var data = processor.FormatBankStatementData(tables);

        // Save results with job ID and document name in the filename
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var safeDocName = Path.GetFileNameWithoutExtension(_document).Replace(" ", "_");
        var outputFile = Path.Combine(_outputDirectory, $"{safeDocName}_{jobId}_{timestamp}.json");
        
        processor.SaveResults(data, outputFile);
        _tracker.UpdateJobStatus(jobId, "COMPLETED");
    }
} 