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
            await badReq.WriteStringAsync("Body invalido. Se esperaba JSON con 'base64Content' y 'fileName'.");
            return badReq;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Base64Content) || string.IsNullOrWhiteSpace(payload.FileName))
        {
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteStringAsync("Faltan campos obligatorios: 'base64Content' y 'fileName'.");
            return badReq;
        }

        string? ftpHost = Environment.GetEnvironmentVariable("FTP_HOST");
        string? ftpUser = Environment.GetEnvironmentVariable("FTP_USER");
        string? ftpPassword = Environment.GetEnvironmentVariable("FTP_PASSWORD");
        string? ftpFolder = Environment.GetEnvironmentVariable("FTP_FOLDER") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(ftpHost) || string.IsNullOrWhiteSpace(ftpUser) || string.IsNullOrWhiteSpace(ftpPassword))
        {
            _logger.LogError("Faltan variables de entorno FTP_HOST / FTP_USER / FTP_PASSWORD.");
            var cfgError = req.CreateResponse(HttpStatusCode.InternalServerError);
            await cfgError.WriteStringAsync("Configuracion FTP incompleta en el servidor (variables de entorno).");
            return cfgError;
        }

        try
        {
            byte[] fileBytes = Convert.FromBase64String(payload.Base64Content);

            string remotePath = $"ftp://{ftpHost.TrimEnd('/')}/{ftpFolder.Trim('/')}/{payload.FileName}".Replace("//", "/").Replace("ftp:/", "ftp://");

            var request = (FtpWebRequest)WebRequest.Create(remotePath);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(ftpUser, ftpPassword);
            request.UsePassive = true;
            request.UseBinary = true;
            request.EnableSsl = bool.TryParse(Environment.GetEnvironmentVariable("FTP_USE_SSL"), out var useSsl) && useSsl;

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
}
