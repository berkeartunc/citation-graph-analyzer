using CitationGraph.Core;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CitationGraphAnalyzer
{
    public partial class MainWindow : Window
    {
        private Graph? _graph;
        private GraphAnalytics? _analytics;

        // Nodes shown on the canvas, nodes added by the last action (highlighted) and k-core nodes
        private readonly HashSet<int> _visible = new();
        private readonly HashSet<int> _newNodes = new();
        private readonly HashSet<int> _kcoreNodes = new();

        // Betweenness values, null until the user calculates them
        private double[]? _bcCache;

        // Tooltip shown when the mouse is over a node
        private ToolTip? _nodeTip;
        private int? _tipNodeIdx;

        private int? _selected;

        // Focus mode: the normal view is saved here, so it can be restored on exit
        private bool _isFocusMode;
        private HashSet<int> _savedVisible = new();
        private HashSet<int> _savedNew = new();
        private HashSet<int> _savedKcore = new();
        private int? _savedSelected;

        public MainWindow()
        {
            InitializeComponent();

            BtnLoad.Click += (_, _) => LoadJson();
            BtnShowH.Click += (_, _) => ShowHGraphFromTextbox();
            BtnBC.Click += async (_, _) => await ComputeBCAsync();
            BtnKCore.Click += (_, _) => ApplyKCore();

            View.HoverChanged += OnHoverChanged;
            View.NodeClicked += OnNodeClicked;
            View.NodeDoubleClicked += OnNodeDoubleClicked; // double click opens focus mode
            PreviewKeyDown += MainWindow_PreviewKeyDown;   // ESC closes focus mode

            InitNodeTooltip();
        }

        // -------------------------------------------------
        // Tooltip
        // -------------------------------------------------
        private void InitNodeTooltip()
        {
            _nodeTip = new ToolTip
            {
                PlacementTarget = View,
                Placement = PlacementMode.Relative,
                StaysOpen = true,
                IsOpen = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };

            View.ToolTip = _nodeTip;

            View.MouseMove += (_, e) =>
            {
                if (_nodeTip == null || !_nodeTip.IsOpen) return;
                UpdateNodeTooltipPosition(e.GetPosition(View));
            };

            View.MouseLeave += (_, _) => CloseNodeTooltip();

            ToolTipService.SetInitialShowDelay(View, 0);
            ToolTipService.SetShowDuration(View, int.MaxValue);
        }

        private void UpdateNodeTooltipPosition(Point p)
        {
            if (_nodeTip == null) return;
            _nodeTip.HorizontalOffset = p.X + 14;
            _nodeTip.VerticalOffset = p.Y + 14;
        }

        private void OpenNodeTooltip(int idx)
        {
            if (_graph == null || _nodeTip == null) return;

            var a = _graph.ArticlesByIndex[idx];

            // Show at most 5 authors in the tooltip
            string authors;
            if (a.Authors.Count == 0)
            {
                authors = "(yok)";
            }
            else
            {
                int take = Math.Min(5, a.Authors.Count);
                authors = string.Join(", ", a.Authors.Take(take));
                if (a.Authors.Count > take) authors += " ...";
            }

            var panel = new StackPanel { Margin = new Thickness(10) };

            panel.Children.Add(new TextBlock
            {
                Text = $"ID: {Article.ShortId(a.Id)}",
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Başlık: {a.Title}",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360
            });
            panel.Children.Add(new TextBlock { Text = $"Yıl: {a.Year}" });
            panel.Children.Add(new TextBlock
            {
                Text = $"Yazarlar: {authors}",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360
            });
            panel.Children.Add(new TextBlock { Text = $"Atıf sayısı: {_graph.CitationCount(idx)}" });
            panel.Children.Add(new TextBlock { Text = $"BC: {BcText(idx)}" });

            _nodeTip.Content = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = panel,
                Opacity = 0.96
            };

            _tipNodeIdx = idx;
            UpdateNodeTooltipPosition(Mouse.GetPosition(View));
            _nodeTip.IsOpen = true;
        }

        private void CloseNodeTooltip()
        {
            if (_nodeTip == null) return;
            _nodeTip.IsOpen = false;
            _tipNodeIdx = null;
        }

        private string BcText(int idx) =>
            _bcCache != null ? _bcCache[idx].ToString("F6") : "(hesaplanmadı)";

        // -------------------------------------------------
        // Focus panel (lists the articles that cite the selected one)
        // -------------------------------------------------
        private void UpdateFocusPanel(int centerIdx, List<int> inSources)
        {
            if (_graph == null) return;

            FocusBox.Visibility = Visibility.Visible;
            LblFocusCenter.Text = $"Merkez: {Article.ShortId(_graph.IdByIndex[centerIdx])}";
            LblFocusIn.Text = $"Gelen atıf (IN): {inSources.Count}";

            // Long lists are cut, so the panel stays readable
            const int Limit = 50;

            ListFocusIn.Items.Clear();
            foreach (var u in inSources.Take(Limit))
                ListFocusIn.Items.Add(Article.ShortId(_graph.IdByIndex[u]));

            if (inSources.Count > Limit)
                ListFocusIn.Items.Add($"... (+{inSources.Count - Limit})");
        }

        private void ClearFocusPanel()
        {
            FocusBox.Visibility = Visibility.Collapsed;
            LblFocusCenter.Text = "Merkez: -";
            LblFocusIn.Text = "Gelen atıf (IN): -";
            ListFocusIn.Items.Clear();
        }

        // -------------------------------------------------
        // Loading
        // -------------------------------------------------
        private void LoadJson()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json|Tüm dosyalar (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var articles = new ArticleJsonReader().ReadFromFile(dialog.FileName);

                _graph = new GraphBuilder().Build(articles);
                _analytics = new GraphAnalytics(_graph);
                _bcCache = null;

                ExitFocusModeIfNeeded();

                _visible.Clear();
                _newNodes.Clear();
                _kcoreNodes.Clear();
                _selected = null;

                for (int i = 0; i < _graph.NodeCount; i++)
                    _visible.Add(i);

                UpdateGlobalStats();
                CloseNodeTooltip();
                RefreshScene();
                View.RequestFitToGraph();

                MessageBox.Show($"Yüklendi. Makale sayısı: {_graph.NodeCount}", "Tamam");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata");
            }
        }

        // -------------------------------------------------
        // h-index view
        // -------------------------------------------------
        private void ShowHGraphFromTextbox()
        {
            if (_graph == null || _analytics == null)
            {
                MessageBox.Show("Önce JSON yükle.");
                return;
            }

            var id = TxtArticleId.Text.Trim();
            if (id.Length == 0)
            {
                MessageBox.Show("Makale ID boş olamaz.");
                return;
            }

            try
            {
                ExitFocusModeIfNeeded();

                var res = _analytics.ComputeHIndex(id);

                // Start a new view with only the article and its h-core
                _visible.Clear();
                _newNodes.Clear();
                _kcoreNodes.Clear();

                _visible.Add(res.TargetIndex);
                foreach (var idx in res.CoreIndices) _visible.Add(idx);
                foreach (var idx in _visible) _newNodes.Add(idx);

                _selected = res.TargetIndex;

                CloseNodeTooltip();
                UpdateHPanel(res);
                RefreshScene();
                View.RequestFitToGraph();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata");
            }
        }

        private void OnNodeClicked(int idx)
        {
            if (_graph == null || _analytics == null) return;

            // In focus mode a click grows the network instead of resetting it
            if (_isFocusMode)
            {
                FocusSelectAndGrow(idx, doFit: false);
                return;
            }

            // Normal mode: add the h-core of the clicked article to the view
            try
            {
                var res = _analytics.ComputeHIndex(_graph.IdByIndex[idx]);
                var before = new HashSet<int>(_visible);

                _newNodes.Clear();
                _kcoreNodes.Clear();

                _visible.Add(idx);
                foreach (var u in res.CoreIndices) _visible.Add(u);

                foreach (var u in _visible)
                    if (!before.Contains(u))
                        _newNodes.Add(u);

                _selected = idx;

                CloseNodeTooltip();
                UpdateHPanel(res);
                RefreshScene();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata");
            }
        }

        private void UpdateHPanel(HIndexResult res)
        {
            LblH.Text = $"h-index: {res.H}";
            LblHMedian.Text = $"h-median: {res.HMedian}";

            if (_graph == null) return;

            ListHCore.Items.Clear();
            for (int i = 0; i < res.CoreIndices.Count; i++)
                ListHCore.Items.Add($"{Article.ShortId(_graph.IdByIndex[res.CoreIndices[i]])} | {res.CoreCitations[i]}");
        }

        // -------------------------------------------------
        // Focus mode
        // -------------------------------------------------
        private void OnNodeDoubleClicked(int idx)
        {
            if (_graph == null) return;
            StartFocus(idx);
        }

        private void StartFocus(int centerIdx)
        {
            if (_graph == null) return;

            // Save the normal view only on the first entry
            if (!_isFocusMode)
            {
                _savedVisible = new HashSet<int>(_visible);
                _savedNew = new HashSet<int>(_newNodes);
                _savedKcore = new HashSet<int>(_kcoreNodes);
                _savedSelected = _selected;
            }

            _isFocusMode = true;

            // The focus network starts empty and grows with each click
            _visible.Clear();
            _kcoreNodes.Clear();
            _newNodes.Clear();

            FocusSelectAndGrow(centerIdx, doFit: true);
        }

        // Selects a node in focus mode and adds the articles that cite it.
        // Nodes added before stay on the canvas. Only the edges (citing article -> center) are drawn.
        private void FocusSelectAndGrow(int centerIdx, bool doFit)
        {
            if (_graph == null) return;

            var inSources = _graph.CitedBy[centerIdx].ToList();

            UpdateFocusPanel(centerIdx, inSources);

            // Highlight only the nodes added by this click
            _newNodes.Clear();

            if (_visible.Add(centerIdx))
                _newNodes.Add(centerIdx);

            foreach (var u in inSources)
                if (_visible.Add(u))
                    _newNodes.Add(u);

            _selected = centerIdx;

            CloseNodeTooltip();
            RefreshScene();
            View.SetIncomingFocus(centerIdx, inSources, doFit);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isFocusMode)
            {
                ExitFocusModeIfNeeded();
                e.Handled = true;
            }
        }

        private void ExitFocusModeIfNeeded()
        {
            if (!_isFocusMode) return;

            _isFocusMode = false;

            // Restore the view saved in StartFocus
            _visible.Clear();
            _visible.UnionWith(_savedVisible);

            _newNodes.Clear();
            _newNodes.UnionWith(_savedNew);

            _kcoreNodes.Clear();
            _kcoreNodes.UnionWith(_savedKcore);

            _selected = _savedSelected;

            View.ClearIncomingFocus();
            ClearFocusPanel();
            CloseNodeTooltip();
            RefreshScene();
            View.RequestFitToGraph();
        }

        // -------------------------------------------------
        // Betweenness and k-core
        // -------------------------------------------------
        private async Task ComputeBCAsync()
        {
            if (_graph == null || _analytics == null)
            {
                MessageBox.Show("Önce JSON yükle.");
                return;
            }

            ExitFocusModeIfNeeded();

            BtnBC.IsEnabled = false;
            ListBC.Items.Clear();
            ListBC.Items.Add("Hesaplanıyor...");

            try
            {
                // Runs on a background thread, so the window does not freeze
                var analytics = _analytics;
                double[] bc = await Task.Run(() => analytics.ComputeBetweennessCentrality());
                _bcCache = bc;

                var top10 = Enumerable.Range(0, _graph.NodeCount)
                                      .OrderByDescending(i => bc[i])
                                      .Take(10);

                ListBC.Items.Clear();
                foreach (var idx in top10)
                    ListBC.Items.Add($"{Article.ShortId(_graph.IdByIndex[idx])} | BC={bc[idx]:F6}");

                // Redraw, so the hover info shows the new values
                View.InvalidateVisual();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata");
                ListBC.Items.Clear();
            }
            finally
            {
                BtnBC.IsEnabled = true;
            }
        }

        private void ApplyKCore()
        {
            if (_graph == null || _analytics == null)
            {
                MessageBox.Show("Önce JSON yükle.");
                return;
            }

            ExitFocusModeIfNeeded();

            if (!int.TryParse(TxtK.Text.Trim(), out int k) || k < 0)
            {
                MessageBox.Show("Geçerli bir k gir (0, 1, 2...).");
                return;
            }

            try
            {
                _kcoreNodes.Clear();
                _kcoreNodes.UnionWith(_analytics.ComputeKCore(k));

                if (_visible.Count == 0)
                    for (int i = 0; i < _graph.NodeCount; i++)
                        _visible.Add(i);

                _selected = null;

                CloseNodeTooltip();
                RefreshScene();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hata");
            }
        }

        // -------------------------------------------------
        // Hover info and statistics
        // -------------------------------------------------
        private void OnHoverChanged(int? idx)
        {
            if (_graph == null || idx == null)
            {
                HoverId.Text = "ID: -";
                HoverTitle.Text = "Başlık: -";
                HoverYear.Text = "Yıl: -";
                HoverAuthors.Text = "Yazarlar: -";
                HoverCitations.Text = "Atıf sayısı: -";
                HoverBC.Text = "BC: -";
                CloseNodeTooltip();
                return;
            }

            int i = idx.Value;
            var a = _graph.ArticlesByIndex[i];

            HoverId.Text = $"ID: {Article.ShortId(a.Id)}";
            HoverTitle.Text = $"Başlık: {a.Title}";
            HoverYear.Text = $"Yıl: {a.Year}";
            HoverAuthors.Text = $"Yazarlar: {(a.Authors.Count == 0 ? "(yok)" : string.Join(", ", a.Authors))}";
            HoverCitations.Text = $"Atıf sayısı: {_graph.CitationCount(i)}";
            HoverBC.Text = $"BC: {BcText(i)}";

            if (_tipNodeIdx != i)
                OpenNodeTooltip(i);
        }

        private void RefreshScene()
        {
            if (_graph == null)
            {
                View.InvalidateVisual();
                LblNodes.Text = "Düğüm: -";
                LblBlack.Text = "Siyah kenar: -";
                LblGreen.Text = "Yeşil kenar: -";
                return;
            }

            View.SetScene(_graph, _visible, _selected, _newNodes, _kcoreNodes);

            var (citations, sequence) = CountEdgesInVisible(_graph, _visible);
            LblNodes.Text = $"Düğüm: {_visible.Count}";
            LblBlack.Text = $"Siyah kenar: {citations}";
            LblGreen.Text = $"Yeşil kenar: {sequence}";
        }

        private void UpdateGlobalStats()
        {
            if (_graph == null || _graph.NodeCount == 0)
            {
                LblTotalNodes.Text = "Toplam düğüm: -";
                LblTotalBlack.Text = "Toplam siyah kenar: -";
                LblTotalGreen.Text = "Toplam yeşil kenar: -";
                LblTotalGiven.Text = "Referans veren makale: -";
                LblTotalReceived.Text = "Atıf alan makale: -";
                LblMostGiving.Text = "En çok referans veren: -";
                LblMostCited.Text = "En çok atıf alan: -";
                return;
            }

            int n = _graph.NodeCount;

            int citingArticles = 0;   // articles with at least one reference in the dataset
            int citedArticles = 0;    // articles cited at least once
            int mostReferencesIdx = 0;
            int mostCitedIdx = 0;

            for (int i = 0; i < n; i++)
            {
                if (_graph.ReferenceCount(i) > 0) citingArticles++;
                if (_graph.CitationCount(i) > 0) citedArticles++;

                if (_graph.ReferenceCount(i) > _graph.ReferenceCount(mostReferencesIdx)) mostReferencesIdx = i;
                if (_graph.CitationCount(i) > _graph.CitationCount(mostCitedIdx)) mostCitedIdx = i;
            }

            LblTotalNodes.Text = $"Toplam düğüm: {n}";
            LblTotalBlack.Text = $"Toplam siyah kenar: {_graph.CitationEdgeCount}";
            LblTotalGreen.Text = $"Toplam yeşil kenar: {_graph.SequenceEdgeCount}";

            LblTotalGiven.Text = $"Referans veren makale: {citingArticles}";
            LblTotalReceived.Text = $"Atıf alan makale: {citedArticles}";

            LblMostGiving.Text =
                $"En çok referans veren: {Article.ShortId(_graph.IdByIndex[mostReferencesIdx])} ({_graph.ReferenceCount(mostReferencesIdx)})";
            LblMostCited.Text =
                $"En çok atıf alan: {Article.ShortId(_graph.IdByIndex[mostCitedIdx])} ({_graph.CitationCount(mostCitedIdx)})";
        }

        // Counts the edges whose both ends are visible
        private static (int citations, int sequence) CountEdgesInVisible(Graph g, HashSet<int> visible)
        {
            int citations = 0, sequence = 0;

            foreach (int u in visible)
            {
                foreach (int v in g.Cites[u]) if (visible.Contains(v)) citations++;
                foreach (int v in g.SequenceNext[u]) if (visible.Contains(v)) sequence++;
            }

            return (citations, sequence);
        }
    }
}
