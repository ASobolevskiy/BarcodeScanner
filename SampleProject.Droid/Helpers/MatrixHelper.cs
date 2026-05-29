using Android.Graphics;
using AndroidX.Camera.Core;
using AndroidX.Camera.View;

namespace SampleProject.Droid.Helpers;

public static class MatrixHelper
{
    public static Matrix? GetCorrectionMatrix(IImageProxy? imageProxy, PreviewView? previewView)
    {
        if (imageProxy == null || previewView == null)
            return null;
        
        var matrix = new Matrix();

        var imageWidth = imageProxy.Width;
        var imageHeight = imageProxy.Height;
        var rotationDegrees = imageProxy.ImageInfo?.RotationDegrees ?? 0;
        
        var rotatedWidth = imageWidth;
        var rotatedHeight = imageHeight;
        if (rotationDegrees is 90 or 270)
        {
            rotatedWidth = imageHeight;
            rotatedHeight = imageWidth;
        }

        var viewWidth = previewView.Width;
        var viewHeight = previewView.Height;
        if (viewWidth == 0 || viewHeight == 0 || rotatedWidth == 0 || rotatedHeight == 0)
            return matrix;
        
        float scale;
        if (previewView.GetScaleType().Equals(PreviewView.ScaleType.FitCenter))
        {
            scale = Math.Min((float)viewWidth / rotatedWidth, (float)viewHeight / rotatedHeight);
        }
        else
        {
            scale = Math.Max((float)viewWidth / rotatedWidth, (float)viewHeight / rotatedHeight);
        }
        
        var offsetX = (viewWidth - rotatedWidth * scale) / 2f;
        var offsetY = (viewHeight - rotatedHeight * scale) / 2f;
        
        matrix.PostScale(scale, scale);
        matrix.PostTranslate(offsetX, offsetY);

        return matrix;
    }
}