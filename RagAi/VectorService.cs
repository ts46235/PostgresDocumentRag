using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.Postgres;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Text;
using Npgsql;
using RagAi.Models;
using RagAi.Services;

namespace RagAi;
#pragma warning disable SKEXP0001, SKEXP0003, SKEXP0010, SKEXP0020, SKEXP0050

public class VectorService
{
    private static Kernel? _kernel;
    private static IVectorStoreRecordCollection<long, Resume>? _resumeCollection;
    
    public static async Task ImportResumes(string pdfDirectory)
    {
        string absolutePath = Path.GetFullPath(pdfDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        foreach (var path in Directory.GetFiles(absolutePath, "*.pdf"))
        {
            string fileName = Path.GetFileName(path);
            Console.WriteLine($"Processing {fileName}");

            string extractedText = DocumentTextReader.ExtractTextFromPdf(path);

            // Use SK's built-in text chunker
            var chunks = TextChunker.SplitPlainTextLines(
                extractedText,
                maxTokensPerLine: 1000);

            Console.WriteLine($"Split into {chunks.Count} chunks");   // is always 1??????

            // Store chunks in memory
            for (int i = 0; i < chunks.Count; i++)
            {
                await GenerateEmbeddingsAndUpsertAsync(fileName, chunks[i]);
                // await memory.SaveInformationAsync(
                //     collection: "resumes",
                //     id: Guid.NewGuid().ToString(), // TODO: needs to come from hash of user identity
                //     text: chunks[i],
                //     description: $"From {fileName}, chunk {i+1}/{chunks.Count}",
                //     additionalMetadata: $"{{ \"fileName\": \"{fileName}\" }}");

                Console.WriteLine($"Processed chunk {i+1}/{chunks.Count}");
            }
        }
    }
    
    public static async Task GenerateEmbeddingsAndUpsertAsync(string fileName, string text, string[]? tags = null)
    {
        var generationService = _kernel!.GetRequiredService<ITextEmbeddingGenerationService>();
        ReadOnlyMemory<float> embedding = await generationService.GenerateEmbeddingAsync(text);

        var id = (long)Guid.NewGuid().GetHashCode(); // TODO: needs to come from hash of user identity i.e. email or an existing id
        
        // Create a record and upsert with the already generated embedding.
        await _resumeCollection.UpsertAsync(new Resume
        {
            Id = id,
            FileName = fileName,
            Content = text,
            ContentEmbedding = embedding,
            Tags = tags ?? []
        });
        
        Resume? resume = await _resumeCollection.GetAsync(id);
        Console.WriteLine($"Upserted {resume?.FileName}");
        
        // ReadOnlyMemory<float> nurseEmbedding = await generationService.GenerateEmbeddingAsync("nurse");
        // ReadOnlyMemory<float> nursingEmbedding = await generationService.GenerateEmbeddingAsync("nursing");
        // var similarity = CosineSimilarity(nurseEmbedding, nursingEmbedding);
        // Console.WriteLine($"Cosine Similarity: {similarity}");
    }
    
    public static async Task<List<VectorSearchResult<Resume>>> RetrieveContextAsync(string userQuery)
    {
        // pre-process the user query to get the actual search query to that vector store search is precise
        var actualQuestion = await _kernel!.InvokePromptAsync($"Return just the actual search query to use to search a separate vector store without any search instructions from: {userQuery}", new KernelArguments());
        
        var textEmbeddingGenerationService = _kernel!.GetRequiredService<ITextEmbeddingGenerationService>();
        ReadOnlyMemory<float> searchEmbedding =
            await textEmbeddingGenerationService.GenerateEmbeddingAsync(actualQuestion.ToString());

        VectorSearchResults<Resume> searchResult = 
            await _resumeCollection!.VectorizedSearchAsync(searchEmbedding, new() { Top = 5});
        List<VectorSearchResult<Resume>> results = await searchResult.Results.ToListAsync();

        // Filter results by relevance score, which should never be null in our case (pgvector always calculates similarity scores)
        var filteredResults = results.Where(r => r.Score >= .75).ToList();

        // Print the results
        Console.WriteLine($"Vector store search found {filteredResults.Count} results with relevance score ≥ .75:");
        foreach (var item in filteredResults)
        {
            Console.WriteLine($"Score: {item.Score}, Filename: {item.Record.FileName}");
        }
        Console.WriteLine();
        
        return filteredResults;
    }
    
    public static string ConstructPrompt(string userQuery, List<VectorSearchResult<Resume>> resumes)
    {
        // Build a more detailed context with additional metadata
        var contextBuilder = new System.Text.StringBuilder();

        //for (int i = 0; i < resumes.Count; i++)
        resumes.ForEach(resume =>
        {
            // Include document identifier and metadata
            contextBuilder.AppendLine($"Resume for {resume.Record.FileName}:");
            contextBuilder.AppendLine(resume.Record.Content);

            // Include relevance score to help the model understand confidence
            contextBuilder.AppendLine($"Relevance: {resume.Score!.Value}");

            // Add spacing between document
            contextBuilder.AppendLine();
        });

        // Construct the final prompt
        return $"""
                Answer the question based on the following resumes. If no resume round with relevant information to answer the question, acknowledge this limitation.

                RESUMES:
                {contextBuilder}

                QUESTION:
                {userQuery}

                ANSWER:
                """;
    }

    public static async Task GetResponseAsync(string userQuery)
    {
        // 1. Retrieve context using VectorizedSearchAsync
        var resumes = await RetrieveContextAsync(userQuery);

        // 2. Construct prompt with retrieved context
        var prompt = ConstructPrompt(userQuery, resumes);

        // 3. Generate response
        try
        {
            var settings = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.75,
                MaxTokens = 1000   
            };
            
            var result = await _kernel!.InvokePromptAsync(prompt, new KernelArguments(settings));
            Console.WriteLine(result);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine(
                "The request timed out. This might be due to high server load or the complexity of your query.");
        }
        catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("rate limit"))
        {
            Console.WriteLine("Rate limit exceeded. Please wait a moment and try again.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine("Try simplifying your query or reducing the context window.");
        }
    }

