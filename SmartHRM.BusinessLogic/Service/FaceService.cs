using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SmartHRM.BusinessLogic;

public class FaceService : IFaceService
{
    private readonly HttpClient _httpClient;

    public FaceService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<byte[]> GetMasterEmbeddingAsync(List<string> base64Images)
    {
        if (base64Images == null || base64Images.Count == 0)
        {
            throw new ArgumentException("No facial biometric samples provided.", nameof(base64Images));
        }

        // 1. Strip data:image/...;base64, prefixes if present
        var cleanedImages = base64Images.Select(img =>
        {
            var commaIndex = img.IndexOf(',');
            return commaIndex >= 0 ? img[(commaIndex + 1)..] : img;
        }).ToList();

        // 2. Post to Python extraction endpoint
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("http://127.0.0.1:8000/extract-embedding", new { images = cleanedImages });
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Cannot connect to AI Face Service at http://127.0.0.1:8000. Ensure python main.py is running. Details: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorDetails = await response.Content.ReadAsStringAsync();
            throw new Exception($"Face extraction failed ({response.StatusCode}): {errorDetails}");
        }

        // 3. Parse 512-d float array
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        var vectorFloats = json?["embedding"]?.AsArray()
            .Select(x => (float)x!.AsValue())
            .ToArray();

        if (vectorFloats == null || vectorFloats.Length == 0)
        {
            throw new Exception("AI service returned an empty or invalid embedding vector.");
        }

        // 4. Convert float[] into byte[] for SQL Server VARBINARY storage
        var bytes = new byte[vectorFloats.Length * sizeof(float)];
        Buffer.BlockCopy(vectorFloats, 0, bytes, 0, bytes.Length);

        // 5. Notify Python service to reload embeddings into its memory cache
        _ = Task.Run(async () =>
        {
            try
            {
                await _httpClient.PostAsync("http://127.0.0.1:8000/reload-embeddings", null);
            }
            catch
            {
                // Non-blocking background sync
            }
        });

        return bytes;
    }
}