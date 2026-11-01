namespace CitationGraph.Core
{
    // Reads articles from a JSON file without any JSON library.
    // The root can be an array of articles or a single object. Nested arrays are
    // scanned too, and every object with an id becomes an Article.
    // Exception messages are in Turkish because the UI shows them to the user directly.
    public sealed class ArticleJsonReader
    {
        // Accepted key names for each field, so small changes in the
        // dataset format (for example "publication_year" instead of "year") still work
        private static readonly HashSet<string> IdKeys = new(StringComparer.OrdinalIgnoreCase)
            { "id", "work_id", "paper_id" };

        private static readonly HashSet<string> TitleKeys = new(StringComparer.OrdinalIgnoreCase)
            { "title", "paper_title", "display_name", "name" };

        private static readonly HashSet<string> YearKeys = new(StringComparer.OrdinalIgnoreCase)
            { "year", "publication_year", "published_year" };

        private static readonly HashSet<string> AuthorKeys = new(StringComparer.OrdinalIgnoreCase)
            { "authors", "author_names", "author", "creators" };

        private static readonly HashSet<string> RefKeys = new(StringComparer.OrdinalIgnoreCase)
            { "referenced_works", "references", "referencedWorks" };

        public List<Article> ReadFromFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Dosya bulunamadı.", path);

            return ReadFromString(File.ReadAllText(path));
        }

        public List<Article> ReadFromString(string json)
        {
            ArgumentNullException.ThrowIfNull(json);

            // Quick check before parsing: valid input starts with '{' or '['
            char first = FirstNonWhitespace(json);
            if (first != '[' && first != '{')
                throw new FormatException("Dosya içeriği JSON gibi görünmüyor ('{' veya '[' ile başlamıyor).");

            var reader = new MiniJsonReader(json);
            var articles = new List<Article>();

            if (reader.PeekNonWhitespace() == '[')
                ReadArray(ref reader, articles);
            else
                ScanValue(ref reader, articles);

            return articles;
        }

        private static char FirstNonWhitespace(string s)
        {
            foreach (char ch in s)
                if (!char.IsWhiteSpace(ch))
                    return ch;
            return '\0';
        }

        private void ReadArray(ref MiniJsonReader r, List<Article> result)
        {
            r.Expect('[');
            if (r.TryConsume(']')) return;

            while (true)
            {
                ScanValue(ref r, result);

                if (r.TryConsume(']')) break;
                r.Expect(',');
            }
        }

        private void ScanValue(ref MiniJsonReader r, List<Article> result)
        {
            r.SkipWhitespace();
            char c = r.Peek();

            if (c == '{')
            {
                var article = ReadArticle(ref r);
                if (article != null) result.Add(article);
            }
            else if (c == '[')
            {
                ReadArray(ref r, result);
            }
            else
            {
                r.SkipValue();
            }
        }

        // Reads one object. Returns null if it has no id (then it is not an article).
        private Article? ReadArticle(ref MiniJsonReader r)
        {
            r.Expect('{');
            if (r.TryConsume('}')) return null;

            var a = new Article();
            bool hasId = false;

            while (true)
            {
                string key = r.ReadString();
                r.Expect(':');
                r.SkipWhitespace();

                if (IdKeys.Contains(key))
                {
                    a.Id = r.ReadStringOrNumberAsString().Trim();
                    hasId = a.Id.Length > 0;
                }
                else if (TitleKeys.Contains(key))
                {
                    a.Title = r.ReadStringOrNumberAsString();
                    if (string.IsNullOrWhiteSpace(a.Title)) a.Title = "Başlıksız";
                }
                else if (YearKeys.Contains(key))
                {
                    if (int.TryParse(r.ReadStringOrNumberAsString(), out int year))
                        a.Year = year;
                }
                else if (RefKeys.Contains(key))
                {
                    a.ReferencedWorkIds = r.ReadStringArrayOrEmpty();
                }
                else if (AuthorKeys.Contains(key))
                {
                    // Authors are expected as ["A", "B"]. Any other shape gives an empty list.
                    a.Authors = r.ReadStringArrayOrEmpty();
                }
                else
                {
                    r.SkipValue();
                }

                if (r.TryConsume('}')) break;
                r.Expect(',');
            }

            return hasId ? a : null;
        }
    }
}
