namespace CitationGraph.Core.Tests
{
    // Runs the whole pipeline on the real dataset (data/articles.json).
    // The expected values were checked separately with the NetworkX library.
    public class DatasetTests
    {
        private static readonly Lazy<Graph> Dataset = new(() =>
            new GraphBuilder().Build(new ArticleJsonReader().ReadFromFile(TestGraphs.DatasetPath)));

        [Fact]
        public void Dataset_HasExpectedSize()
        {
            var g = Dataset.Value;

            Assert.Equal(1000, g.NodeCount);
            Assert.Equal(1580, g.CitationEdgeCount); // 1590 references minus 10 self-citations
            Assert.Equal(999, g.SequenceEdgeCount);
        }

        [Fact]
        public void MostCitedArticle_HasExpectedHIndex()
        {
            var g = Dataset.Value;
            int top = Enumerable.Range(0, g.NodeCount).MaxBy(g.CitationCount);

            Assert.Equal("W1686810756", Article.ShortId(g.IdByIndex[top]));
            Assert.Equal(58, g.CitationCount(top));

            var h = new GraphAnalytics(g).ComputeHIndex("W1686810756");
            Assert.Equal(5, h.H);
            Assert.Equal(8.0, h.HMedian);
        }

        [Fact]
        public void Betweenness_MatchesReferenceValue()
        {
            var g = Dataset.Value;
            var bc = new GraphAnalytics(g).ComputeBetweennessCentrality();
            int best = Enumerable.Range(0, g.NodeCount).MaxBy(i => bc[i]);

            Assert.Equal("W1686810756", Article.ShortId(g.IdByIndex[best]));
            Assert.Equal(61831.1437, bc[best], 3);
        }

        [Fact]
        public void KCore_LargestCoreHas16Articles()
        {
            var analytics = new GraphAnalytics(Dataset.Value);

            Assert.Equal(16, analytics.ComputeKCore(6).Count);
            Assert.Empty(analytics.ComputeKCore(7));
        }
    }
}
