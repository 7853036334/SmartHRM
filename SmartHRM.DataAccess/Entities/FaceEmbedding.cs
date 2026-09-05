namespace SmartHRM.DataAccess.Entities;

public class FaceEmbedding
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public byte[] EmbeddingVector { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Employee Employee { get; set; } = null!;
}
