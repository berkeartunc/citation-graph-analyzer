namespace CitationGraph.Core
{
    // Builds a Graph from a list of articles:
    // - every article with an id becomes a node (if an id repeats, the first one is kept)
    // - a citation edge is added for each referenced id that exists in the dataset
    // - sequence edges link the articles in ascending id order
    public sealed class GraphBuilder
    {
        public Graph Build(IReadOnlyList<Article> articles)
        {
            ArgumentNullException.ThrowIfNull(articles);

            // 1) Give every valid id an index
            var idByIndex = new List<string>();
            var articlesByIndex = new List<Article>();
            var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var article in articles)
            {
                if (article == null || string.IsNullOrWhiteSpace(article.Id))
                    continue;

                string id = article.Id.Trim();
                if (!indexById.TryAdd(id, idByIndex.Count))
                    continue; // duplicate id

                idByIndex.Add(id);
                articlesByIndex.Add(article);
            }

            int n = idByIndex.Count;
            if (n == 0)
                throw new InvalidDataException("Geçerli id'ye sahip makale bulunamadı, graf oluşturulamadı.");

            var cites = new List<int>[n];
            var sequenceNext = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                cites[i] = new List<int>();
                sequenceNext[i] = new List<int>();
            }

            // 2) Citation edges. References to articles outside the dataset are ignored.
            // The same reference written twice counts once, and an article citing itself is ignored.
            var seen = new HashSet<int>();
            for (int from = 0; from < n; from++)
            {
                var refs = articlesByIndex[from].ReferencedWorkIds;
                if (refs == null) continue;

                seen.Clear();
                foreach (var refId in refs)
                {
                    if (string.IsNullOrWhiteSpace(refId)) continue;
                    if (!indexById.TryGetValue(refId.Trim(), out int to)) continue;
                    if (to == from || !seen.Add(to)) continue;

                    cites[from].Add(to);
                }
            }

            // 3) Sequence edges: sort the indexes by id, then link each one to the next
            var order = Enumerable.Range(0, n).ToList();
            order.Sort((a, b) => string.CompareOrdinal(idByIndex[a], idByIndex[b]));

            for (int i = 0; i < n - 1; i++)
                sequenceNext[order[i]].Add(order[i + 1]);

            return new Graph(articlesByIndex, idByIndex, indexById, cites, sequenceNext);
        }
    }
}
