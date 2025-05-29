using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.SemanticKernel.Memory;
using Npgsql;

namespace RagAi;
#pragma warning disable SKEXP0001, SKEXP0003, SKEXP0010, SKEXP0050

public class PostgresMemoryStore : IMemoryStore
{
    private readonly string _connectionString;

    public PostgresMemoryStore(string connectionString)
    {
        _connectionString = connectionString;
        SqlHelper.CreateEmbeddingsTableIfNotExists();
    }

    public async Task<string> SaveInformationAsync(string collection, string key, string text, ReadOnlyMemory<float> embedding, string? metadata = null, CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrEmpty(key) ? Guid.NewGuid() : Guid.Parse(key);
        var metadataJson = metadata ?? JsonSerializer.Serialize(new { text });

        await SqlHelper.InsertEmbedding(id, collection, embedding, text, metadataJson);
        return id.ToString();
    }

    public async Task<IList<MemoryQueryResult>> SearchAsync(
        string collection, 
        ReadOnlyMemory<float> embedding, 
        int limit = 1, 
        double minRelevanceScore = 0.7, 
        bool withEmbeddings = false, 
        CancellationToken cancellationToken = default)
    {
        var searchResults = await SqlHelper.SearchEmbeddings(
            collection,
            embedding, 
            minRelevanceScore, 
            limit, 
            withEmbeddings, 
            cancellationToken);

        var results = new List<MemoryQueryResult>();
        foreach (var (docId, text, docEmbedding, metadataJson, relevance) in searchResults)
        {
            var memoryRecord = MemoryRecord.LocalRecord(
                id: docId,
                text: text, 
                embedding: docEmbedding,
                description: ExtractFromMetadata(metadataJson, "fileName"),
                additionalMetadata: metadataJson);

            results.Add(new MemoryQueryResult(
                memoryRecord.Metadata,
                relevance,
                withEmbeddings ? docEmbedding : null
            ));
        }

        return results;
    }
    
    private string ExtractFromMetadata(string json, string propertyName = "text")
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var textElement))
            {
                return textElement.GetString() ?? "";
            }
        }
        catch { }
        return "";
    }
    
    public async Task<string> UpsertAsync(
        string collectionName, 
        MemoryRecord record,
        CancellationToken cancellationToken = default)
    {
        // this needs to support updates. if candidate wants to update their resume on file instead of have 2+ resumes in the system. To support you would need to start storing a unique identifier somehow. It appears metadata is the actual pdf document embedding so not sure where that could go.
        return await SaveInformationAsync(
            collectionName,
            record.Metadata.Id,
            record.Metadata.Text,
            record.Embedding,
            record.Metadata.AdditionalMetadata,
            cancellationToken);
    }
    
    public async IAsyncEnumerable<string> UpsertBatchAsync(
        string collectionName, 
        IEnumerable<MemoryRecord> records,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            var id = string.IsNullOrEmpty(record.Metadata.Id) 
                ? Guid.NewGuid().ToString() 
                : record.Metadata.Id;
            
            await SqlHelper.InsertEmbedding(
                Guid.Parse(id),
                collectionName,
                record.Embedding, 
                record.Metadata.Text,
                record.Metadata.AdditionalMetadata);
            
            yield return id;
        }
    }

    public Task<MemoryRecord?> GetAsync(string collection, string key, bool withEmbedding = false, CancellationToken cancellationToken = default)
    {
        // Implementation would retrieve a specific record by key
        return Task.FromResult<MemoryRecord?>(null);
    }

    public async IAsyncEnumerable<MemoryRecord> GetBatchAsync(
    string collectionName, 
    IEnumerable<string> keys, 
    bool withEmbeddings = false,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    foreach (var key in keys)
    {
        var record = await GetAsync(collectionName, key, withEmbeddings, cancellationToken);
        if (record != null)
        {
            yield return record;
        }
    }
}

    public async Task RemoveAsync(string collectionName, string key,
        CancellationToken cancellationToken = new CancellationToken())
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM embeddings WHERE id = @id";
        command.Parameters.AddWithValue("@id", Guid.Parse(key));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveBatchAsync(
        string collectionName, 
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            await RemoveAsync(collectionName, key, cancellationToken);
        }
    }

    public async IAsyncEnumerable<(MemoryRecord, double)> GetNearestMatchesAsync(
        string collectionName, 
        ReadOnlyMemory<float> embedding, 
        int limit,
        double minRelevanceScore = 0, 
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var searchResults = await SqlHelper.SearchEmbeddings(
            collectionName,
            embedding,
            minRelevanceScore,
            limit,
            withEmbeddings,
            cancellationToken);

        foreach (var (docId, text, docEmbedding, metadataJson, relevance) in searchResults)
        {
            var memoryRecord = MemoryRecord.LocalRecord(
                id: docId,
                text: text,
                embedding: withEmbeddings ? docEmbedding : null,
                description: ExtractFromMetadata(metadataJson, "fileName"),
                additionalMetadata: metadataJson);

            yield return (memoryRecord, relevance);
        }
    }

    public async Task<(MemoryRecord, double)?> GetNearestMatchAsync(
        string collectionName, 
        ReadOnlyMemory<float> embedding, 
        double minRelevanceScore = 0,
        bool withEmbedding = false, 
        CancellationToken cancellationToken = default)
    {
        await using var enumerator = GetNearestMatchesAsync(
            collectionName,
            embedding,
            limit: 1,
            minRelevanceScore,
            withEmbedding,
            cancellationToken).GetAsyncEnumerator(cancellationToken);
        
        if (await enumerator.MoveNextAsync())
        {
            return enumerator.Current;
        }
    
        return null;
    }

    public async Task DeleteCollectionAsync(string collectionName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM embeddings WHERE collection = @collection";
        command.Parameters.AddWithValue("@collection", collectionName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = new CancellationToken())
    {
        // Since collections are just logical partitions in our table,
        // creating a collection doesn't require any database operation
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> GetCollectionsAsync([EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT collection FROM embeddings";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return reader.GetString(0);
        }
    }

    public async Task<bool> DoesCollectionExistAsync(string collectionName,
        CancellationToken cancellationToken = new CancellationToken())
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM embeddings WHERE collection = @collection LIMIT 1)";
        command.Parameters.AddWithValue("@collection", collectionName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
#pragma warning restore SKEXP0001, SKEXP0003, SKEXP0010, SKEXP0050

