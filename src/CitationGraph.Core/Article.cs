namespace CitationGraph.Core
{
    // One article from the JSON file. Each article becomes one node in the graph.
    public sealed class Article
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "Başlıksız";
        public int Year { get; set; }
        public List<string> Authors { get; set; } = new();
        public List<string> ReferencedWorkIds { get; set; } = new();

        // Short form of an OpenAlex id, used in the UI.
        // Example: "https://openalex.org/W2117539524" -> "W2117539524"
        public static string ShortId(string? fullId)
        {
            if (string.IsNullOrWhiteSpace(fullId))
                return "-";

            int slash = fullId.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < fullId.Length)
                return fullId.Substring(slash + 1);

            return fullId;
        }

        public override string ToString() =>
            $"[{Id}] {Title} ({Year}) - {Authors.Count} authors, {ReferencedWorkIds.Count} references";
    }
}
