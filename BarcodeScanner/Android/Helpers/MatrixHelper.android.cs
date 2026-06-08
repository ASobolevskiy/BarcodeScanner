using Android.Graphics;
using Android.Hardware.Camera2;
using AndroidX.Camera.Core;
using AndroidX.Camera.View;

namespace BarcodeScanner.Helpers;

public static class MatrixHelper
{
    public static Matrix? GetCorrectionMatrix(
        IImageProxy? imageProxy,
        PreviewView? previewView,
        LensFacing lensFacing = LensFacing.Back,
        bool applyRotation = false,
        Matrix? targetMatrix = null)
    {
        if (imageProxy == null || previewView == null)
            return null;
        
        var viewWidth = previewView.Width;
        var viewHeight = previewView.Height;
        if(viewWidth == 0 ||  viewHeight == 0)
            return null;
        
        var imgWidth = imageProxy.Width;
        var imgHeight = imageProxy.Height;
        var rotationDegrees = imageProxy.ImageInfo?.RotationDegrees ?? 0;
        
        var isRotated = rotationDegrees % 180 != 0;
        float rotatedWidth = isRotated ? imgHeight : imgWidth;
        float rotatedHeight = isRotated ? imgWidth : imgHeight;
        if (rotatedWidth <= 0 || rotatedHeight <= 0)
            return null;

        var scaleType = previewView.GetScaleType();
        if(scaleType == null)
            return null;

        var scale = GetScale(scaleType, viewWidth, rotatedWidth, viewHeight, rotatedHeight);

        var (offsetX, offsetY) = GetAxisOffsets(scaleType, viewWidth, viewHeight, rotatedWidth, rotatedHeight, scale);

        var matrix = targetMatrix ?? new Matrix();
        matrix.Reset();

        if (applyRotation)
        {
            matrix.PostRotate(rotationDegrees, rotatedWidth / 2f, rotatedHeight / 2f);
        }
        
        if (lensFacing == LensFacing.Front)
        {
            matrix.PostScale(-1f, 1f);
            matrix.PostTranslate(viewWidth, 0f);
        }
        
        matrix.PostScale(scale, scale);
        matrix.PostTranslate(offsetX, offsetY);

        return matrix;
    }

    private static float GetScale(PreviewView.ScaleType scaleType, int viewWidth, float rotatedWidth, int viewHeight,
        float rotatedHeight)
    {
        return scaleType.Equals(PreviewView.ScaleType.FitCenter) ||
                    scaleType.Equals(PreviewView.ScaleType.FitStart) ||
                    scaleType.Equals(PreviewView.ScaleType.FitEnd)
            ? MathF.Min(viewWidth / rotatedWidth, viewHeight / rotatedHeight)
            : MathF.Max(viewWidth / rotatedWidth, viewHeight / rotatedHeight);
    }

    private static (float, float) GetAxisOffsets(
        PreviewView.ScaleType scaleType, 
        int viewWidth,
        int  viewHeight,
        float rotatedWidth,
        float rotatedHeight,
        float scale)
    {
        float offsetX, offsetY;
        if (scaleType.Equals(PreviewView.ScaleType.FitStart) || scaleType.Equals(PreviewView.ScaleType.FillStart))
        {
            offsetX = 0f;
            offsetY = 0f;
        }
        else if (scaleType.Equals(PreviewView.ScaleType.FitEnd) || scaleType.Equals(PreviewView.ScaleType.FillEnd))
        {
            offsetX = viewWidth - rotatedWidth * scale;
            offsetY = viewHeight - rotatedHeight * scale;
        }
        else // FitCenter, FillCenter или неизвестный тип → центрируем
        {
            offsetX = (viewWidth - rotatedWidth * scale) / 2f;
            offsetY = (viewHeight - rotatedHeight * scale) / 2f;
        }
        return (offsetX, offsetY);
    }
}