// IngestFromUrl: an HTTP-triggered Azure Function (.NET isolated worker) that lets an automation tool
// such as Zapier add a PDF to the SpecialistAI ingestion pipeline.
//
// Flow: Zapier POSTs { "fileUrl": "...", "fileName": "..." } -> this Function downloads the PDF
// (with safety checks) -> writes it to the blob container that the existing blob-triggered
// ingestion Function already watches -> ingestion extracts, chunks, embeds and indexes it.
//
// NOT COMPILED OR RUN YET. It was written without a .NET SDK available. Build it in your repo,
// fix any small compile errors, and test with the curl command in the README.
//
// Packages (add to the Function project if missing):
//   dotnet add package Azure.Storage.Blobs
//   dotnet add package Microsoft.Azure.Functions.Worker.Extensions.Http
//
// App settings:
//   INGEST_CONTAINER           Name of the container your blob-triggered ingestion Function watches
//   INGEST_STORAGE_CONNECTION  Optional. Storage connection string. Falls back to AzureWebJobsStorage.
//   ALLOWED_DOWNLOAD_HOSTS     Optional. Comma-separated host allow-list. Default below.

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace SpecialistAI.Function;

public class IngestFromUrl
{
    private const long MaxBytes = 25L * 1024 * 1024;           // 25 MB cap
    private const string DefaultHosts = "drive.google.com,docs.google.com,googleusercontent.com,zapier.com";

    // Redirects are followed by hand so every hop can be checked against the allow-list.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly Regex SafeName = new(
        @"^[A-Za-z0-9][A-Za-z0-9 ._()\-]{0,150}\.pdf$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<IngestFromUrl> _log;

    public IngestFromUrl(ILogger<IngestFromUrl> log) => _log = log;

    private sealed record IngestRequest(string? FileUrl, string? FileName);

    // AuthorizationLevel.Function means callers must send the function key (?code=... or x-functions-key).
    [Function("IngestFromUrl")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        FunctionContext context)
    {
        var ct = context.CancellationToken;

        IngestRequest? body;
        try { body = await JsonSerializer.DeserializeAsync<IngestRequest>(req.Body, JsonOpts, ct); }
        catch (JsonException) { return await Reply(req, HttpStatusCode.BadRequest, "Body must be JSON with fileUrl and fileName."); }

        if (body is null || string.IsNullOrWhiteSpace(body.FileUrl) || string.IsNullOrWhiteSpace(body.FileName))
            return await Reply(req, HttpStatusCode.BadRequest, "fileUrl and fileName are required.");

        var fileName = body.FileName.Trim();
        if (!SafeName.IsMatch(fileName))
            return await Reply(req, HttpStatusCode.BadRequest,
                "fileName must be a plain .pdf name (letters, numbers, spaces, dots, dashes, brackets).");

        if (!Uri.TryCreate(body.FileUrl, UriKind.Absolute, out var uri))
            return await Reply(req, HttpStatusCode.BadRequest, "fileUrl is not a valid URL.");

        var allowed = (Environment.GetEnvironmentVariable("ALLOWED_DOWNLOAD_HOSTS") ?? DefaultHosts)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        MemoryStream pdf;
        try
        {
            using var res = await GetCheckedAsync(uri, allowed, ct);
            if (!res.IsSuccessStatusCode)
                return await Reply(req, HttpStatusCode.BadGateway, $"The file host answered {(int)res.StatusCode}.");
            if (res.Content.Headers.ContentLength is long len && len > MaxBytes)
                return await Reply(req, HttpStatusCode.RequestEntityTooLarge, "File is larger than 25 MB.");

            pdf = new MemoryStream();
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (pdf.Length + read > MaxBytes)
                    return await Reply(req, HttpStatusCode.RequestEntityTooLarge, "File is larger than 25 MB.");
                pdf.Write(buffer, 0, read);
            }
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning("Rejected download: {Reason}", ex.Message);
            return await Reply(req, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Download failed");
            return await Reply(req, HttpStatusCode.BadGateway, "Could not download the file.");
        }

        // A real PDF starts with "%PDF-". This stops HTML error pages being ingested as documents.
        var head = pdf.GetBuffer().AsSpan(0, (int)Math.Min(5, pdf.Length));
        if (!head.SequenceEqual("%PDF-"u8))
            return await Reply(req, HttpStatusCode.UnsupportedMediaType, "The downloaded file is not a PDF.");

        var conn = Environment.GetEnvironmentVariable("INGEST_STORAGE_CONNECTION")
                   ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var containerName = Environment.GetEnvironmentVariable("INGEST_CONTAINER");
        if (string.IsNullOrEmpty(conn) || string.IsNullOrEmpty(containerName))
            return await Reply(req, HttpStatusCode.InternalServerError, "Storage settings are missing.");

        var container = new BlobContainerClient(conn, containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = container.GetBlobClient(fileName);

        try
        {
            pdf.Position = 0;
            await blob.UploadAsync(pdf, new BlobUploadOptions
            {
                // Refuse to overwrite a document that is already in the library.
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" }
            }, ct);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return await Reply(req, HttpStatusCode.Conflict, $"{fileName} is already in the library.");
        }

        _log.LogInformation("Queued {Name} ({Bytes} bytes) for ingestion", fileName, pdf.Length);
        var ok = req.CreateResponse();
        await ok.WriteAsJsonAsync(new { status = "queued", blobName = fileName, bytes = pdf.Length }, HttpStatusCode.Accepted);
        return ok;
    }

    private static async Task<HttpResponseMessage> GetCheckedAsync(Uri uri, string[] allowed, CancellationToken ct)
    {
        for (var hop = 0; hop <= 3; hop++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Only https links are accepted.");
            if (!allowed.Any(a => uri.Host.Equals(a, StringComparison.OrdinalIgnoreCase)
                                  || uri.Host.EndsWith("." + a, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Downloads from {uri.Host} are not allowed.");

            var res = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } next)
            {
                res.Dispose();
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            return res;
        }
        throw new InvalidOperationException("Too many redirects.");
    }

    private static async Task<HttpResponseData> Reply(HttpRequestData req, HttpStatusCode code, string message)
    {
        var r = req.CreateResponse();
        await r.WriteAsJsonAsync(new { status = "error", message }, code);
        return r;
    }
}
