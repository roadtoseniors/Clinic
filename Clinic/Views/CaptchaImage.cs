using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
namespace Clinic.Views;
public sealed class CaptchaImage : Image
{
    public static readonly StyledProperty<string?> ImageBase64Property = AvaloniaProperty.Register<CaptchaImage, string?>(nameof(ImageBase64));
    public string? ImageBase64 { get => GetValue(ImageBase64Property); set => SetValue(ImageBase64Property, value); }
    private Bitmap? _bitmap;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImageBase64Property) Load();
    }
    private void Load()
    {
        Source = null; _bitmap?.Dispose(); _bitmap = null;
        if (string.IsNullOrWhiteSpace(ImageBase64)) return;
        using var stream = new MemoryStream(Convert.FromBase64String(ImageBase64));
        Source = _bitmap = new Bitmap(stream);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Load(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Source = null; _bitmap?.Dispose(); _bitmap = null; base.OnDetachedFromVisualTree(e); }
}
