# AWS Textract Document Processor

This solution processes documents using AWS Textract to extract tables and form data, with a focus on bank statements.

## Projects

- **TextractTest.Core**: Main application that processes documents using AWS Textract
- **TextractTest.JobChecker**: Utility to check the status of Textract jobs
- **TextractTest.Shared**: Shared models and services

## Prerequisites

- .NET 8.0
- AWS Account with Textract access
- AWS CLI configured with SSO profile

## Configuration

Set the following environment variables:
- `INPUT_BUCKET_NAME`: S3 bucket containing input documents
- `AWS_REGION`: AWS region (defaults to eu-central-1)
- `AWS_PROFILE`: AWS CLI profile name (e.g., "ayem")

## Usage

1. Process a document:
```bash
cd src/TextractTest.Core
dotnet run <document_name> [output_directory]
```

2. Check job status:
```bash
cd src/TextractTest.JobChecker
dotnet run <job_id>
```

## Output

The processor generates JSON files containing:
- Summary information (account details, balances)
- Transaction details (date, reference, description, amounts)

Output files are saved in the format:
```
[document_name]_[job_id]_[timestamp].json
``` 