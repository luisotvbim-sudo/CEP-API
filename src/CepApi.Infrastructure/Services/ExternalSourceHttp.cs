using CepApi.Application;

namespace CepApi.Infrastructure.Services;

internal static class ExternalSourceHttp
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request,
        string codePrefix, string sourceName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode) return response;
            response.Dispose();
            throw new ExternalDirectoryException($"{codePrefix}_http_error", $"{sourceName} returned an unsuccessful response.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ExternalDirectoryException($"{codePrefix}_unreachable", $"{sourceName} could not be reached.", exception);
        }
    }
}
