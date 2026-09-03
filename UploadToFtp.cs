using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FtpBridgeFunction;

public class UploadToFtp
{
    private readonly ILogger<UploadToFtp> _logger;

    public UploadToFtp(ILogger<UploadToFtp> logger)
    {
        _logger = logger;
    }

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

        string? ftpHost = payload.FtpHost;
        string? ftpUser = payload.FtpUser;
        string? ftpPassword = payload.FtpPassword;
        string? ftpFolder = payload.DestinyFolder;

        try
        {
            byte[] fileBytes = Convert.FromBase64String(payload.Base64Content);

            string remotePath = $"ftp://{ftpHost.TrimEnd('/')}";
            // Puerto explícito.
            if (payload.Port != null)
                remotePath += $":{payload.Port}";
            // Directorio desde raíz.
            if (!string.IsNullOrEmpty(ftpFolder))
                remotePath += $"/{ftpFolder.Trim('/')}";

            remotePath += $"/{payload.FileName}".Replace("//", "/").Replace("ftp:/", "ftp://");

            var request = (FtpWebRequest)WebRequest.Create(remotePath);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(ftpUser, ftpPassword);
            request.UsePassive = true;
            request.UseBinary = true;
            request.EnableSsl = payload.UseSsl ?? false;

            using (var requestStream = await request.GetRequestStreamAsync())
            {
                await requestStream.WriteAsync(fileBytes, 0, fileBytes.Length);
            }

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            _logger.LogInformation("Subida completada. Estado FTP: {Status}", response.StatusDescription);

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteStringAsync($"Fichero '{payload.FileName}' subido correctamente. Estado FTP: {response.StatusDescription}");
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
}
