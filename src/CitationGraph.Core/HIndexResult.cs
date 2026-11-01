namespace CitationGraph.Core
{
    // Result of GraphAnalytics.ComputeHIndex for one article.
    public sealed class HIndexResult
    {
        public int TargetIndex { get; }
        public string TargetId { get; }
        public int H { get; }
        public double HMedian { get; }

        // The h-core: graph indexes of the h citing articles with the most citations
        public IReadOnlyList<int> CoreIndices { get; }

        // Citation count of each h-core article (same order as CoreIndices)
        public IReadOnlyList<int> CoreCitations { get; }

        public HIndexResult(int targetIndex, string targetId, int h, double hMedian,
                            IReadOnlyList<int> coreIndices, IReadOnlyList<int> coreCitations)
        {
            TargetIndex = targetIndex;
            TargetId = targetId;
            H = h;
            HMedian = hMedian;
            CoreIndices = coreIndices;
            CoreCitations = coreCitations;
        }
    }
}
