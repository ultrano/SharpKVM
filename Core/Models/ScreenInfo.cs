using Avalonia;

namespace SharpKVM
{
    public class ScreenInfo
    {
        public string ID { get; set; } = string.Empty;
        public Rect Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public Rect UIBounds { get; set; }
    }
}
