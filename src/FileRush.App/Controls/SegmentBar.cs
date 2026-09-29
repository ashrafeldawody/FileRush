using System.Windows;
using System.Windows.Media;
using FileRush.Core.Models;

namespace FileRush.App.Controls;

public sealed class SegmentBar : FrameworkElement
{
    private static readonly Brush Downloaded = new SolidColorBrush(Color.FromRgb(0x2E, 0x62, 0xC8));
    private static readonly Brush ActiveDownloaded = new SolidColorBrush(Color.FromRgb(0x4A, 0x8A, 0xF0));
    private static readonly Brush Remaining = Brushes.White;
    private static readonly Pen StartPen = new(new SolidColorBrush(Color.FromRgb(0xE8, 0x3E, 0xA8)), 1.5);
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)), 1);

    static SegmentBar()
    {
        Downloaded.Freeze();
        ActiveDownloaded.Freeze();
        StartPen.Freeze();
        BorderPen.Freeze();
    }

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<SegmentSnapshot>), typeof(SegmentBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TotalSizeProperty = DependencyProperty.Register(
        nameof(TotalSize), typeof(long), typeof(SegmentBar), new FrameworkPropertyMetadata(-1L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IndeterminateBytesProperty = DependencyProperty.Register(
        nameof(IndeterminateBytes), typeof(long), typeof(SegmentBar), new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<SegmentSnapshot>? Segments
    {
        get => (IReadOnlyList<SegmentSnapshot>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public long TotalSize
    {
        get => (long)GetValue(TotalSizeProperty);
        set => SetValue(TotalSizeProperty, value);
    }

    public long IndeterminateBytes
    {
        get => (long)GetValue(IndeterminateBytesProperty);
        set => SetValue(IndeterminateBytesProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsInfinity(availableSize.Height) ? 24 : Math.Min(24, availableSize.Height);
        var width = double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2) return;
        var rect = new Rect(0.5, 0.5, width - 1, height - 1);
        dc.DrawRectangle(Remaining, BorderPen, rect);
        var total = TotalSize;
        var segments = Segments;
        if (total <= 0 || segments is null || segments.Count == 0)
        {
            if (IndeterminateBytes > 0)
            {
                var phase = (IndeterminateBytes / 65536) % 20 / 20.0;
                var x = 1 + phase * (width - 2) * 0.8;
                dc.DrawRectangle(ActiveDownloaded, null, new Rect(x, 1, Math.Min((width - 2) * 0.2, width - 1 - x), height - 2));
            }
            return;
        }
        var scale = (width - 2) / total;
        foreach (var segment in segments)
        {
            var end = segment.End < 0 ? total - 1 : segment.End;
            var downloadedEnd = Math.Min(segment.Position, end + 1);
            if (downloadedEnd > segment.Start)
            {
                var x1 = 1 + segment.Start * scale;
                var x2 = 1 + downloadedEnd * scale;
                dc.DrawRectangle(segment.Active ? ActiveDownloaded : Downloaded, null, new Rect(x1, 1, Math.Max(0.5, x2 - x1), height - 2));
            }
        }
        foreach (var segment in segments)
        {
            var x = 1 + segment.Start * scale;
            dc.DrawLine(StartPen, new Point(x, 1), new Point(x, height - 1));
        }
    }
}
