using Avalonia;

namespace SharpKVM
{
    public class ScreenInfo
    {
        public string ID { get; set; } = string.Empty;
        public Rect Bounds;
        public bool IsPrimary;
        public Rect UIBounds;
    }
}
