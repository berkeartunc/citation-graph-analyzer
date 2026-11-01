namespace CitationGraph.Core
{
    // Analysis on a Graph: h-index, betweenness centrality and k-core.
    public sealed class GraphAnalytics
    {
        private readonly Graph _graph;

        // Undirected neighbor lists for betweenness and k-core.
        // Both citation and sequence edges are used, and each pair appears once.
        private readonly List<int>[] _undirectedAdj;

        public GraphAnalytics(Graph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _undirectedAdj = BuildUndirectedAdjacency();
        }

        public int NodeCount => _graph.NodeCount;

        // -------------------------------------------------
        // 1) h-index, h-core and h-median
        // -------------------------------------------------

        // Computes the h-index of an article from the articles that cite it.
        // h is the largest number such that h citing articles have at least h citations each.
        // The h-core is those h articles, and the h-median is the median of their citation counts.
        // articleId can be the full id or the short id, for example "W2117539524".
        public HIndexResult ComputeHIndex(string articleId)
        {
            if (string.IsNullOrWhiteSpace(articleId))
                throw new ArgumentException("Makale id boş olamaz.", nameof(articleId));

            if (!_graph.TryFindIndex(articleId, out int target))
                throw new KeyNotFoundException($"'{articleId.Trim()}' id'li makale grafta bulunamadı.");

            string targetId = _graph.IdByIndex[target];

            // Citing articles, sorted by their own citation count (highest first).
            // Ties are sorted by id, so the result is always the same.
            var citing = _graph.CitedBy[target]
                .Select(u => (Index: u, Citations: _graph.CitationCount(u)))
                .OrderByDescending(x => x.Citations)
                .ThenBy(x => _graph.IdByIndex[x.Index], StringComparer.Ordinal)
                .ToList();

            int h = 0;
            while (h < citing.Count && citing[h].Citations >= h + 1)
                h++;

            if (h == 0)
                return new HIndexResult(target, targetId, 0, 0.0, Array.Empty<int>(), Array.Empty<int>());

            var coreIndices = citing.Take(h).Select(x => x.Index).ToList();
            var coreCitations = citing.Take(h).Select(x => x.Citations).ToList();

            // The list is already sorted, so the median is the middle item (or the average of the two middle items)
            double hMedian = (h % 2 == 1)
                ? coreCitations[h / 2]
                : (coreCitations[h / 2 - 1] + coreCitations[h / 2]) / 2.0;

            return new HIndexResult(target, targetId, h, hMedian, coreIndices, coreCitations);
        }

        // -------------------------------------------------
        // 2) Undirected graph (for betweenness and k-core)
        // -------------------------------------------------
        private List<int>[] BuildUndirectedAdjacency()
        {
            int n = _graph.NodeCount;
            var sets = new HashSet<int>[n];
            for (int i = 0; i < n; i++)
                sets[i] = new HashSet<int>();

            void AddEdge(int u, int v)
            {
                if (u == v) return;
                sets[u].Add(v);
                sets[v].Add(u);
            }

            for (int u = 0; u < n; u++)
            {
                foreach (int v in _graph.Cites[u]) AddEdge(u, v);
                foreach (int v in _graph.SequenceNext[u]) AddEdge(u, v);
            }

            var adj = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                adj[i] = new List<int>(sets[i]);
                adj[i].Sort(); // fixed order, so results do not depend on HashSet order
            }

            return adj;
        }

        // -------------------------------------------------
        // 3) Betweenness centrality (Brandes algorithm)
        // -------------------------------------------------

        // For each node, sums the share of shortest paths between other node pairs
        // that pass through it. Runs one BFS per node: O(V * E) time.
        public double[] ComputeBetweennessCentrality()
        {
            int n = NodeCount;
            var bc = new double[n];

            var stack = new Stack<int>();
            var queue = new Queue<int>();
            var pred = new List<int>[n];

            // Number of shortest paths. double instead of int: the count can grow
            // very fast in large graphs and an int would overflow.
            var sigma = new double[n];
            var dist = new int[n];
            var delta = new double[n];

            for (int i = 0; i < n; i++)
                pred[i] = new List<int>();

            for (int s = 0; s < n; s++)
            {
                for (int i = 0; i < n; i++)
                {
                    pred[i].Clear();
                    sigma[i] = 0;
                    dist[i] = -1;
                    delta[i] = 0.0;
                }

                sigma[s] = 1;
                dist[s] = 0;
                queue.Enqueue(s);

                // BFS from s: distances, path counts and predecessors
                while (queue.Count > 0)
                {
                    int v = queue.Dequeue();
                    stack.Push(v);

                    foreach (int w in _undirectedAdj[v])
                    {
                        if (dist[w] < 0)
                        {
                            dist[w] = dist[v] + 1;
                            queue.Enqueue(w);
                        }

                        if (dist[w] == dist[v] + 1)
                        {
                            sigma[w] += sigma[v];
                            pred[w].Add(v);
                        }
                    }
                }

                // Go back from the farthest nodes and add up each node's share
                while (stack.Count > 0)
                {
                    int w = stack.Pop();
                    foreach (int v in pred[w])
                        delta[v] += (sigma[v] / sigma[w]) * (1.0 + delta[w]);

                    if (w != s)
                        bc[w] += delta[w];
                }
            }

            // In an undirected graph every (s, t) pair is counted twice
            for (int i = 0; i < n; i++)
                bc[i] /= 2.0;

            return bc;
        }

        // -------------------------------------------------
        // 4) k-core (on the undirected graph)
        // -------------------------------------------------

        // Returns the nodes of the k-core: the largest subgraph where every node
        // has at least k neighbors. Nodes with fewer than k neighbors are removed
        // one by one until no such node is left.
        public List<int> ComputeKCore(int k)
        {
            if (k < 0)
                throw new ArgumentOutOfRangeException(nameof(k), "k negatif olamaz.");

            int n = NodeCount;
            var degree = new int[n];
            var removed = new bool[n];
            var queue = new Queue<int>();

            for (int i = 0; i < n; i++)
            {
                degree[i] = _undirectedAdj[i].Count;
                if (degree[i] < k)
                {
                    removed[i] = true;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int u in _undirectedAdj[v])
                {
                    if (removed[u]) continue;

                    degree[u]--;
                    if (degree[u] < k)
                    {
                        removed[u] = true;
                        queue.Enqueue(u);
                    }
                }
            }

            var core = new List<int>();
            for (int i = 0; i < n; i++)
                if (!removed[i])
                    core.Add(i);

            return core;
        }
    }
}
