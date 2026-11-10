namespace Pitly.Api.Services;

/// <summary>
/// Size limits of one import request. The same total is configured in the Nginx that fronts the API in Docker
/// (client_max_body_size in frontend/nginx.conf and docker/frontend.nginx.conf); a test keeps the three in step.
/// Without that, Nginx would reject with its 1 MiB default what the API accepts.
/// </summary>
public static class UploadLimits
{
    private const long MiB = 1024 * 1024;

    /// <summary>Largest single statement file.</summary>
    public const long MaxFileBytes = 10 * MiB;

    /// <summary>Largest multipart request as a whole: several years of statements plus the form fields.</summary>
    public const long MaxRequestBytes = 64 * MiB;

    public const int MaxFiles = 60;

    public const int MaxFileMegabytes = (int)(MaxFileBytes / MiB);
    public const int MaxRequestMegabytes = (int)(MaxRequestBytes / MiB);
}
