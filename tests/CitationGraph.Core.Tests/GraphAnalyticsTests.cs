using static CitationGraph.Core.Tests.TestGraphs;

namespace CitationGraph.Core.Tests
{
    public class GraphAnalyticsTests
    {
        // T is cited by A, B, C and D.
        // A is cited 3 times, B 2 times, C once and D never.
        // Sorted counts: 3, 2, 1, 0 -> h = 2 (two articles with at least 2 citations).
        private static Graph HIndexGraph() => Build(
            Node("T"),
            Node("A", "T"), Node("B", "T"), Node("C", "T"), Node("D", "T"),
            Node("X1", "A", "B", "C"),
            Node("X2", "A", "B"),
            Node("X3", "A"));

        [Fact]
        public void HIndex_IsComputedFromCitingArticles()
        {
            var g = HIndexGraph();
            var result = new GraphAnalytics(g).ComputeHIndex("T");

            Assert.Equal(2, result.H);
            Assert.Equal(new[] { Index(g, "A"), Index(g, "B") }, result.CoreIndices);
            Assert.Equal(new[] { 3, 2 }, result.CoreCitations);
            Assert.Equal(2.5, result.HMedian);
            Assert.Equal(Index(g, "T"), result.TargetIndex);
        }

        [Fact]
        public void HIndex_MedianIsTheMiddleValueWhenHIsOdd()
        {
            // T is cited by A, B, C. Each of them is cited by X1, X2 and X3, so all have 3 citations -> h = 3.
            var g = Build(
                Node("T"),
                Node("A", "T"), Node("B", "T"), Node("C", "T"),
                Node("X1", "A", "B", "C"), Node("X2", "A", "B", "C"), Node("X3", "A", "B", "C"));

            var result = new GraphAnalytics(g).ComputeHIndex("T");

            Assert.Equal(3, result.H);
            Assert.Equal(3.0, result.HMedian);
        }

        [Fact]
        public void HIndex_IsZeroWhenNoArticleCitesTheTarget()
        {
            var g = Build(Node("T"), Node("A"));
            var result = new GraphAnalytics(g).ComputeHIndex("T");

            Assert.Equal(0, result.H);
            Assert.Equal(0.0, result.HMedian);
            Assert.Empty(result.CoreIndices);
        }

        [Fact]
        public void HIndex_AcceptsShortOpenAlexId()
        {
            var g = Build(Node("https://openalex.org/W1"), Node("https://openalex.org/W2", "https://openalex.org/W1"));
            var result = new GraphAnalytics(g).ComputeHIndex("W1");

            Assert.Equal("https://openalex.org/W1", result.TargetId);
        }

        [Fact]
        public void HIndex_ThrowsForUnknownOrEmptyId()
        {
            var analytics = new GraphAnalytics(HIndexGraph());

            Assert.Throws<KeyNotFoundException>(() => analytics.ComputeHIndex("NOPE"));
            Assert.Throws<ArgumentException>(() => analytics.ComputeHIndex("  "));
        }

        [Fact]
        public void Betweenness_OnAPath_CountsPairsThatPassThroughEachNode()
        {
            // No citations, so only the sequence edges exist: A - B - C - D
            var g = Build(Node("A"), Node("B"), Node("C"), Node("D"));
            var bc = new GraphAnalytics(g).ComputeBetweennessCentrality();

            Assert.Equal(0.0, bc[Index(g, "A")], 9);
            Assert.Equal(2.0, bc[Index(g, "B")], 9); // pairs A-C and A-D
            Assert.Equal(2.0, bc[Index(g, "C")], 9); // pairs A-D and B-D
            Assert.Equal(0.0, bc[Index(g, "D")], 9);
        }

        [Fact]
        public void Betweenness_SplitsPairsThatHaveTwoShortestPaths()
        {
            // Sequence edges A-B-C-D and the citation D -> A make a 4-cycle.
            // A and C have two shortest paths (through B or D), so B and D get 0.5 each, and so on.
            var g = Build(Node("A"), Node("B"), Node("C"), Node("D", "A"));
            var bc = new GraphAnalytics(g).ComputeBetweennessCentrality();

            foreach (string id in new[] { "A", "B", "C", "D" })
                Assert.Equal(0.5, bc[Index(g, id)], 9);
        }

        [Fact]
        public void KCore_RemovesNodesWithTooFewNeighbors()
        {
            // Sequence edges A-B-C-D plus the citation A -> C: triangle A, B, C and D hanging from C
            var g = Build(Node("A", "C"), Node("B"), Node("C"), Node("D"));
            var analytics = new GraphAnalytics(g);

            var core2 = analytics.ComputeKCore(2);
            Assert.Equal(new[] { Index(g, "A"), Index(g, "B"), Index(g, "C") }, core2.OrderBy(i => i));

            Assert.Equal(4, analytics.ComputeKCore(1).Count);
            Assert.Empty(analytics.ComputeKCore(3));
        }

        [Fact]
        public void KCore_ThrowsForNegativeK()
        {
            var analytics = new GraphAnalytics(Build(Node("A")));
            Assert.Throws<ArgumentOutOfRangeException>(() => analytics.ComputeKCore(-1));
        }
    }
}
