using System.Net;
using System.Text.Json;
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
            string remotePath = $"ftp://{payload.FtpHost.TrimEnd('/')}/{ftpFolder.Trim('/')}/{payload.FileName}"
                .Replace("//", "/").Replace("ftp:/", "ftp://");

            byte[] memoryStream;
            if (payload.UseSFTP ?? false)
                memoryStream = await Sftp(payload, remotePath);
            else
                memoryStream = await Ftp(payload, ftpFolder);

            string base64Content = Convert.ToBase64String(memoryStream.ToArray());

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new { fileName = payload.FileName, base64Content });
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar el fichero por FTP.");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync($"Error al descargar el fichero por FTP: {ex.Message}");
            return error;
        }
    }

    private static async Task<byte[]> Ftp(DownloadPayload payload, string remotePath)
    {
        var request = (FtpWebRequest)WebRequest.Create(remotePath);
        request.Method = WebRequestMethods.Ftp.DownloadFile;
        request.Credentials = new NetworkCredential(payload.FtpUser, payload.FtpPassword);
        request.UsePassive = true;
        request.UseBinary = true;
        request.EnableSsl = payload.UseSsl;
        using var response = (FtpWebResponse)await request.GetResponseAsync();
        using var responseStream = response.GetResponseStream();
        using var memoryStream = new MemoryStream();
        await responseStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    private static async Task<byte[]> Sftp(DownloadPayload payload, string ftpFolder)
    {
        using var client = new SftpClient(payload.FtpHost, payload.FtpUser, payload.FtpPassword);
        client.Connect();
        using var memoryStream = new MemoryStream();
        await client.DownloadAsync($"{ftpFolder}/{payload.FileName}", memoryStream);
        client.Disconnect();
        return memoryStream.ToArray();
    }
}

public class DownloadPayload
{
    public string? FtpHost { get; set; }
    public string? FtpUser { get; set; }
    public string? FtpPassword { get; set; }
    public string? FtpFolder { get; set; }
    public string? FileName { get; set; }
    public bool UseSsl { get; set; }
    public bool? UseSFTP { get; set; }
}