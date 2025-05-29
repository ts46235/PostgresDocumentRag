using System.Text;
using UglyToad.PdfPig;

namespace RagAi.Services;

public static class DocumentTextReader
{
    public static string ExtractTextFromPdf(string pdfPath)
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