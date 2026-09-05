namespace SmartHRM.BusinessLogic;

public interface IFaceService
{
    Task<byte[]> GetMasterEmbeddingAsync(List<string> base64Images);
}