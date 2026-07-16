using AVFoundation;
using BarcodeScanner.Models;

namespace BarcodeScanner.Helpers;

internal static class IosBarcodeFormatMapper
{
    internal static AVMetadataObjectType ToMetadataType(this BarcodeSymbology barcodeSymbology)
    {
        return barcodeSymbology switch
        {
            BarcodeSymbology.Aztec => AVMetadataObjectType.AztecCode,
            BarcodeSymbology.Code128 => AVMetadataObjectType.Code128Code,
            BarcodeSymbology.Code39 => AVMetadataObjectType.Code39Code,
            BarcodeSymbology.Code93 => AVMetadataObjectType.Code93Code,
            BarcodeSymbology.Ean13 => AVMetadataObjectType.EAN13Code,
            BarcodeSymbology.Ean8 => AVMetadataObjectType.EAN8Code,
            BarcodeSymbology.QrCode => AVMetadataObjectType.QRCode,
            BarcodeSymbology.Upce => AVMetadataObjectType.UPCECode,
            // ML Kit's FORMAT_ITF is generic variable-length Interleaved 2 of 5, not restricted
            // to the 14-digit GS1 subset — request both native types so iOS matches what Android detects.
            BarcodeSymbology.Itf => AVMetadataObjectType.Interleaved2of5Code | AVMetadataObjectType.ITF14Code,
            BarcodeSymbology.DataMatrix => AVMetadataObjectType.DataMatrixCode,
            BarcodeSymbology.Pdf417 => AVMetadataObjectType.PDF417Code,
            BarcodeSymbology.Codabar => AVMetadataObjectType.CodabarCode,
            // AllSymbologies/Unknown intentionally have no native equivalent: callers must resolve
            // "all formats" via AVCaptureMetadataOutput.AvailableMetadataObjectTypes instead
            // (see MetadataScanningController.ResolveMetadataObjectTypes), since AVFoundation has
            // no single "match everything" constant the way ML Kit does.
            _ => AVMetadataObjectType.None
        };
    }

    internal static BarcodeSymbology ToLocalFormat(this AVMetadataObjectType objectType)
    {
        return objectType switch
        {
            AVMetadataObjectType.AztecCode => BarcodeSymbology.Aztec,
            AVMetadataObjectType.Code128Code => BarcodeSymbology.Code128,
            AVMetadataObjectType.Code39Code => BarcodeSymbology.Code39,
            AVMetadataObjectType.Code93Code => BarcodeSymbology.Code93,
            AVMetadataObjectType.EAN13Code => BarcodeSymbology.Ean13,
            AVMetadataObjectType.EAN8Code => BarcodeSymbology.Ean8,
            AVMetadataObjectType.QRCode => BarcodeSymbology.QrCode,
            AVMetadataObjectType.UPCECode => BarcodeSymbology.Upce,
            AVMetadataObjectType.ITF14Code => BarcodeSymbology.Itf,
            AVMetadataObjectType.Interleaved2of5Code => BarcodeSymbology.Itf,
            AVMetadataObjectType.DataMatrixCode => BarcodeSymbology.DataMatrix,
            AVMetadataObjectType.PDF417Code => BarcodeSymbology.Pdf417,
            AVMetadataObjectType.CodabarCode => BarcodeSymbology.Codabar,
            _ => BarcodeSymbology.Unknown
        };
    }
}