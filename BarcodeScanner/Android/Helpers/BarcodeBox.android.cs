namespace BarcodeScanner.Helpers;

public readonly struct BarcodeBox(float centerX, float centerY, float width, float height)
{
    public float CenterX { get; } = centerX;
    public float CenterY { get; } = centerY;
    public float Width { get; } = width;
    public float Height { get; } = height;
    
    public float[] ToRectPoints() =>
    [
        CenterX - Width / 2f,  CenterY - Height / 2f, // Левый верхний
        CenterX + Width / 2f,  CenterY - Height / 2f, // Правый верхний
        CenterX + Width / 2f,  CenterY + Height / 2f, // Правый нижний
        CenterX - Width / 2f,  CenterY + Height / 2f  // Левый нижний
    ];
}