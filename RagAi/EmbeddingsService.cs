using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;
using Npgsql;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Embeddings;
using UglyToad.PdfPig;
using Microsoft.SemanticKernel.Plugins.Memory;

namespace RagAi;
#pragma warning disable SKEXP0001, SKEXP0003, SKEXP0010, SKEXP0050

public class EmbeddingsService
{
    private static Kernel? _kernel;

    public static async Task ProcessDocuments(string pdfDirectory)
    {
        // Set up kernel with memory integration
        var memory = _kernel!.GetRequiredService<ISemanticTextMemory>();
        
        string absolutePath = Path.GetFullPath(pdfDirectory.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        foreach (var path in Directory.GetFiles(absolutePath, "*.pdf"))
        {
            string fileName = Path.GetFileName(path);
            Console.WriteLine($"Processing {fileName}");

            // Extract text from PDF
            string extractedText = ExtractTextFromPdf(path);

            // Use SK's built-in text chunker
            var chunks = TextChunker.SplitPlainTextLines(
                extractedText,
                maxTokensPerLine: 1000);

            Console.WriteLine($"Split into {chunks.Count} chunks");

            // Store chunks in memory
            for (int i = 0; i < chunks.Count; i++)
            {
                await memory.SaveInformationAsync(
                    collection: "resumes",
                    id: Guid.NewGuid().ToString(), // TODO: needs to come from hash of user identity
                    text: chunks[i],
                    description: $"From {fileName}, chunk {i+1}/{chunks.Count}",
                    additionalMetadata: $"{{ \"fileName\": \"{fileName}\" }}");

                Console.WriteLine($"Processed chunk {i+1}/{chunks.Count}");
            }
        }
    }

    public static async Task QueryMemory(string question)
    {
        // Create prompt that uses memory recall, the text following `recall` are the params to SearchAsync
        const string promptTemplate = """
                                      Answer the question based on the following context.

                                      Context:
                                      {{memory.Recall input=$question limit="10" relevance="0.75" collection="resumes"}}

                                      Question: {{$question}}
                                      """;
        try
        {
            var result = await _kernel.InvokePromptAsync(
                promptTemplate, 
                new KernelArguments() { {"question", question }});

            Console.WriteLine(result);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("The request timed out. This might be due to high server load or the complexity of your query.");
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
    
    public static void BuildKernel()
    {
        if (_kernel != null)
        {
            return;
        }
        
        var builder = Kernel.CreateBuilder();

        // Add Azure OpenAI services
        builder.AddAzureOpenAITextEmbeddingGeneration(
            "text-embedding-ada-002",
            "<your azure openAi-endpoint>",
            "<your apikey>");

        builder.AddAzureOpenAIChatCompletion(
            "gpt-35-turbo", // these expire soon; check ai foundry to see which available
            "<your azure openAi-endpoint>",
            "<your apikey>");

        // Register custom PostgreSQL memory store
        builder.Services.AddSingleton<IMemoryStore>(_ => new PostgresMemoryStore(SqlHelper.ConnectionString));
        
        builder.Services.AddSingleton<ISemanticTextMemory>(sp =>
            new SemanticTextMemory(
                sp.GetRequiredService<IMemoryStore>(), 
                sp.GetRequiredService<ITextEmbeddingGenerationService>()
            )
        );
        _kernel = builder.Build();
        var memory = _kernel!.GetRequiredService<ISemanticTextMemory>();

        // Import memory plugin for recall
        // This IS being used - the kernel needs the plugin registered to use the recall function
        _kernel.ImportPluginFromObject(new TextMemoryPlugin(memory), "memory");
    }

    private static string ExtractTextFromPdf(string pdfPath)
    {
        using var pdf = PdfDocument.Open(pdfPath);
        var text = new StringBuilder();

        foreach (var page in pdf.GetPages())
        {
            // Get words and completely remove any that contain null characters
            var words = page.GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text) && !w.Text.Contains('\0'))
                .Select(w => w.Text.Trim());

            string pageText = string.Join(" ", words);
            text.AppendLine(pageText);
        }

        return text.ToString();
    }
}
#pragma warning restore SKEXP0001, SKEXP0003, SKEXP0010, SKEXP0050
