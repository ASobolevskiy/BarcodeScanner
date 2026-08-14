namespace BarcodeScanner.Models;

public readonly record struct ViewFinderRect(
    float Left,
    float Top,
    float Right,
    float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;

    public static readonly ViewFinderRect Empty = new(0, 0, 0, 0);
}