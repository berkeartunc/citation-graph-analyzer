namespace CitationGraph.Core
{
    // Directed graph of articles. Nodes are stored by index (0..NodeCount-1).
    // There are two kinds of edges:
    // - citation edges: u -> v means article u cites article v (drawn black in the UI)
    // - sequence edges: each article points to the next one in id order (drawn green)
    // Use GraphBuilder to create a graph from a list of articles.
    public sealed class Graph
    {
        public IReadOnlyList<Article> ArticlesByIndex { get; }
        public IReadOnlyList<string> IdByIndex { get; }

        // Full id -> index. Lookup ignores case.
        public IReadOnlyDictionary<string, int> IndexById { get; }

        // Cites[u] = articles that u cites (outgoing citation edges)
        public List<int>[] Cites { get; }

        // CitedBy[v] = articles that cite v (incoming citation edges).
        // Built once here, so "who cites this article?" does not need a full scan.
        public List<int>[] CitedBy { get; }

        // SequenceNext[u] = next article in id order (at most one item)
        public List<int>[] SequenceNext { get; }

        public int NodeCount => IdByIndex.Count;
        public int CitationEdgeCount { get; }
        public int SequenceEdgeCount { get; }

        // Short id (for example "W2117539524") -> index, so the user can type the short form
        private readonly Dictionary<string, int> _indexByShortId;

        internal Graph(
            List<Article> articlesByIndex,
            List<string> idByIndex,
            Dictionary<string, int> indexById,
            List<int>[] cites,
            List<int>[] sequenceNext)
        {
            ArticlesByIndex = articlesByIndex;
            IdByIndex = idByIndex;
            IndexById = indexById;
            Cites = cites;
            SequenceNext = sequenceNext;

            int n = idByIndex.Count;

            CitedBy = new List<int>[n];
            for (int i = 0; i < n; i++)
                CitedBy[i] = new List<int>();

            for (int u = 0; u < n; u++)
            {
                foreach (int v in cites[u])
                    CitedBy[v].Add(u);
                CitationEdgeCount += cites[u].Count;
                SequenceEdgeCount += sequenceNext[u].Count;
            }

            _indexByShortId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
                _indexByShortId.TryAdd(Article.ShortId(idByIndex[i]), i);
        }

        // Number of times the article is cited inside the dataset (in-degree).
        public int CitationCount(int index) => CitedBy[index].Count;

        // Number of references the article gives inside the dataset (out-degree).
        public int ReferenceCount(int index) => Cites[index].Count;

        // Finds an article by its full id ("https://openalex.org/W123")
        // or its short id ("W123").
        public bool TryFindIndex(string id, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(id)) return false;

            id = id.Trim();
            return IndexById.TryGetValue(id, out index) || _indexByShortId.TryGetValue(id, out index);
        }
    }
}