    public static async Task BuildKernel()
    {
        if (_kernel != null)
        {
            return;
        }
        
        var builder = Kernel.CreateBuilder();

        builder.AddAzureOpenAITextEmbeddingGeneration(
            "text-embedding-ada-002",
            "<your azure openAi-endpoint>",
            "<your apikey>");

        builder.AddAzureOpenAIChatCompletion(
            "gpt-4",
            "<your azure openAi-endpoint>",
            "<your apikey>");

        //builder.Services.AddSingleton<IVectorStore>(_ => vectorStore);
        builder.Services.AddPostgresVectorStore(SqlHelper.ConnectionString);

        _kernel = builder.Build();
        _resumeCollection = _kernel!.GetRequiredService<IVectorStore>().GetCollection<long, Resume>("resumes");
        await _resumeCollection.CreateCollectionIfNotExistsAsync();
        
        // builder.Services.AddVectorStoreTextSearch();
        // var textSearch = _kernel!.GetRequiredService<ITextSearchService>();
        // var searchResults = await textSearch.SearchAsync(
        //     "resumes",
        //     userQuery,
        //     minRelevanceScore: 0.7,
        //     limit: 5);
    }

    private static double CosineSimilarity(ReadOnlyMemory<float> vectorA, ReadOnlyMemory<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException("Vectors must be of the same length.");
        }

        double dotProduct = 0.0;
        double magnitudeA = 0.0;
        double magnitudeB = 0.0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA.Span[i] * vectorB.Span[i];
            magnitudeA += Math.Pow(vectorA.Span[i], 2);
            magnitudeB += Math.Pow(vectorB.Span[i], 2);
        }

        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0.0; // Avoid division by zero
        }

        return dotProduct / (magnitudeA * magnitudeB);
    }
}
#pragma warning restore SKEXP0001, SKEXP0003, SKEXP0010, SKEXP0020, SKEXP0050
