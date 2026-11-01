using static CitationGraph.Core.Tests.TestGraphs;

namespace CitationGraph.Core.Tests
{
    public class GraphBuilderTests
    {
        [Fact]
        public void Build_KeepsFirstArticleForDuplicateIdsAndSkipsEmptyIds()
        {
            var first = Node("A");
            var g = Build(first, Node("B"), Node("A"), Node(""), Node("  "));

            Assert.Equal(2, g.NodeCount);
            Assert.Same(first, g.ArticlesByIndex[Index(g, "A")]);
        }

        [Fact]
        public void Build_AddsCitationEdgesOnlyForIdsInsideTheDataset()
        {
            var g = Build(Node("A", "B", "OUTSIDE"), Node("B"));

            int a = Index(g, "A"), b = Index(g, "B");
            Assert.Equal(new[] { b }, g.Cites[a]);
            Assert.Equal(1, g.CitationEdgeCount);
        }

        [Fact]
        public void Build_IgnoresSelfCitationsAndRepeatedReferences()
        {
            var g = Build(Node("A", "A", "B", "B", " B "), Node("B"));

            int a = Index(g, "A");
            Assert.Single(g.Cites[a]);
            Assert.Equal(1, g.CitationEdgeCount);
        }

        [Fact]
        public void Build_LinksArticlesInIdOrderWithSequenceEdges()
        {
            // Input order is mixed on purpose
            var g = Build(Node("C"), Node("A"), Node("B"));

            Assert.Equal(new[] { Index(g, "B") }, g.SequenceNext[Index(g, "A")]);
            Assert.Equal(new[] { Index(g, "C") }, g.SequenceNext[Index(g, "B")]);
            Assert.Empty(g.SequenceNext[Index(g, "C")]);
            Assert.Equal(2, g.SequenceEdgeCount);
        }

        [Fact]
        public void Build_ThrowsWhenThereIsNoArticleWithAnId()
        {
            Assert.Throws<InvalidDataException>(() => Build(Node(""), Node(" ")));
        }

        [Fact]
        public void CitedBy_IsTheReverseOfCites()
        {
            var g = Build(Node("A", "C"), Node("B", "C"), Node("C", "A"));

            int a = Index(g, "A"), b = Index(g, "B"), c = Index(g, "C");
            Assert.Equal(new[] { a, b }, g.CitedBy[c]);
            Assert.Equal(new[] { c }, g.CitedBy[a]);
            Assert.Equal(2, g.CitationCount(c));
            Assert.Equal(1, g.ReferenceCount(a));
        }

        [Fact]
        public void TryFindIndex_AcceptsFullIdAndShortId()
        {
            var g = Build(Node("https://openalex.org/W123"), Node("https://openalex.org/W456"));

            Assert.True(g.TryFindIndex("https://openalex.org/W123", out int full));
            Assert.True(g.TryFindIndex(" w123 ", out int shortId));
            Assert.Equal(full, shortId);
            Assert.False(g.TryFindIndex("W999", out _));
            Assert.False(g.TryFindIndex("", out _));
        }

        [Theory]
        [InlineData("https://openalex.org/W2117539524", "W2117539524")]
        [InlineData("W42", "W42")]
        [InlineData("", "-")]
        [InlineData(null, "-")]
        public void ShortId_RemovesTheUrlPart(string? fullId, string expected)
        {
            Assert.Equal(expected, Core.Article.ShortId(fullId));
        }
    }
}
