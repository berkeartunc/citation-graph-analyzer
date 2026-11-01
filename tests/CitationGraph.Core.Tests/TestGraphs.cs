namespace CitationGraph.Core.Tests
{
    // Helpers that build small graphs for the tests.
    internal static class TestGraphs
    {
        // Creates an article that cites the given ids.
        public static Article Node(string id, params string[] cites) =>
            new() { Id = id, Title = "Title " + id, ReferencedWorkIds = cites.ToList() };

        public static Graph Build(params Article[] articles) =>
            new GraphBuilder().Build(articles);

        public static int Index(Graph g, string id)
        {
            Assert.True(g.TryFindIndex(id, out int index), $"id not found: {id}");
            return index;
        }

        // Path to the real dataset, copied next to the test binaries.
        public static string DatasetPath =>
            Path.Combine(AppContext.BaseDirectory, "data", "articles.json");
    }
}
