namespace FinanceCore.Infrastructure.FileIngestion;

public class FileIngestionOptions
{
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    public string InputDirectory { get; set; } = "./data/input";
    public string ProcessedDirectory { get; set; } = "./data/processed";
    public string ErrorDirectory { get; set; } = "./data/error";
    public string[] SupportedExtensions { get; set; } = [".csv", ".xlsx"];
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
    public string FileNamePattern { get; set; } = @"^[A-Za-z0-9._-]+$";
}
