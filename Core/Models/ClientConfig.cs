namespace SharpKVM
{
    public class ClientConfig
    {
        public string IP { get; set; } = "";
        public double X { get; set; } = -1;
        public double Y { get; set; } = -1;
        public double Width { get; set; } = -1;
        public double Height { get; set; } = -1;
        public double DesktopX { get; set; } = -1;
        public double DesktopY { get; set; } = -1;
        public double DesktopWidth { get; set; } = -1;
        public double DesktopHeight { get; set; } = -1;
        public bool IsPlaced { get; set; }
        public bool IsSnapped { get; set; }
        public string SnapAnchorID { get; set; } = "";
        public LayoutMode LayoutMode { get; set; } = LayoutMode.Snap;
        public double Sensitivity { get; set; } = 3.0;
        public double WheelSensitivity { get; set; } = 1.0;
    }
}
