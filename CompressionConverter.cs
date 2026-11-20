using SharpCompress.Common;
using SharpCompress.Readers;
using System.IO.Compression;
using System.Text;

namespace FtpBridgeFunction;

/// <summary>
/// Excepción personalizada para las acciones de conversión entre formatos.
/// </summary>
internal class CompressConvertException : Exception
{
    public CompressConvertException(): base() {}
    public CompressConvertException(string? message) : base(message) { }
    public CompressConvertException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Herramientas para convertir entre formatos RAR y ZIP.
/// </summary>
internal class CompressionConverter
{
    /// <summary>
    /// Convierte RAR a ZIP a través de streams, sin disco.
    /// </summary>
    public static async Task RarToZip(Stream rarStream, Stream zipStream)
    {
        ReaderOptions readerOptions = CrearOpcionesLectura();

        // El ZipArchive debe cerrarse ANTES que zipStream: al hacer Dispose escribe el
        // directorio central del ZIP. leaveOpen:true para que no cierre el stream SFTP.
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        // Reader API de SharpCompress = lectura secuencial (forward-only).
        using IReader reader = ReaderFactory.OpenReader(rarStream, readerOptions);
        while (reader.MoveToNextEntry())
        {
            // Las carpetas van implícitas en la ruta de cada fichero del ZIP
            if (reader.Entry.IsDirectory)
                continue;

            // Normalizar separadores (RAR suele traer "\" y ZIP espera "/")
            string ruta = reader.Entry.Key!.Replace('\\', '/').TrimStart('/');

            // El formato ZIP solo admite fechas 1980..2107; fuera de rango se deja la de por defecto
            ZipArchiveEntry entradaZip = zip.CreateEntry(ruta, CompressionLevel.Optimal);
            if (reader.Entry.LastModifiedTime is DateTime fecha && fecha.Year >= 1980 && fecha.Year <= 2107)
                entradaZip.LastWriteTime = fecha;

            // Copia byte a byte del contenido descomprimido: no se interpreta nada
            await using Stream destino = entradaZip.Open();
            using Stream origen = reader.OpenEntryStream();
            await origen.CopyToAsync(destino);
        }
    }

    /// <summary>
    /// Opciones de lectura de SharpCompress: codificación de nombres de fichero
    /// para RAR4 sin marca Unicode (el contenido nunca se toca).
    /// </summary>
    private static ReaderOptions CrearOpcionesLectura()
    {
        var opciones = new ReaderOptions
        {
            // Si los RAR llevan contraseña: Password = Environment.GetEnvironmentVariable("RarPassword")
        };

        string? nombreEncoding = Environment.GetEnvironmentVariable("RarEncoding");
        if (string.IsNullOrWhiteSpace(nombreEncoding))
            return opciones;

        try
        {
            // Las páginas de códigos clásicas (850, 1252...) requieren registrar el proveedor. Es idempotente.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Encoding encoding = int.TryParse(nombreEncoding, out int codePage)
                ? Encoding.GetEncoding(codePage)
                : Encoding.GetEncoding(nombreEncoding);

            opciones.ArchiveEncoding = new ArchiveEncoding { Default = encoding };
            // _log.LogInformation("Codificación de nombres RAR: {Encoding}", encoding.WebName);
        }
        catch // (Exception ex)
        {
            // _log.LogWarning(ex, "RarEncoding '{Valor}' no válido; se usa la codificación por defecto", nombreEncoding);
        }

        return opciones;
    }
}
