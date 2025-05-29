using Microsoft.Extensions.VectorData;

namespace RagAi.Models;

public class Resume
{
    [VectorStoreRecordKey(StoragePropertyName = "id")]
    public long Id { get; set; }

    [VectorStoreRecordData(IsFilterable = true, StoragePropertyName = "filename")]
    public string FileName { get; set; } = string.Empty;

    [VectorStoreRecordData(IsFullTextSearchable = true, StoragePropertyName = "content")]
    public string Content { get; set; } = string.Empty;

    [VectorStoreRecordVector(Dimensions: 1536, DistanceFunction.CosineSimilarity, IndexKind.Hnsw, StoragePropertyName = "content_embedding")]
    public ReadOnlyMemory<float>? ContentEmbedding { get; set; }

    [VectorStoreRecordData(IsFilterable = true, StoragePropertyName = "tags")]
    public string[] Tags { get; set; } = [];
}