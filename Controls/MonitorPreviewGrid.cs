using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AnimatedWallPaper.Services;

using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Size = System.Windows.Size;

namespace AnimatedWallPaper.Controls;

internal sealed record MonitorPreviewItem(string Id, string Label, double Aspect,
    WallpaperRequest? Request, bool IsSelected, bool IsSimulated);

/// <summary>Independent, live native previews laid out for the space the window provides.</summary>
public sealed class MonitorPreviewGrid : Grid, IDisposable
{
    private readonly Dictionary<string, PreviewCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PreviewCard> _orderedCards = [];
    private bool _suspended = true;
    private bool _audioEnabled = true;
    private bool _disposed;
    private int? _frameCap;
    private int _columns = -1;

    internal event Action<string>? DisplaySelected;
    internal event Action<string, string>? WallpaperSelected;
    internal event Action<string, double>? AspectSelected;
    internal IReadOnlyList<WallpaperPreviewControl> Previews => _orderedCards.Select(card => card.Preview).ToArray();

    public MonitorPreviewGrid()
    {
        ClipToBounds = true;
        Loaded += (_, _) => ApplySuspension();
        Unloaded += (_, _) =>
        {
            foreach (var card in _orderedCards) card.Preview.SetSuspended(true);
        };
    }

    internal void UpdateItems(IReadOnlyList<MonitorPreviewItem> items, IReadOnlyList<WallpaperEntry> catalog)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Dispatcher.VerifyAccess();
        var ids = items.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count != items.Count) throw new ArgumentException("Preview display IDs must be unique.", nameof(items));

        foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var card = _cards[id];
            card.Dispose();
            Children.Remove(card.Root);
            _cards.Remove(id);
        }

        _orderedCards.Clear();
        foreach (var item in items)
        {
            if (!_cards.TryGetValue(item.Id, out var card))
            {
                card = new PreviewCard(this, item.Id);
                card.Preview.SetAudioEnabled(_audioEnabled);
                _cards.Add(item.Id, card);
                Children.Add(card.Root);
            }
            _orderedCards.Add(card);
            card.Update(item, catalog, _frameCap);
            card.Preview.SetSuspended(_suspended || !IsLoaded);
        }

        // Keep native child windows parented to the same card when selection or order changes.
        // Clearing/rebuilding Children would destroy every otherwise unchanged preview.
        _columns = -1;
        InvalidateMeasure();
    }

    internal void SetSuspended(bool suspended)
    {
        if (_disposed) return;
        Dispatcher.VerifyAccess();
        _suspended = suspended;
        ApplySuspension();
    }

    internal void SetAudioEnabled(bool enabled)
    {
        if (_disposed) return;
        Dispatcher.VerifyAccess();
        _audioEnabled = enabled;
        foreach (var card in _orderedCards) card.Preview.SetAudioEnabled(enabled);
    }

    internal void SetFrameCap(int framesPerSecond)
    {
        if (_disposed) return;
        Dispatcher.VerifyAccess();
        _frameCap = Math.Clamp(framesPerSecond, 1, 30);
        foreach (var card in _orderedCards) card.SetFrameCap(_frameCap.Value);
    }

    private void ApplySuspension()
    {
        if (_disposed) return;
        foreach (var card in _orderedCards) card.Preview.SetSuspended(_suspended || !IsLoaded);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Reflow(constraint);
        return base.MeasureOverride(constraint);
    }

    private void Reflow(Size size)
    {
        var width = double.IsFinite(size.Width) ? Math.Max(1, size.Width) : 800;
        var height = double.IsFinite(size.Height) ? Math.Max(1, size.Height) : 500;
        var columns = ChooseColumns(width, height);
        if (_columns == columns) return;
        _columns = columns;
        var rows = Math.Max(1, (_orderedCards.Count + columns - 1) / columns);
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        for (var index = 0; index < columns; index++) ColumnDefinitions.Add(new ColumnDefinition());
        for (var index = 0; index < rows; index++) RowDefinitions.Add(new RowDefinition());
        for (var index = 0; index < _orderedCards.Count; index++)
        {
            var root = _orderedCards[index].Root;
            SetColumn(root, index % columns);
            SetRow(root, index / columns);
            root.Margin = new Thickness(index % columns == 0 ? 0 : 6, index < columns ? 0 : 6,
                index % columns == columns - 1 ? 0 : 6, index / columns == rows - 1 ? 0 : 6);
        }
    }

    private int ChooseColumns(double width, double height)
    {
        var count = _orderedCards.Count;
        if (count <= 1) return 1;
        var bestColumns = 1;
        var bestScore = double.MinValue;
        for (var columns = 1; columns <= Math.Min(4, count); columns++)
        {
            var rows = (count + columns - 1) / columns;
            var cellWidth = Math.Max(1, (width - (columns - 1) * 12) / columns - 18);
            var cellHeight = Math.Max(1, (height - (rows - 1) * 12) / rows - 104);
            var score = 0d;
            foreach (var card in _orderedCards)
            {
                var fit = LibraryLayout.Fit(cellWidth, cellHeight, card.Aspect);
                score += fit.Width * fit.Height;
            }
            // A slightly larger picture is not useful if its selector becomes unreadable.
            score *= Math.Min(1, cellWidth / 190) * Math.Min(1, cellHeight / 70);
            if (score > bestScore) (bestScore, bestColumns) = (score, columns);
        }
        return bestColumns;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Dispatcher.VerifyAccess();
        _disposed = true;
        foreach (var card in _orderedCards) card.Dispose();
        _orderedCards.Clear();
        _cards.Clear();
        Children.Clear();
    }

    private sealed record WallpaperChoice(string Id, string Title);
    private sealed record AspectChoice(double Aspect, string Title);

    private sealed class PreviewCard : IDisposable
    {
        private readonly MonitorPreviewGrid _owner;
        private readonly string _id;
        private readonly Button _heading;
        private readonly TextBlock _headingText;
        private readonly ComboBox _wallpapers;
        private readonly ComboBox _formats;
        private readonly AspectRatioDecorator _aspect;
        private readonly TextBlock _empty;
        private readonly TextBlock _status;
        private WallpaperChoice[] _choices = [];
        private WallpaperRequest? _request;
        private bool _updating;
        private bool _simulated;
        private bool _disposed;
        public Border Root { get; }
        public WallpaperPreviewControl Preview { get; }
        public double Aspect => _aspect.AspectRatio;

        public PreviewCard(MonitorPreviewGrid owner, string id)
        {
            _owner = owner;
            _id = id;
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root = new Border
            {
                Tag = id, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8), Child = content, SnapsToDevicePixels = true
            };
            Root.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            _headingText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
            _heading = new Button
            {
                Content = _headingText, Height = 28, MinWidth = 0, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 6), Tag = "SelectDisplay"
            };
            _heading.SetResourceReference(StyleProperty, "QuietButtonStyle");
            _heading.Click += (_, _) => _owner.DisplaySelected?.Invoke(_id);
            content.Children.Add(_heading);
            _formats = new ComboBox
            {
                DisplayMemberPath = nameof(AspectChoice.Title), SelectedValuePath = nameof(AspectChoice.Aspect),
                Height = 28, Margin = new Thickness(0, 0, 0, 6), Tag = "MonitorFormat",
                ToolTip = "Simulated monitor format"
            };
            _formats.SelectionChanged += (_, _) =>
            {
                if (!_updating && _formats.SelectedValue is double aspect) _owner.AspectSelected?.Invoke(_id, aspect);
            };
            content.Children.Add(_formats);

            _wallpapers = new ComboBox
            {
                DisplayMemberPath = nameof(WallpaperChoice.Title), SelectedValuePath = nameof(WallpaperChoice.Id),
                Height = 30, Margin = new Thickness(0, 0, 0, 8), Tag = "WallpaperSelection"
            };
            var choiceStyle = new Style(typeof(ComboBoxItem), owner.TryFindResource(typeof(ComboBoxItem)) as Style);
            var placeholder = new DataTrigger { Binding = new Binding(nameof(WallpaperChoice.Id)), Value = "" };
            placeholder.Setters.Add(new Setter(IsEnabledProperty, false));
            choiceStyle.Triggers.Add(placeholder);
            _wallpapers.ItemContainerStyle = choiceStyle;
            _wallpapers.SelectionChanged += (_, _) =>
            {
                if (!_updating && _wallpapers.SelectedValue is string { Length: > 0 } wallpaperId)
                    _owner.WallpaperSelected?.Invoke(_id, wallpaperId);
            };
            SetRow(_wallpapers, 1);
            content.Children.Add(_wallpapers);

            Preview = new WallpaperPreviewControl { Tag = id };
            Preview.SetSuspended(true);
            Preview.StatusChanged += PreviewStatusChanged;
            var screen = new Grid { Background = Brushes.Black };
            screen.Children.Add(Preview);
            _aspect = new AspectRatioDecorator { Child = screen, Margin = new Thickness(1) };
            _aspect.SetResourceReference(AspectRatioDecorator.FrameBrushProperty, "InputBorderBrush");
            // The space outside the monitor uses the card background. A black
            // letterbox used to look like part of the monitor, hiding its real size.
            var imageArea = new Grid { ClipToBounds = true };
            imageArea.Children.Add(_aspect);
            _empty = new TextBlock
            {
                Text = "Choose a wallpaper", FontSize = 12, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(12)
            };
            _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            screen.Children.Add(_empty);
            SetRow(imageArea, 2);
            content.Children.Add(imageArea);

            _status = new TextBlock
            {
                FontSize = 10, Margin = new Thickness(0, 7, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            SetRow(_status, 3);
            content.Children.Add(_status);
        }

        public void Update(MonitorPreviewItem item, IReadOnlyList<WallpaperEntry> catalog, int? frameCap)
        {
            _updating = true;
            try
            {
                _simulated = item.IsSimulated;
                _heading.Visibility = item.IsSimulated ? Visibility.Collapsed : Visibility.Visible;
                _formats.Visibility = item.IsSimulated ? Visibility.Visible : Visibility.Collapsed;
                if (_formats.ItemsSource is null)
                    _formats.ItemsSource = new[]
                    {
                        new AspectChoice(16d / 9, item.Label + " · 16:9"),
                        new AspectChoice(21d / 9, item.Label + " · 21:9 ultrawide"),
                        new AspectChoice(32d / 9, item.Label + " · 32:9 super ultrawide"),
                        new AspectChoice(9d / 16, item.Label + " · 9:16 portrait")
                    };
                _formats.SelectedValue = item.Aspect;
                AutomationProperties.SetName(_formats, "Monitor format for " + item.Label);
                _headingText.Text = item.Label + (item.IsSimulated ? " · Simulated" : "");
                _heading.ToolTip = _headingText.Text;
                _heading.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, item.IsSelected ? "AccentBrush" : "TextPrimaryBrush");
                Root.SetResourceReference(Border.BorderBrushProperty, item.IsSelected ? "AccentBrush" : "InputBorderBrush");
                AutomationProperties.SetName(Root, _headingText.Text + " live preview");
                AutomationProperties.SetName(_heading, "Select " + _headingText.Text);
                AutomationProperties.SetName(_wallpapers, "Wallpaper for " + _headingText.Text);
                AutomationProperties.SetName(Preview, _headingText.Text + " animated wallpaper");
                _aspect.AspectRatio = double.IsFinite(item.Aspect) && item.Aspect > 0 ? item.Aspect : 16d / 9;

                var choices = new[] { new WallpaperChoice("", "Choose a wallpaper") }
                    .Concat(catalog.Select(entry => new WallpaperChoice(entry.Id, entry.Title))).ToArray();
                if (!_choices.SequenceEqual(choices))
                {
                    _choices = choices;
                    _wallpapers.ItemsSource = _choices;
                }
                _wallpapers.SelectedValue = item.Request?.Id ?? "";

                // A simulated display may never create a desktop renderer. Each preview fills
                // its own HWND; only WallpaperPreviewControl is allowed to set PreviewTarget.
                var request = item.Request is null ? null : item.Request with
                {
                    Target = null, Preview = null,
                    FramesPerSecond = Math.Clamp(frameCap ?? item.Request.FramesPerSecond, 1, 30)
                };
                if (request != _request)
                {
                    if (request is not null && _request is not null && request.Settings is not null
                        && request with { Settings = _request.Settings, FramesPerSecond = _request.FramesPerSecond } == _request)
                    {
                        if (request.Settings != _request.Settings) Preview.UpdateSettings(request.Settings);
                        if (request.FramesPerSecond != _request.FramesPerSecond) Preview.SetFrameCap(request.FramesPerSecond);
                    }
                    else
                    {
                        Preview.Select(request);
                        SetStatus(request is null ? "No wallpaper assigned" : "Preparing preview…");
                    }
                    _request = request;
                }
                if (request is null) SetStatus("No wallpaper assigned");
                Preview.Visibility = request is null ? Visibility.Collapsed : Visibility.Visible;
                _empty.Visibility = request is null ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _updating = false; }
        }

        public void SetFrameCap(int framesPerSecond)
        {
            if (_request is not null) _request = _request with { FramesPerSecond = framesPerSecond };
            Preview.SetFrameCap(framesPerSecond);
        }

        private void PreviewStatusChanged(string status)
        {
            if (_disposed) return;
            if (!Root.Dispatcher.CheckAccess())
            {
                _ = Root.Dispatcher.InvokeAsync(() => PreviewStatusChanged(status));
                return;
            }
            if (_request is not null) SetStatus(status);
        }

        private void SetStatus(string status)
        {
            _status.Text = status + (_simulated ? " · Preview only" : "");
            _status.ToolTip = _status.Text;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Preview.StatusChanged -= PreviewStatusChanged;
            Preview.Dispose();
        }
    }
}
