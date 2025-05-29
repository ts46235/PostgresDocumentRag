using Npgsql;

namespace RagAi;
public class SqlHelper
{
    public static string ConnectionString = "Host=localhost;Port=5012;Username=postgres;Password=postgres;Database=rag";

    void CreateDatabaseIfNotExists(string connectionString, string databaseName)
    {
        // Connect to the 'postgres' database to check if 'rag' exists
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres" // Connect to default database first
        };
    
        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();
    
        // Check if database exists
        var checkCmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @dbname", 
            connection);
        checkCmd.Parameters.AddWithValue("dbname", databaseName);
    
        var exists = checkCmd.ExecuteScalar() != null;
    
        if (!exists)
        {
            // Create the database
            var createCmd = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
            createCmd.ExecuteNonQuery();
            Console.WriteLine($"Database '{databaseName}' created successfully.");
        }
        else
        {
            Console.WriteLine($"Database '{databaseName}' already exists.");
        }
    }
    
    public static void CreateEmbeddingsTableIfNotExists()
    {
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
            connection.Open();

            string createTableQuery =
                """
                        CREATE TABLE IF NOT EXISTS embeddings (
                            id SERIAL PRIMARY KEY,
                            document_id UUID NOT NULL,
                            collection VARCHAR(100) NOT NULL DEFAULT 'default',
                            text TEXT,
                            embedding VECTOR(1536),
                            metadata JSONB,
                            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                        );
                """;

            using (var command = new NpgsqlCommand(createTableQuery, connection))
            {
                command.ExecuteNonQuery();
                Console.WriteLine("Table 'embeddings' created successfully.");
            }
        }
    }
    
    // Insert embedding into PostgreSQL
    public static async Task InsertEmbedding(Guid documentId, string collection, 
        ReadOnlyMemory<float> embedding, string text, string metadataJson)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var query = @"
        INSERT INTO embeddings (document_id, collection, text, embedding, metadata)
        VALUES (@document_id, @collection, @text, @embedding, @metadata::jsonb);";

        using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("document_id", documentId);
        command.Parameters.AddWithValue("collection", collection);
        command.Parameters.AddWithValue("text", text);
        command.Parameters.AddWithValue("embedding", embedding.ToArray());
        command.Parameters.AddWithValue("metadata", metadataJson);

        await command.ExecuteNonQueryAsync();
        Console.WriteLine("Embedding inserted successfully.");
    }
    
    public static async Task<List<(string DocumentId, string text, float[] Embedding, string MetadataJson, double Similarity)>> SearchEmbeddings(
        string collection,
        ReadOnlyMemory<float> queryEmbedding, 
        double minRelevanceScore, 
        int limit, 
        bool retrieveEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var query =
            """
                SELECT document_id, text, embedding, metadata,
                       1 - (embedding <=> @embedding::vector) as similarity
                FROM embeddings
                WHERE collection = @collection
                AND 1 - (embedding <=> @embedding::vector) > @minRelevanceScore
                ORDER BY similarity DESC
                LIMIT @limit
            """;

        using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("collection", collection);
        command.Parameters.AddWithValue("embedding", queryEmbedding.ToArray());
        command.Parameters.AddWithValue("minRelevanceScore", minRelevanceScore);
        command.Parameters.AddWithValue("limit", limit);

        var results = new List<(string DocumentId, string text, float[] Embedding, string MetadataJson, double Similarity)>();
        
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var docId = reader.GetGuid(0).ToString();
            var text = reader.GetString(1);
            var docEmbedding = retrieveEmbeddings ? reader.GetFieldValue<float[]>(2) : [];
            var metadataJson = reader.GetString(3);
            var relevance = reader.GetDouble(4);

            results.Add((docId, text, docEmbedding, metadataJson, relevance));
        }

        return results;
    }
    
    public static async Task DeleteEmbeddings()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        string deleteQuery = "DELETE FROM embeddings";

        using var command = new NpgsqlCommand(deleteQuery, connection);
        int rowsDeleted = await command.ExecuteNonQueryAsync();
        Console.WriteLine($"Cleared {rowsDeleted} embedding records from the database.");
    }
}