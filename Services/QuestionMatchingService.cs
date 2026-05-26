using ChatbotApi.Models;

namespace ChatbotApi.Services;

public interface IQuestionMatchingService
{
    (QnAPair? Match, double Score) FindBestMatch(string userQuestion, IEnumerable<QnAPair> qnaPairs);
    //string GetFallbackMessage();
}

public class QuestionMatchingService : IQuestionMatchingService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","is","are","was","were","be","been","being","have","has","had",
        "do","does","did","will","would","could","should","may","might","shall",
        "i","me","my","we","our","you","your","it","its","this","that","these","those",
        "what","how","when","where","why","who","which","can","please","tell","about"
    };

    public (QnAPair? Match, double Score) FindBestMatch(string userQuestion, IEnumerable<QnAPair> qnaPairs)
    {
        if (string.IsNullOrWhiteSpace(userQuestion)) return (null, 0);

        var activeQna = qnaPairs.Where(q => q.IsActive).ToList();
        if (!activeQna.Any()) return (null, 0);

        var questionTokens = Tokenize(userQuestion);
        if (!questionTokens.Any()) return (null, 0);

        QnAPair? bestMatch = null;
        double bestScore = 0;

        foreach (var pair in activeQna)
        {
            double score = CalculateScore(userQuestion, questionTokens, pair.Question);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = pair;
            }
        }

        // Require a minimum confidence threshold of 0.2
        return bestScore >= 0.2 ? (bestMatch, bestScore) : (null, bestScore);
    }

    //public string GetFallbackMessage() =>
    //    "I'm sorry, I don't understand your question. Please contact our support team for assistance.";

    private double CalculateScore(string rawQuestion, HashSet<string> questionTokens, string storedQuestion)
    {
        double score = 0;

        // 1. Exact match (case-insensitive) — highest weight
        if (string.Equals(rawQuestion.Trim(), storedQuestion.Trim(), StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // 2. Contains full question
        if (storedQuestion.Contains(rawQuestion, StringComparison.OrdinalIgnoreCase))
            score += 0.8;

        var storedTokens = Tokenize(storedQuestion);
        if (!storedTokens.Any()) return score;

        // 3. Token overlap: Jaccard similarity
        var intersection = questionTokens.Intersect(storedTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = questionTokens.Union(storedTokens, StringComparer.OrdinalIgnoreCase).Count();
        double jaccard = union > 0 ? (double)intersection / union : 0;
        score += jaccard * 0.6;

        // 4. Keyword coverage: how many of the user's keywords appear in stored question
        double coverage = questionTokens.Count > 0
            ? (double)questionTokens.Count(t => storedTokens.Contains(t, StringComparer.OrdinalIgnoreCase)) / questionTokens.Count
            : 0;
        score += coverage * 0.4;

        return Math.Min(score, 1.0);
    }

    private HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '?', '!', ';', ':', '-', '_' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
