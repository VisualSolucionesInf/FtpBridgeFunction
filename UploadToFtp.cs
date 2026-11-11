using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Async;
using System.Net;
using System.Text.Json;

namespace FtpBridgeFunction;

public class UploadToFtp(ILogger<UploadToFtp> logger)
{
    private readonly ILogger<UploadToFtp> _logger = logger;

    [Function("UploadToFtp")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Recibida peticion de subida a FTP.");

        UploadPayload? payload;
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            payload = JsonSerializer.Deserialize<UploadPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al parsear el body recibido.");
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync("Error al parsear el body recibido.");
            return badReq;
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Base64Content)
            || string.IsNullOrWhiteSpace(payload.FileName)
            || string.IsNullOrWhiteSpace(payload.FtpHost)
            || string.IsNullOrWhiteSpace(payload.FtpUser)
            || string.IsNullOrWhiteSpace(payload.FtpPassword))
        {
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync("Falta alguno de los campos obligatorios:" +
                " 'base64Content', 'fileName', 'ftpHost', 'ftpUser', 'ftpPassword'.");
            return badReq;
        }

        try
        {
            byte[] fileBytes = Convert.FromBase64String(payload.Base64Content);

            string remotePath = $"ftp://{payload.FtpHost.TrimEnd('/')}";
            // Puerto explícito.
            if (payload.Port != null)
                remotePath += $":{payload.Port}";
            // Directorio desde raíz.
            if (!string.IsNullOrEmpty(payload.DestinyFolder))
                remotePath += $"/{payload.DestinyFolder.Trim('/')}";

            remotePath += $"/{payload.FileName}".Replace("//", "/").Replace("ftp:/", "ftp://");

            if (payload.UseSftp ?? false)
                await ProcessViaSftp(payload, fileBytes, payload.DestinyFolder ?? string.Empty);
            else
                await ProcessViaFtp(payload, fileBytes, remotePath);

            var method = payload.UseSftp switch { true => "SFTP", _ => "" };
            _logger.LogInformation($"Subida completada vía {method}.");
            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteStringAsync($"Fichero '{payload.FileName}' subido correctamente");
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al subir el fichero por FTP.");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync($"Error al subir el fichero por FTP: {ex.Message}");
            return error;
        }
    }

    private static async Task<FtpWebResponse?> ProcessViaFtp(UploadPayload payload, byte[] fileBytes, string remotePath)
    {
        var request = (FtpWebRequest)WebRequest.Create(remotePath);
        request.Method = WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new NetworkCredential(payload.FtpUser, payload.FtpPassword);
        request.UsePassive = true;
        request.UseBinary = true;
        request.EnableSsl = payload.UseSsl ?? false;

        using (var requestStream = await request.GetRequestStreamAsync())
        {
            await requestStream.WriteAsync(fileBytes, 0, fileBytes.Length);
        }

        using var response = (FtpWebResponse)await request.GetResponseAsync();
        return response;
    }

    private static async Task ProcessViaSftp(UploadPayload payload, byte[] fileBytes, string ftpFolder)
    {
        using var client = new SftpClient(payload.FtpHost, payload.FtpUser, payload.FtpPassword);
        client.Connect();
        using var stream = new MemoryStream(fileBytes);
        await client.UploadAsync(stream, $"{ftpFolder}/{payload.FileName}");
        client.Disconnect();
    }
}

public class UploadPayload
{
    public string? Base64Content { get; set; }
    public string? FileName { get; set; }
    public string? FtpHost { get; set; }
    public string? FtpUser { get; set; }
    public string? FtpPassword { get; set; }
    public string? DestinyFolder { get; set; }
    public bool? UseSsl { get; set; }
    public int? Port { get; set; }
    public bool? UseSftp { get; set; }
}
