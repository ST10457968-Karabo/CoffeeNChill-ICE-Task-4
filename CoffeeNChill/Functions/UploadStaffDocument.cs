using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.RegularExpressions;

namespace CoffeeNChill.Functions;

public class UploadStaffDocument
{
    private const string ContainerName = "staff-docs";

    private readonly ILogger<UploadStaffDocument> _logger;
    private readonly BlobServiceClient _blobServiceClient;

    // Allowed MIME types for staff documents
    private static readonly HashSet<string> AllowedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/msword", // .doc
        "image/jpeg",
        "image/png",
        "image/gif",
        "text/plain"
    };

    public UploadStaffDocument(ILogger<UploadStaffDocument> logger, BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _blobServiceClient = blobServiceClient;
    }

    [Function("UploadStaffDocument")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequestData req)
    {
        _logger.LogInformation("UploadStaffDocument triggered.");

        try
        {
            // Check if the request has multipart/form-data content
            if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues) ||
                !contentTypeValues.Any(ct => ct.Contains("multipart/form-data")))
            {
                return await BuildErrorResponse(req, HttpStatusCode.BadRequest,
                    "Request must be multipart/form-data with a file attachment.");
            }

            var boundary = GetBoundary(contentTypeValues.First());
            if (string.IsNullOrEmpty(boundary))
            {
                return await BuildErrorResponse(req, HttpStatusCode.BadRequest,
                    "Could not determine multipart boundary.");
            }

            // Stream the multipart body directly - no full buffering into memory
            var multipartReader = new MultipartReader(boundary, req.Body);

            string? fileName = null;
            string? contentType = null;
            bool fileFound = false;
            bool uploadSucceeded = false;
            string? uniqueFileName = null;

            MultipartSection? section;
            while ((section = await multipartReader.ReadNextSectionAsync()) != null)
            {
                var contentDisposition = section.GetContentDispositionHeader();
                if (contentDisposition == null || !contentDisposition.IsFileDisposition())
                {
                    continue; // skip non-file form fields
                }

                fileFound = true;
                fileName = contentDisposition.FileName.Value ?? "unnamed";
                contentType = section.ContentType ?? "application/octet-stream";

                // Validate MIME type before touching storage
                if (!IsAllowedMimeType(contentType))
                {
                    return await BuildErrorResponse(req, HttpStatusCode.BadRequest,
                        $"File type '{contentType}' is not allowed. Allowed types: PDF, DOCX, DOC, JPEG, PNG, GIF, TXT.");
                }

                // Ensure the blob container exists
                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                await containerClient.CreateIfNotExistsAsync();

                string sanitizedFileName = SanitizeFileName(fileName);
                uniqueFileName = GenerateUniqueFileName(sanitizedFileName);

                var blobClient = containerClient.GetBlobClient(uniqueFileName);

                try
                {
                    // Stream section.Body straight to blob storage - never buffered into a byte[]
                    await blobClient.UploadAsync(section.Body, overwrite: false);
                    uploadSucceeded = true;
                    _logger.LogInformation($"File uploaded successfully: {uniqueFileName}");
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError(ex, $"Error uploading file {uniqueFileName} to blob storage.");
                    return await BuildErrorResponse(req, HttpStatusCode.InternalServerError,
                        "An error occurred while uploading the file to storage.");
                }

                break; // only process the first file part
            }

            if (!fileFound || !uploadSucceeded)
            {
                return await BuildErrorResponse(req, HttpStatusCode.BadRequest,
                    "No file was uploaded. Please attach a file with the request.");
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                fileName = uniqueFileName,
                originalFileName = SanitizeFileName(fileName!),
                contentType = contentType,
                uploadDate = DateTime.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document upload.");
            return await BuildErrorResponse(req, HttpStatusCode.InternalServerError,
                "An error occurred while processing the upload.");
        }
    }

    private static string? GetBoundary(string contentType)
    {
        var boundaryMatch = Regex.Match(contentType, @"boundary=(?<boundary>.+?)(;|$)");
        if (boundaryMatch.Success)
        {
            string boundary = boundaryMatch.Groups["boundary"].Value;
            if (boundary.StartsWith("\"") && boundary.EndsWith("\""))
            {
                boundary = boundary.Substring(1, boundary.Length - 2);
            }
            return boundary;
        }
        return null;
    }

    private static bool IsAllowedMimeType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        foreach (var allowed in AllowedMimeTypes)
        {
            if (contentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string SanitizeFileName(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        string invalidChars = new string(Path.GetInvalidFileNameChars()) + " ";
        var regex = new Regex($"[{Regex.Escape(invalidChars)}]");
        return regex.Replace(fileName, "_");
    }

    private static string GenerateUniqueFileName(string originalFileName)
    {
        string extension = Path.GetExtension(originalFileName);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string guid = Guid.NewGuid().ToString().Substring(0, 8);
        return $"{nameWithoutExtension}_{timestamp}_{guid}{extension}";
    }

    private static async Task<HttpResponseData> BuildErrorResponse(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}