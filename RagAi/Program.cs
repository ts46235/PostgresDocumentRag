
using Microsoft.Extensions.Configuration;
using RagAi;
using RagAi.Services;

await RunAsync();

async Task RunAsync()
{
    var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<Program>()
            .Build();

    //IMemoryStore implementation
    // EmbeddingsService.BuildKernel();
    // await SqlHelper.DeleteEmbeddings();
    // await EmbeddingsService.ProcessDocuments("~/Downloads/Resumes");

    // IVectorStore Implementation
    DbHelper.CreateResumeTableIfNotExists();
    await DbHelper.DeleteResumeEmbeddings();
    await VectorService.BuildKernel(config);
    await VectorService.ImportResumes("Assets/Resumes");
    
    while (true)
    {
        Console.Write("\nEnter your search query: ");
        string? query = Console.ReadLine();
        
        // Check for exit condition
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Exiting search system. Goodbye!");
            break;
        }
        
        // Process the search query
        Console.WriteLine("Searching...\n");
        //await EmbeddingsService.QueryMemory(query);
        await VectorService.GetResponseAsync(query);
    }
    
//Show Persons name, relevance and up to a 10 word snippet from each resume that has the word nurse in them
//Show Persons name and up to ten words from each resumes of people who have a certificate
//Show Persons name and short description and up to a 10 word snippet from each resumes of people who have worked in the auto industry
//Show Persons name and short description and up to 10 words in context of those who have worked in the auto industry
//Show a description and up to a 10 word snippet from each resumes of people who can speak spanish

    
//resumes that have nurse in them
//resumes of people who have a certificate
//resumes of people who have worked in the auto industry
//resumes of people who can speak spanish
}