using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopPet1Refined.NaturalMotion.Models;
using DesktopPet1Refined.NaturalMotion.Runtime;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfImage = System.Windows.Controls.Image;
using WpfPanel = System.Windows.Controls.Panel;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace DesktopPet1Refined.App.NaturalMotion;

internal sealed class MotionSceneRenderer
{
    private readonly Canvas _layerCanvas;
    private readonly Canvas _debugCanvas;
    private readonly AssetImageLoader _assets;
    private readonly Dictionary<string, TargetVisual> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private MotionDefinition? _motion;

    public MotionSceneRenderer(Canvas layerCanvas, Canvas debugCanvas, AssetImageLoader assets)
    {
        _layerCanvas = layerCanvas;
        _debugCanvas = debugCanvas;
        _assets = assets;
    }

    public bool ShowPivots { get; set; }

    public bool ShowAnchors { get; set; }

    public bool ShowBounds { get; set; }

    public void Prepare(MotionDefinition motion)
    {
        Reset();
        _motion = motion;
        foreach (var target in motion.Targets.Where(item => item.Kind != MotionTargetKind.Window))
        {
            var bounds = target.Bounds ?? new MotionRect(0, 0, 512, 512);
            var image = new WpfImage
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                Source = target.Kind == MotionTargetKind.PartSprite
                    ? _assets.LoadBitmap(target.SpriteFrames[0])
                    : _assets.LoadBitmap(target.Source!)
            };
            Canvas.SetLeft(image, bounds.X);
            Canvas.SetTop(image, bounds.Y);
            WpfPanel.SetZIndex(image, target.ZIndex);

            var rotate = new RotateTransform();
            var translate = new TranslateTransform();
            var transforms = new TransformGroup();
            transforms.Children.Add(rotate);
            transforms.Children.Add(translate);
            image.RenderTransform = transforms;
            if (target.Pivot is { } pivot && bounds.Width > 0 && bounds.Height > 0)
            {
                image.RenderTransformOrigin = new WpfPoint(
                    (pivot.X - bounds.X) / bounds.Width,
                    (pivot.Y - bounds.Y) / bounds.Height);
            }

            _layerCanvas.Children.Add(image);
            _visuals[target.Id] = new TargetVisual(target, image, rotate, translate);
        }
        RebuildDebugOverlay();
    }

    public (double X, double Y) Apply(MotionSample sample)
    {
        var windowX = 0d;
        var windowY = 0d;
        if (_motion is null)
        {
            return (windowX, windowY);
        }

        foreach (var pair in sample.Values)
        {
            var target = _motion.Targets.First(item => string.Equals(item.Id, pair.Key.Target, StringComparison.OrdinalIgnoreCase));
            if (target.Kind == MotionTargetKind.Window)
            {
                if (pair.Key.Property == MotionProperty.TranslateX)
                {
                    windowX = pair.Value;
                }
                else if (pair.Key.Property == MotionProperty.TranslateY)
                {
                    windowY = pair.Value;
                }
                continue;
            }

            var visual = _visuals[target.Id];
            switch (pair.Key.Property)
            {
                case MotionProperty.TranslateX:
                    visual.Translate.X = pair.Value;
                    break;
                case MotionProperty.TranslateY:
                    visual.Translate.Y = pair.Value;
                    break;
                case MotionProperty.Rotate:
                    visual.Rotate.Angle = pair.Value;
                    break;
                case MotionProperty.Opacity:
                    visual.Image.Opacity = pair.Value;
                    break;
                case MotionProperty.SpriteFrame:
                    var frame = Math.Clamp((int)pair.Value, 0, target.SpriteFrames.Count - 1);
                    visual.Image.Source = _assets.LoadBitmap(target.SpriteFrames[frame]);
                    break;
            }
        }
        return (windowX, windowY);
    }

    public void RefreshDebugOverlay() => RebuildDebugOverlay();

    public void Reset()
    {
        _motion = null;
        _visuals.Clear();
        _layerCanvas.Children.Clear();
        _debugCanvas.Children.Clear();
    }

    private void RebuildDebugOverlay()
    {
        _debugCanvas.Children.Clear();
        if (_motion is null)
        {
            return;
        }

        foreach (var target in _motion.Targets.Where(item => item.Kind != MotionTargetKind.Window))
        {
            if (ShowBounds && target.Bounds is { } bounds)
            {
                var rectangle = new WpfRectangle
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Stroke = WpfBrushes.DeepSkyBlue,
                    StrokeThickness = 1,
                    StrokeDashArray = [4, 3],
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rectangle, bounds.X);
                Canvas.SetTop(rectangle, bounds.Y);
                _debugCanvas.Children.Add(rectangle);
            }
            if (ShowPivots && target.Pivot is { } pivot)
            {
                AddMarker(pivot, WpfBrushes.OrangeRed, 8);
            }
            if (ShowAnchors && target.Anchor is { } anchor)
            {
                AddMarker(anchor, WpfBrushes.LimeGreen, 6);
            }
        }
    }

    private void AddMarker(MotionPoint point, WpfBrush brush, double size)
    {
        var marker = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = brush,
            Stroke = WpfBrushes.Black,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(marker, point.X - (size / 2));
        Canvas.SetTop(marker, point.Y - (size / 2));
        _debugCanvas.Children.Add(marker);
    }

    private sealed record TargetVisual(
        MotionTargetDefinition Definition,
        WpfImage Image,
        RotateTransform Rotate,
        TranslateTransform Translate);
}
