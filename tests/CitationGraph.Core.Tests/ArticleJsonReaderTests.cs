namespace CitationGraph.Core.Tests
{
    public class ArticleJsonReaderTests
    {
        private static List<Article> Read(string json) => new ArticleJsonReader().ReadFromString(json);

        [Fact]
        public void Reads_AllFieldsOfAnArticle()
        {
            var articles = Read("""
                [{
                  "id": "https://openalex.org/W1",
                  "title": "Deep Learning",
                  "year": 2015,
                  "authors": ["Ada", "Alan"],
                  "referenced_works": ["https://openalex.org/W2", "https://openalex.org/W3"],
                  "venue": "Ignored",
                  "keywords": ["also", "ignored"]
                }]
                """);

            var a = Assert.Single(articles);
            Assert.Equal("https://openalex.org/W1", a.Id);
            Assert.Equal("Deep Learning", a.Title);
            Assert.Equal(2015, a.Year);
            Assert.Equal(new[] { "Ada", "Alan" }, a.Authors);
            Assert.Equal(2, a.ReferencedWorkIds.Count);
        }

        [Fact]
        public void Reads_AlternativeKeyNamesAndNumberIds()
        {
            var a = Assert.Single(Read("""[{"work_id": 42, "display_name": "X", "publication_year": "2001", "references": []}]"""));

            Assert.Equal("42", a.Id);
            Assert.Equal("X", a.Title);
            Assert.Equal(2001, a.Year);
        }

        [Fact]
        public void Handles_EscapesAndUnicode()
        {
            var a = Assert.Single(Read("""{"id": "W1", "title": "\"Quoted\" \u00e7\u0131kt\u0131\n"}"""));

            Assert.Equal("\"Quoted\" çıktı\n", a.Title);
        }

        [Fact]
        public void Uses_DefaultsForNullAndWrongTypes()
        {
            var a = Assert.Single(Read("""[{"id": "W1", "title": null, "year": null, "authors": {"name": "A"}, "referenced_works": null}]"""));

            Assert.Equal("Başlıksız", a.Title);
            Assert.Equal(0, a.Year);
            Assert.Empty(a.Authors);
            Assert.Empty(a.ReferencedWorkIds);
        }

        [Fact]
        public void Skips_ObjectsWithoutIdAndReadsNestedArrays()
        {
            var articles = Read("""[{"title": "no id"}, [{"id": "W1"}, {"id": "W2"}], 5, "text", true]""");

            Assert.Equal(new[] { "W1", "W2" }, articles.Select(a => a.Id));
        }

        [Fact]
        public void Skips_NonStringItemsInsideStringArrays()
        {
            var a = Assert.Single(Read("""[{"id": "W1", "authors": ["A", null, 3, {"x": [1, 2]}, "B"]}]"""));

            Assert.Equal(new[] { "A", "B" }, a.Authors);
        }

        [Theory]
        [InlineData("hello")]
        [InlineData("")]
        public void Throws_WhenTextIsNotJson(string text)
        {
            Assert.Throws<FormatException>(() => Read(text));
        }

        [Theory]
        [InlineData("""[{"id": "W1""")]
        [InlineData("""[{"id": "W1"} {"id": "W2"}]""")]
        [InlineData("""[{"id" "W1"}]""")]
        public void Throws_ForBrokenJson(string json)
        {
            Assert.Throws<FormatException>(() => Read(json));
        }

        [Fact]
        public void ReadFromFile_ThrowsWhenFileIsMissing()
        {
            Assert.Throws<FileNotFoundException>(() =>
                new ArticleJsonReader().ReadFromFile(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid() + ".json")));
        }
    }
}
