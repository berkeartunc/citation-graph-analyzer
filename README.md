# Citation Graph Analyzer

[Türkçe](README.tr.md)

A Windows desktop app (C#, WPF) that builds a citation graph from a JSON file of scientific articles, analyzes it and draws it interactively. The graph algorithms and the JSON reader are written from scratch, without any external library.

## What it does

- **Reads articles from JSON** with a small hand-written JSON reader. It accepts a few different key names (for example `year` or `publication_year`) and skips fields it doesn't use.
- **Builds a directed graph:** each article is a node, and `A -> B` means article A cites article B. References to articles outside the dataset, repeated references and self-citations are ignored. Sequence edges also link the articles in id order.
- **h-index, h-core and h-median** of an article, calculated from the articles that cite it.
- **Betweenness centrality** of every node with the Brandes algorithm, on a background thread so the window stays responsive.
- **k-core decomposition:** highlights the subgraph where every node has at least k neighbors.
- **Interactive view:** zoom, pan, hover details, click to expand the h-core of an article, and a focus mode (double click) that grows a network of the articles citing the selected one.

## Dataset

`data/articles.json` contains 1,000 computer vision articles (1971-2025) taken from [OpenAlex](https://openalex.org), an open catalog of scientific works (CC0 license).

| Metric | Value |
|---|---|
| Articles | 1,000 |
| Citation edges | 1,580 (1,590 references, 10 self-citations ignored) |
| Most cited article | *Very Deep Convolutional Networks for Large-Scale Image Recognition* (2014), 58 citations, h-index 5 |
| Highest h-index | 6, *Improving quality inspection of food products by computer vision - a review* (2003) |
| Highest betweenness | the same 2014 article |
| Largest k-core | k = 6, 16 articles |

The sequence edges join all articles into one chain, so the undirected graph used for betweenness and k-core is always connected. This raises the values compared to a graph with citation edges only.

## How the h-index is calculated here

For an article T, take the articles that cite T and sort them by how many citations each of them has. h is the largest number such that h of these articles have at least h citations. The h-core is those h articles and the h-median is the median of their citation counts.

## Project structure

```
src/
├── CitationGraph.Core/          class library (.NET 8, no UI, runs on any OS)
│   ├── Article.cs               data model
│   ├── ArticleJsonReader.cs     reads articles from JSON
│   ├── MiniJsonReader.cs        hand-written JSON tokenizer
│   ├── Graph.cs                 adjacency lists, citation counts, id lookup
│   ├── GraphBuilder.cs          builds the graph from articles
│   ├── GraphAnalytics.cs        h-index, betweenness, k-core
│   └── HIndexResult.cs
└── CitationGraphAnalyzer/       WPF app (Windows)
    ├── MainWindow.xaml(.cs)     controls and statistics panel
    └── GraphView.cs             custom control that draws the graph
tests/CitationGraph.Core.Tests/  xUnit tests
data/articles.json               dataset
```

The analysis code is in a separate library with no UI code, so it can be tested on its own and reused with a different interface.

## Getting started

Requirements: Windows and the .NET 8 SDK (or Visual Studio 2022).

1. Open `CitationGraphAnalyzer.sln` in Visual Studio and run the `CitationGraphAnalyzer` project, or run:
   ```
   dotnet run --project src/CitationGraphAnalyzer
   ```
2. Click **JSON Yükle** (Load JSON) and select `data/articles.json`.
3. Enter an article id (for example `W1686810756`) and click **Göster / Baştan Çiz** (Show / Redraw) to see its h-core. Click nodes to expand, double click to open focus mode, and press ESC to leave it.

The UI language is Turkish.

## Tests

```
dotnet test tests/CitationGraph.Core.Tests
```

30 xUnit tests cover the graph builder, h-index, betweenness, k-core and the JSON reader (escapes, wrong types, broken input). Some tests run on the real dataset, and their expected values were checked separately with the NetworkX library in Python. The tests use only the core library, so they also run on Linux and macOS.

## Tech stack

C# 12, .NET 8, WPF, xUnit
