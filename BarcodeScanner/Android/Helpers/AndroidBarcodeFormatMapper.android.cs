using BarcodeScanner.Models;
using Xamarin.Google.MLKit.Vision.Barcode.Common;

namespace BarcodeScanner.Helpers;

internal static class AndroidBarcodeFormatMapper
{
    internal static int ToMlKitFormat(this BarcodeSymbology symbology)
    {
        return symbology switch
        {
            BarcodeSymbology.Aztec => Barcode.FormatAztec,
            BarcodeSymbology.Code128 => Barcode.FormatCode128,
            BarcodeSymbology.Code39 => Barcode.FormatCode39,
            BarcodeSymbology.Code93 => Barcode.FormatCode93,
            BarcodeSymbology.Ean13 => Barcode.FormatEan13,
            BarcodeSymbology.Ean8 => Barcode.FormatEan8,
            BarcodeSymbology.QrCode => Barcode.FormatQrCode,
            BarcodeSymbology.Upce => Barcode.FormatUpcE,
            BarcodeSymbology.Itf => Barcode.FormatItf,
            BarcodeSymbology.DataMatrix => Barcode.FormatDataMatrix,
            BarcodeSymbology.Pdf417 => Barcode.FormatPdf417,
            _ => Barcode.FormatAllFormats
        };
    }

    internal static BarcodeSymbology ToLocalFormat(this int mlFormat)
    {
        return mlFormat switch
        {
            Barcode.FormatAztec => BarcodeSymbology.Aztec,
            Barcode.FormatCode128 => BarcodeSymbology.Code128,
            Barcode.FormatCode39 => BarcodeSymbology.Code39,
            Barcode.FormatCode93 => BarcodeSymbology.Code93,
            Barcode.FormatEan13 => BarcodeSymbology.Ean13,
            Barcode.FormatEan8 => BarcodeSymbology.Ean8,
            Barcode.FormatQrCode => BarcodeSymbology.QrCode,
            Barcode.FormatUpcE => BarcodeSymbology.Upce,
            Barcode.FormatItf => BarcodeSymbology.Itf,
            Barcode.FormatDataMatrix => BarcodeSymbology.DataMatrix,
            Barcode.FormatPdf417 => BarcodeSymbology.Pdf417,
            _ => BarcodeSymbology.Unknown
        };
    } 
}