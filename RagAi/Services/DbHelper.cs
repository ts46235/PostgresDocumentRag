using Npgsql;

namespace RagAi.Services;

public class DbHelper
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
    
    public static void CreateResumeTableIfNotExists()
    {
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
            connection.Open();

            string createTableQuery =
                """
                CREATE TABLE IF NOT EXISTS public.resumes (
                    id BIGINT PRIMARY KEY,
                    filename TEXT NOT NULL,
                    content TEXT NOT NULL,
                    content_embedding vector(1536) NOT NULL,
                    tags TEXT[] NULL
                );
                
                -- Create filterable index on filename
                CREATE INDEX IF NOT EXISTS idx_resumes_filename ON public.resumes USING btree (filename);
                
                -- Create full text search index on content
                CREATE INDEX IF NOT EXISTS idx_resumes_content_fts ON public.resumes USING gin (to_tsvector('english', content));
                
                -- Create filterable index on tags
                CREATE INDEX IF NOT EXISTS idx_resumes_tags ON public.resumes USING gin (tags);
                
                -- Create vector similarity search index on content_embedding
                CREATE INDEX IF NOT EXISTS idx_resumes_content_embedding ON public.resumes 
                USING hnsw (content_embedding vector_cosine_ops)
                WITH (
                    m = 16,
                    ef_construction = 64
                );
                """;

            using (var command = new NpgsqlCommand(createTableQuery, connection))
            {
                command.ExecuteNonQuery();
                Console.WriteLine("Table 'resumes' created successfully.");
            }
        }
    }
    
    // Insert embedding into PostgreSQL
    public static async Task InsertResume(ulong id, string fileName, string content, 
        ReadOnlyMemory<float> contentEmbedding, string[]? tags = null)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var query = @"
            INSERT INTO public.resumes (id, filename, content, content_embedding, tags)
            VALUES (@id, @filename, @content, @content_embedding, @tags);";

        using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("filename", fileName);
        command.Parameters.AddWithValue("content", content);
        command.Parameters.AddWithValue("content_embedding", contentEmbedding.ToArray());
        command.Parameters.AddWithValue("tags", tags ?? []);

        await command.ExecuteNonQueryAsync();
        Console.WriteLine($"Resume '{fileName}' inserted successfully.");
    }
    
    
    // this needs to change; see chrome example ************
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
    
    public static async Task DeleteResumeEmbeddings()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        string deleteQuery = "DELETE FROM resumes";

        using var command = new NpgsqlCommand(deleteQuery, connection);
        int rowsDeleted = await command.ExecuteNonQueryAsync();
        Console.WriteLine($"Cleared {rowsDeleted} embedding records from the database.");
    }
}