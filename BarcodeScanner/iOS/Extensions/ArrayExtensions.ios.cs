using AVFoundation;

namespace BarcodeScanner;

public static class ArrayExtensions
{
    extension(IEnumerable<AVMetadataObjectType> collection)
    {
        public AVMetadataObjectType ToBitmask()
        {
            return collection.Aggregate<AVMetadataObjectType, AVMetadataObjectType>(0, (current, type) => current | type);
        }
    }
}