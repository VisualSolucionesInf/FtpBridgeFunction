using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Async;

namespace FtpBridgeFunction;

public class DownloadFromFtp
{
    private readonly ILogger<DownloadFromFtp> _logger;

    public DownloadFromFtp(ILogger<DownloadFromFtp> logger)
    {
        _logger = logger;
    }

    [Function("DownloadFromFtp")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Recibida peticion de descarga desde FTP.");

        DownloadPayload? payload;
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            payload = JsonSerializer.Deserialize<DownloadPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al parsear el body recibido.");
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync("Body invalido.");
            return badReq;
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.FtpHost)
            || string.IsNullOrWhiteSpace(payload.FtpUser)
            || string.IsNullOrWhiteSpace(payload.FtpPassword)
            || string.IsNullOrWhiteSpace(payload.FileName))
        {
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync("Faltan campos obligatorios: ftpHost, ftpUser, ftpPassword, fileName.");
            return badReq;
        }

        try
        {
            string ftpFolder = payload.FtpFolder ?? string.Empty;
            string remotePath = $"ftp://{payload.FtpHost.TrimEnd('/')}";
            // Puerto explícito.
            if (payload.Port != null)
                remotePath += $":{payload.Port}";
            // Directorio desde raíz.
            if (!string.IsNullOrEmpty(payload.FtpFolder))
                remotePath += $"/{payload.FtpFolder.Trim('/')}";

            remotePath += $"/{payload.FileName}".Replace("//", "/").Replace("ftp:/", "ftp://");

            byte[] memoryStream;
            if (payload.UseSftp ?? false)
                memoryStream = await Sftp(payload);
            else
                memoryStream = await Ftp(payload, remotePath);

            string base64Content = Convert.ToBase64String(memoryStream.ToArray());

            // ######## RAR TO ZIP ########
            if (payload.ConvertRarToZip ?? false)
            {
                // Convierte el RAR descargado a ZIP antes de devolverlo a Base64.
                base64Content = await ConvertRarToZip(base64Content);
            }

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new DownloadResponse()
            {
                FileName = payload.FileName,
                Success = true,
                Base64Content = base64Content
            });
            return ok;
        }
        catch (CompressConvertException ex)
        {
            _logger.LogError(ex, "Error al convertir de RAR a ZIP");
            var okButFailure = req.CreateResponse(HttpStatusCode.OK);
            await okButFailure.WriteAsJsonAsync(new DownloadResponse()
            {
                FileName = payload.FileName,
                Success = false,
                ErrorMessage = $"Error al convertir de RAR a ZIP: {ex.Message}"
            });
            return okButFailure;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar el fichero por FTP.");
            var okButFailure = req.CreateResponse(HttpStatusCode.OK);
            await okButFailure.WriteAsJsonAsync(new DownloadResponse()
            {
                FileName = payload.FileName,
                Success = false,
                ErrorMessage = ex.Message
            });
            return okButFailure;
        }
    }

    private static async Task<byte[]> Ftp(DownloadPayload payload, string remotePath)
    {
        var request = (FtpWebRequest)WebRequest.Create(remotePath);
        request.Method = WebRequestMethods.Ftp.DownloadFile;
        request.Credentials = new NetworkCredential(payload.FtpUser, payload.FtpPassword);
        request.UsePassive = true;
        request.UseBinary = true;
        request.EnableSsl = payload.UseSsl ?? false;
        using var response = (FtpWebResponse)await request.GetResponseAsync();
        using var responseStream = response.GetResponseStream();
        using var memoryStream = new MemoryStream();
        await responseStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    private static async Task<byte[]> Sftp(DownloadPayload payload)
    {
        using var client = new SftpClient(payload.FtpHost, payload.FtpUser, payload.FtpPassword);
        client.Connect();
        using var memoryStream = new MemoryStream();
        await client.DownloadAsync($"/{payload.FtpFolder?.Trim('/')}/{payload.FileName}", memoryStream);
        client.Disconnect();
        return memoryStream.ToArray();
    }

    private static async Task<string> ConvertRarToZip(string rarBase64)
    {
        using var rarStream = new MemoryStream(Convert.FromBase64String(rarBase64));
        using var zipStream = new MemoryStream();
        try
        {
            await CompressionConverter.RarToZip(rarStream, zipStream);
        }
        catch (Exception ex)
        {
            throw new CompressConvertException(ex.Message, ex);
        }
        return Convert.ToBase64String(zipStream.ToArray());
    }
}

public class DownloadPayload
{
    public string? FtpHost { get; set; }
    public string? FtpUser { get; set; }
    public string? FtpPassword { get; set; }
    public string? FtpFolder { get; set; }
    public string? FileName { get; set; }
    public bool? UseSsl { get; set; }
    public bool? UseSftp { get; set; }
    public int? Port { get; set; }
    public bool? ConvertRarToZip { get; set; }
}

public class DownloadResponse
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("success")]
    public bool Success { get; set; } = false;
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;
    [JsonPropertyName("base64Content")]
    public string Base64Content { get; set; } = string.Empty;
}