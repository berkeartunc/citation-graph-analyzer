using CitationGraph.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace CitationGraphAnalyzer
{
    // Custom control that draws the graph with WPF DrawingContext.
    // Supports zoom (mouse wheel), pan (left or right drag), hover, click and double click.
    // Normal mode places the visible nodes on a circle. Focus mode shows a growing
    // network of the articles that cite the clicked nodes.
    public sealed class GraphView : FrameworkElement
    {
        // Zoom / Pan state
        private readonly ScaleTransform _scale = new ScaleTransform(1.0, 1.0);
        private readonly TranslateTransform _pan = new TranslateTransform(0.0, 0.0);
        private readonly TransformGroup _viewTf = new TransformGroup();

        // Right-drag pan
        private bool _isPanning = false;
        private Point _lastPanScreen;

        // Left-drag pan + click detection
        private bool _isLeftDown = false;
        private bool _leftDragged = false;
        private Point _leftDownOrigin;
        private Point _leftLastPanScreen;
        private int? _leftDownHovered;

        private Graph? _graph;
        private HashSet<int> _visible = new();
        private HashSet<int> _newNodes = new();
        private HashSet<int> _kcoreNodes = new();
        private int? _selected;
        private int? _hovered;

        // Node positions (world)
        private readonly Dictionary<int, Point> _pos = new();

        public event Action<int?>? HoverChanged;
        public event Action<int>? NodeClicked;

        // MainWindow uses this to open focus mode
        public event Action<int>? NodeDoubleClicked;

        // Circle layout info (normal mode)
        private Point _layoutCenter;
        private double _layoutRadius;
        private int? _startNodeIdx;
        private bool _pendingFit = false;

        private const double NodeR = 15;

        // =========================
        // Focus Mode (Grow IN Network)
        // =========================
        private bool _focusMode = false;

        // center -> articles that cite it (u -> center). Only these edges are drawn.
        private readonly Dictionary<int, HashSet<int>> _focusInByCenter = new();

        // All citing articles of all centers, kept as one set for fast lookup
        private readonly HashSet<int> _focusAllSources = new();

        // Last clicked center, drawn in a different color
        private int? _focusActiveCenter = null;

        public bool IsIncomingFocusMode => _focusMode;

        public GraphView()
        {
            Focusable = true;
            ClipToBounds = true;

            // Order matters: scale then translate
            _viewTf.Children.Add(_scale);
            _viewTf.Children.Add(_pan);
        }

        public void RequestFitToGraph()
        {
            _pendingFit = true;
            InvalidateVisual();
        }

        // Focus mode: shows the center and the articles that cite it (u -> center).
        // If focus mode is already open, the old nodes stay and the network grows.
        // doFit = true fits the camera, it is used on the first entry.
        public void SetIncomingFocus(int centerIdx, IEnumerable<int> inSources, bool doFit = false)
        {
            // First entry
            if (!_focusMode)
            {
                _focusMode = true;
                _focusInByCenter.Clear();
                _focusAllSources.Clear();
                _focusActiveCenter = null;

                // Positions are reset once, then new nodes are added around the old ones
                _pos.Clear();
            }

            // Add the new citing articles to this center
            if (!_focusInByCenter.TryGetValue(centerIdx, out var set))
            {
                set = new HashSet<int>();
                _focusInByCenter[centerIdx] = set;
            }

            foreach (var u in inSources ?? Enumerable.Empty<int>())
                set.Add(u);

            RebuildFocusUnion();

            // The center and its citing articles must always be visible
            _visible.Add(centerIdx);
            foreach (var u in set)
                _visible.Add(u);

            _focusActiveCenter = centerIdx;

            // Place only the new nodes
            ReLayout();

            if (doFit)
                _pendingFit = true;

            InvalidateVisual();
        }

        public void ClearIncomingFocus()
        {
            _focusMode = false;
            _focusInByCenter.Clear();
            _focusAllSources.Clear();
            _focusActiveCenter = null;

            _pos.Clear();
            ReLayout();
            _pendingFit = true;
            InvalidateVisual();
        }

        private void RebuildFocusUnion()
        {
            _focusAllSources.Clear();
            foreach (var kv in _focusInByCenter)
                foreach (var u in kv.Value)
                    _focusAllSources.Add(u);
        }

        private Point ScreenToWorld(Point screen)
        {
            var inv = _viewTf.Inverse;
            return inv != null ? inv.Transform(screen) : screen;
        }

        public void SetScene(Graph graph,
                             IEnumerable<int> visible,
                             int? selected,
                             IEnumerable<int>? newNodes = null,
                             IEnumerable<int>? kcoreNodes = null)
        {
            _graph = graph;
            _visible = new HashSet<int>(visible ?? Enumerable.Empty<int>());
            _selected = selected;

            _newNodes = new HashSet<int>(newNodes ?? Enumerable.Empty<int>());
            _kcoreNodes = new HashSet<int>(kcoreNodes ?? Enumerable.Empty<int>());

            ReLayout();
            InvalidateVisual();
        }

        private void ReLayout()
        {
            if (_graph == null) return;
            if (_visible.Count == 0) return;

            if (_focusMode)
                ReLayoutFocusIncremental();
            else
                ReLayoutNormal();
        }

        private void ReLayoutNormal()
        {
            _pos.Clear();
            _startNodeIdx = null;

            if (_graph == null) return;
            if (_visible.Count == 0) return;

            // Numeric ids are sorted as numbers, other ids (like OpenAlex ids) as text
            int Key(int idx)
            {
                var s = _graph.IdByIndex[idx];
                return int.TryParse(s, out int v) ? v : int.MaxValue;
            }

            var list = _visible.ToList();
            list.Sort((a, b) =>
            {
                int ka = Key(a), kb = Key(b);
                if (ka != kb) return ka.CompareTo(kb);
                return string.Compare(_graph.IdByIndex[a], _graph.IdByIndex[b], StringComparison.Ordinal);
            });

            // The first node in id order gets the START marker
            if (list.Count > 0)
                _startNodeIdx = list[0];

            double w = Math.Max(ActualWidth, 200);
            double h = Math.Max(ActualHeight, 200);

            int n = list.Count;
            if (n == 1)
            {
                var p = new Point(w / 2.0, h / 2.0);
                _pos[list[0]] = p;
                _layoutCenter = p;
                _layoutRadius = 0;
                return;
            }

            var center = new Point(w / 2.0, h / 2.0);

            double spacingCirc = (NodeR * 2.0) + 3.0;
            double neededCircumference = n * spacingCirc;
            double neededRadius = neededCircumference / (2.0 * Math.PI);

            double baseRadiusCirc = Math.Min(w, h) * 0.42;
            double radiusCirc = Math.Max(baseRadiusCirc, neededRadius);

            double startAngle = -Math.PI / 2.0;

            for (int i = 0; i < n; i++)
            {
                double ang = startAngle + (2.0 * Math.PI * i) / n;
                _pos[list[i]] = new Point(
                    center.X + radiusCirc * Math.Cos(ang),
                    center.Y + radiusCirc * Math.Sin(ang)
                );
            }

            _layoutCenter = center;
            _layoutRadius = radiusCirc;
        }

        // Focus mode layout: old nodes keep their positions and new nodes are
        // placed around the clicked center.
        private void ReLayoutFocusIncremental()
        {
            _startNodeIdx = null;

            double w = Math.Max(ActualWidth, 200);
            double h = Math.Max(ActualHeight, 200);

            // If there is no active center, use the first one
            if (_focusActiveCenter == null)
            {
                var first = _focusInByCenter.Keys.FirstOrDefault();
                if (_focusInByCenter.Count > 0) _focusActiveCenter = first;
            }

            if (!_focusActiveCenter.HasValue)
                return;

            int active = _focusActiveCenter.Value;

            // 1) On the first entry, put the active center on the right side.
            //    If it already has a position, it is never moved.
            Point firstAnchor = new Point(w * 0.72, h * 0.52);
            if (!_pos.ContainsKey(active))
                _pos[active] = firstAnchor;

            // 2) New citing articles are placed to the left of the active center
            Point pActive = _pos[active];

            // 3) Place the citing articles of the active center that have no position yet
            if (_focusInByCenter.TryGetValue(active, out var activeSources))
            {
                var newSources = activeSources
                    .Where(u => _visible.Contains(u) && !_pos.ContainsKey(u))
                    .ToList();

                if (newSources.Count > 0)
                    PlaceSourcesAroundCenter(pActive, newSources, preferLeftSide: true);
            }

            // 4) Any other visible node without a position (rare) goes around the active center
            var leftovers = _visible.Where(u => !_pos.ContainsKey(u)).ToList();
            if (leftovers.Count > 0)
                PlaceSourcesAroundCenter(pActive, leftovers, preferLeftSide: true);

            // No circle in focus mode, fitting uses the bounding box
            _layoutCenter = new Point(w / 2.0, h / 2.0);
            _layoutRadius = Math.Min(w, h) * 0.35;
        }

        private void PlaceSourcesAroundCenter(Point pc, List<int> sources, bool preferLeftSide)
        {
            if (_graph == null || sources == null || sources.Count == 0) return;

            // Layout settings (pixels)
            double spacingY = (NodeR * 2.0) + 30.0;
            double baseOffsetX = (NodeR * 2.0) + 220.0;
            double curveK = 0.55;
            double colGapX = (NodeR * 2.0) + 120.0;

            // Sort by id, so nodes do not jump between redraws
            sources.Sort((a, b) =>
                string.Compare(_graph.IdByIndex[a], _graph.IdByIndex[b], StringComparison.Ordinal));

            double w = Math.Max(ActualWidth, 200);
            double h = Math.Max(ActualHeight, 200);

            // How many nodes fit in one column
            int maxPerCol = Math.Max(6, (int)((h * 0.78) / spacingY));

            double dir = preferLeftSide ? -1.0 : +1.0;

            // Rows go 0, +1, -1, +2, -2 ... around the center, so arrows overlap less
            static int SymRow(int row)
            {
                if (row == 0) return 0;
                int k = (row + 1) / 2;
                return (row % 2 == 1) ? +k : -k;
            }

            double yMin = NodeR + 35;
            double yMax = h - (NodeR + 35);

            double xMin = NodeR + 60;
            double xMax = w - (NodeR + 60);

            for (int i = 0; i < sources.Count; i++)
            {
                int u = sources[i];
                if (_pos.ContainsKey(u)) continue;

                int col = i / maxPerCol;
                int row = i % maxPerCol;

                int srow = SymRow(row);
                double y = pc.Y + srow * spacingY;

                // Keep inside the screen
                y = Math.Clamp(y, yMin, yMax);

                double yOff = y - pc.Y;

                // Columns on one side, bent like a curve: rows far from the center move further out
                double x = pc.X + dir * (baseOffsetX + col * colGapX + curveK * Math.Abs(yOff));

                // Do not go too far to the side
                x = Math.Clamp(x, xMin, xMax);

                // Simple overlap check: move the node until it does not touch another one
                int guard = 0;
                while (guard++ < 30)
                {
                    bool collision = false;

                    foreach (var kv in _pos)
                    {
                        var p = kv.Value;
                        double dx = p.X - x;
                        double dy = p.Y - y;
                        double d2 = dx * dx + dy * dy;

                        double minDist = (NodeR * 2.0) + 10.0;
                        if (d2 < minDist * minDist)
                        {
                            collision = true;
                            break;
                        }
                    }

                    if (!collision) break;

                    x += dir * 18.0;
                    x = Math.Clamp(x, xMin, xMax);
                }

                _pos[u] = new Point(x, y);
            }
        }

        private static Vector Rotate(Vector v, double rad)
        {
            double c = Math.Cos(rad);
            double s = Math.Sin(rad);
            return new Vector(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        private static void DrawArrowHead(DrawingContext dc, Pen pen, Point tip, Vector dir, double len = 12, double angleDeg = 28)
        {
            if (dir.Length < 1e-9) return;
            dir.Normalize();

            double a = angleDeg * Math.PI / 180.0;
            Vector left = Rotate(-dir, +a) * len;
            Vector right = Rotate(-dir, -a) * len;

            dc.DrawLine(pen, tip, tip + left);
            dc.DrawLine(pen, tip, tip + right);
        }

        private void DrawOuterIdRingWithArrows(DrawingContext dc)
        {
            if (_layoutRadius <= 0) return;

            double outer = _layoutRadius + (NodeR + 18);
            var ringPen = new Pen(Brushes.Green, 1.8);

            dc.DrawEllipse(null, ringPen, _layoutCenter, outer, outer);

            int arrowCount = Math.Clamp(_visible.Count / 30, 12, 40);
            double start = -Math.PI / 2.0;

            for (int i = 0; i < arrowCount; i++)
            {
                double ang = start + (2.0 * Math.PI * i) / arrowCount;

                Point tip = new Point(
                    _layoutCenter.X + outer * Math.Cos(ang),
                    _layoutCenter.Y + outer * Math.Sin(ang)
                );

                Vector tangent = new Vector(-Math.Sin(ang), Math.Cos(ang));
                DrawArrowHead(dc, ringPen, tip, tangent, len: 14, angleDeg: 28);
            }
        }

        private void DrawNodeText(DrawingContext dc, int u, Point p)
        {
            if (_graph == null) return;

            // Two lines inside the node: citation count and short id
            string id = Article.ShortId(_graph.IdByIndex[u]);
            int cit = _graph.CitationCount(u);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var ftCit = new FormattedText(
                cit.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                Brushes.Black,
                pixelsPerDip);

            var ftId = new FormattedText(
                id,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                3,
                Brushes.Black,
                pixelsPerDip);

            // Center both lines on the node
            double totalH = ftCit.Height + ftId.Height - 1;
            double yTop = p.Y - totalH / 2.0;

            dc.DrawText(ftCit, new Point(p.X - ftCit.Width / 2.0, yTop));
            dc.DrawText(ftId, new Point(p.X - ftId.Width / 2.0, yTop + ftCit.Height - 1));
        }

        private void DrawStartMarker(DrawingContext dc)
        {
            if (_startNodeIdx == null) return;
            if (!_pos.TryGetValue(_startNodeIdx.Value, out var p)) return;

            var pen = new Pen(Brushes.Red, 2.2);
            dc.DrawEllipse(null, pen, p, NodeR + 6, NodeR + 6);

            Vector dir = p - _layoutCenter;
            if (dir.Length < 1e-6) dir = new Vector(0, -1);
            dir.Normalize();

            Point anchor = p + dir * (NodeR + 22);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText(
                "START",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                Brushes.Red,
                pixelsPerDip
            );

            double padX = 8, padY = 4;

            var bgRect = new Rect(
                anchor.X - (ft.Width / 2.0) - padX,
                anchor.Y - (ft.Height / 2.0) - padY,
                ft.Width + 2 * padX,
                ft.Height + 2 * padY
            );

            dc.DrawRoundedRectangle(Brushes.White, pen, bgRect, 6, 6);

            dc.DrawText(ft, new Point(
                bgRect.X + (bgRect.Width - ft.Width) / 2.0,
                bgRect.Y + (bgRect.Height - ft.Height) / 2.0
            ));
        }

        private void DrawFocusLegendScreenFixed(DrawingContext dc)
        {
            var box = new Rect(12, 12, 260, 54);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                                    new Pen(Brushes.Gray, 1),
                                    box, 8, 8);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var title = new FormattedText(
                "Odak modu (gelen atıflar)",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                13,
                Brushes.Black,
                pixelsPerDip);

            dc.DrawText(title, new Point(box.X + 10, box.Y + 6));

            // legend line
            dc.DrawLine(new Pen(Brushes.DodgerBlue, 3),
                new Point(box.X + 12, box.Y + 36),
                new Point(box.X + 42, box.Y + 36));

            var inTxt = new FormattedText("Ok yönü: atıf yapan → merkez", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, Brushes.DodgerBlue, pixelsPerDip);

            dc.DrawText(inTxt, new Point(box.X + 52, box.Y + 28));
        }

        // Fits the view: the whole circle in normal mode, the bounding box in focus mode
        private void FitToGraphCore()
        {
            if (_visible.Count == 0) return;

            double pad = 30;
            double availW = Math.Max(50, ActualWidth - 2 * pad);
            double availH = Math.Max(50, ActualHeight - 2 * pad);

            double minX, minY, maxX, maxY;

            if (!_focusMode && _layoutRadius > 0)
            {
                double outer = _layoutRadius + (NodeR + 18) + 12;
                minX = _layoutCenter.X - outer;
                maxX = _layoutCenter.X + outer;
                minY = _layoutCenter.Y - outer;
                maxY = _layoutCenter.Y + outer;
            }
            else
            {
                minX = double.PositiveInfinity; minY = double.PositiveInfinity;
                maxX = double.NegativeInfinity; maxY = double.NegativeInfinity;

                foreach (int u in _visible)
                {
                    if (!_pos.TryGetValue(u, out var p)) continue;
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }

                if (double.IsInfinity(minX)) return;

                double margin = NodeR + 26;
                minX -= margin; minY -= margin;
                maxX += margin; maxY += margin;
            }

            double needW = Math.Max(10, maxX - minX);
            double needH = Math.Max(10, maxY - minY);

            double sx = availW / needW;
            double sy = availH / needH;

            double s = Math.Min(sx, sy);
            s = Math.Clamp(s, 0.02, 20.0);

            _scale.ScaleX = s;
            _scale.ScaleY = s;

            var worldCenter = new Point((minX + maxX) / 2.0, (minY + maxY) / 2.0);
            var screenCenter = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

            _pan.X = screenCenter.X - s * worldCenter.X;
            _pan.Y = screenCenter.Y - s * worldCenter.Y;
        }

        private void DrawFocusEdges(DrawingContext dc)
        {
            foreach (var kv in _focusInByCenter)
            {
                int c = kv.Key;
                if (!_visible.Contains(c)) continue;
                if (!_pos.TryGetValue(c, out var pc)) continue;

                bool isActive = (_focusActiveCenter.HasValue && _focusActiveCenter.Value == c);

                var pen = new Pen(isActive ? Brushes.DodgerBlue : Brushes.LightSkyBlue,
                                  isActive ? 3.4 : 2.2)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };

                foreach (int u in kv.Value)
                {
                    if (!_visible.Contains(u)) continue;
                    if (!_pos.TryGetValue(u, out var pu)) continue;

                    // incoming: u -> c
                    Vector d = pc - pu;

                    // If the two nodes are on the same point, use a default direction,
                    // otherwise Normalize() would fail and the arrow would not be drawn
                    if (d.Length < 1e-6)
                        d = new Vector(1, 0);

                    d.Normalize();

                    Point from = pu + d * (NodeR + 2);
                    Point tip = pc - d * (NodeR + 2);

                    dc.DrawLine(pen, from, tip);
                    DrawArrowHead(dc, pen, tip, d, len: 10, angleDeg: 30);
                }
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_graph == null || _visible.Count == 0) return;

            if (_pendingFit && ActualWidth > 1 && ActualHeight > 1)
            {
                FitToGraphCore();
                _pendingFit = false;
            }

            // The legend is drawn before the transform, so it does not move with zoom
            if (_focusMode)
                DrawFocusLegendScreenFixed(dc);

            dc.PushTransform(_viewTf);

            // =========================
            // Focus mode: only (citing article -> center) arrows.
            // There can be more than one center after the network grows.
            // =========================
            if (_focusMode)
            {
                DrawFocusEdges(dc);

                // Nodes
                foreach (int u in _visible)
                {
                    if (!_pos.TryGetValue(u, out var p)) continue;

                    bool isCenter = _focusInByCenter.ContainsKey(u);
                    bool isActiveCenter = _focusActiveCenter.HasValue && _focusActiveCenter.Value == u;
                    bool isSource = _focusAllSources.Contains(u);

                    Brush fill;
                    Pen stroke;

                    if (isActiveCenter)
                    {
                        // Light brown for the last clicked center
                        fill = new SolidColorBrush(Color.FromRgb(200, 170, 120));
                        stroke = new Pen(new SolidColorBrush(Color.FromRgb(120, 85, 55)), 2.4);
                    }
                    else if (isCenter)
                    {
                        fill = Brushes.OrangeRed;
                        stroke = new Pen(Brushes.Black, 2.0);
                    }
                    else if (isSource)
                    {
                        fill = Brushes.Orange;
                        stroke = new Pen(Brushes.OrangeRed, 1.8);
                    }
                    else
                    {
                        fill = new SolidColorBrush(Color.FromRgb(120, 200, 255));
                        stroke = new Pen(Brushes.DarkSlateGray, 1.2);
                    }

                    if (_hovered.HasValue && _hovered.Value == u)
                        fill = Brushes.Gold;

                    dc.DrawEllipse(fill, stroke, p, NodeR, NodeR);
                    DrawNodeText(dc, u, p);
                }

                dc.Pop();
                return;
            }

            // =========================
            // Normal mode: circle layout
            // =========================
            var penBlack = new Pen(Brushes.Black, 1.0);
            var penCoreEdge = new Pen(Brushes.DeepPink, 2.6);

            Brush selectedBrush = Brushes.OrangeRed;
            var penSelectedEdge = new Pen(selectedBrush, 3.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            HashSet<int>? selectedTargets = null;

            DrawOuterIdRingWithArrows(dc);
            DrawStartMarker(dc);

            // Citation edges (black)
            foreach (int u in _visible)
            {
                if (!_pos.TryGetValue(u, out var pu)) continue;

                foreach (int v in _graph.Cites[u])
                {
                    if (!_visible.Contains(v)) continue;
                    if (!_pos.TryGetValue(v, out var pv)) continue;
                    dc.DrawLine(penBlack, pu, pv);
                }
            }

            // k-core edges drawn again on top, in pink
            if (_kcoreNodes.Count > 0)
            {
                foreach (int u in _kcoreNodes)
                {
                    if (!_visible.Contains(u)) continue;
                    if (!_pos.TryGetValue(u, out var pu)) continue;

                    foreach (int v in _graph.Cites[u])
                    {
                        if (!_kcoreNodes.Contains(v)) continue;
                        if (!_visible.Contains(v)) continue;
                        if (!_pos.TryGetValue(v, out var pv)) continue;

                        dc.DrawLine(penCoreEdge, pu, pv);
                    }
                }
            }

            // References of the selected node, drawn on top
            if (_selected.HasValue)
            {
                int s = _selected.Value;
                if (_visible.Contains(s) && _pos.TryGetValue(s, out var ps))
                {
                    selectedTargets = new HashSet<int>();

                    foreach (int v in _graph.Cites[s])
                    {
                        if (!_visible.Contains(v)) continue;
                        if (!_pos.TryGetValue(v, out var pv)) continue;

                        selectedTargets.Add(v);
                        dc.DrawLine(penSelectedEdge, ps, pv);
                    }
                }
            }

            // nodes
            foreach (int u in _visible)
            {
                if (!_pos.TryGetValue(u, out var p)) continue;

                Brush fill = new SolidColorBrush(Color.FromRgb(120, 200, 255));

                if (_kcoreNodes.Count > 0 && _kcoreNodes.Contains(u))
                    fill = Brushes.DeepPink;

                if (_newNodes.Contains(u))
                    fill = Brushes.LimeGreen;

                if (_selected.HasValue && _selected.Value == u)
                    fill = Brushes.OrangeRed;

                if (_hovered.HasValue && _hovered.Value == u)
                    fill = Brushes.Gold;

                Pen stroke = new Pen(Brushes.DarkSlateGray, 1.0);

                if (selectedTargets != null &&
                    selectedTargets.Contains(u) &&
                    (!_selected.HasValue || u != _selected.Value))
                {
                    stroke = new Pen(selectedBrush, 2.6);
                }

                dc.DrawEllipse(fill, stroke, p, NodeR, NodeR);
                DrawNodeText(dc, u, p);
            }

            dc.Pop();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            // Layout again after resize (in focus mode only nodes without a position are placed)
            ReLayout();
            InvalidateVisual();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            Focus();

            double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;

            double oldScale = _scale.ScaleX;
            double newScale = Math.Clamp(oldScale * factor, 0.02, 20.0);
            if (Math.Abs(newScale - oldScale) < 1e-9) return;

            Point mouse = e.GetPosition(this);

            double tX = _pan.X;
            double tY = _pan.Y;

            double k = newScale / oldScale;

            _pan.X = mouse.X - (mouse.X - tX) * k;
            _pan.Y = mouse.Y - (mouse.Y - tY) * k;

            _scale.ScaleX = newScale;
            _scale.ScaleY = newScale;

            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);

            _isPanning = true;
            _lastPanScreen = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);

            _isPanning = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            // Double click: on a node opens focus mode, on empty space fits the view
            if (e.ClickCount == 2)
            {
                if (_hovered.HasValue)
                    NodeDoubleClicked?.Invoke(_hovered.Value);
                else
                    RequestFitToGraph();

                e.Handled = true;
                return;
            }

            base.OnMouseLeftButtonDown(e);

            Focus();

            _isLeftDown = true;
            _leftDragged = false;
            _leftDownOrigin = e.GetPosition(this);
            _leftLastPanScreen = _leftDownOrigin;

            _leftDownHovered = _hovered;

            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isLeftDown)
            {
                _isLeftDown = false;
                ReleaseMouseCapture();

                if (!_leftDragged && _leftDownHovered.HasValue)
                    NodeClicked?.Invoke(_leftDownHovered.Value);

                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isPanning)
            {
                var now = e.GetPosition(this);
                var d = now - _lastPanScreen;

                _pan.X += d.X;
                _pan.Y += d.Y;

                _lastPanScreen = now;
                InvalidateVisual();
                return;
            }

            if (_isLeftDown)
            {
                var now = e.GetPosition(this);
                var fromOrigin = now - _leftDownOrigin;

                if (!_leftDragged && (Math.Abs(fromOrigin.X) > 5 || Math.Abs(fromOrigin.Y) > 5))
                    _leftDragged = true;

                if (_leftDragged)
                {
                    var d = now - _leftLastPanScreen;

                    _pan.X += d.X;
                    _pan.Y += d.Y;

                    _leftLastPanScreen = now;
                    InvalidateVisual();
                    return;
                }
            }

            if (_graph == null || _visible.Count == 0) return;

            var mWorld = ScreenToWorld(e.GetPosition(this));

            int? hit = null;
            double best = double.MaxValue;

            double tol = NodeR + (3.0 / Math.Max(0.0001, _scale.ScaleX));
            double tol2 = tol * tol;

            foreach (int u in _visible)
            {
                if (!_pos.TryGetValue(u, out var p)) continue;

                double dx = p.X - mWorld.X;
                double dy = p.Y - mWorld.Y;
                double d2 = dx * dx + dy * dy;

                if (d2 <= tol2 && d2 < best)
                {
                    best = d2;
                    hit = u;
                }
            }

            if (hit != _hovered)
            {
                _hovered = hit;
                HoverChanged?.Invoke(hit);
                InvalidateVisual();
            }
        }
    }
}
