namespace BarcodeScanner.Helpers;

internal readonly struct BarcodeBox(float centerX, float centerY, float width, float height)
{
    public float CenterX { get; } = centerX;
    public float CenterY { get; } = centerY;
    public float Width { get; } = width;
    public float Height { get; } = height;
    
    public float[] ToRectPoints() =>
    [
        CenterX - Width / 2f,  CenterY - Height / 2f, // Top-left
        CenterX + Width / 2f,  CenterY - Height / 2f, // Top-right
        CenterX + Width / 2f,  CenterY + Height / 2f, // Bottom-right
        CenterX - Width / 2f,  CenterY + Height / 2f  // Bottom-left
    ];
}