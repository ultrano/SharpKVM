using Avalonia;

namespace SharpKVM
{
    public class ClientLayout
    {
        public string ClientKey { get; set; } = "";
        public Rect StageRect { get; set; }
        public Rect DesktopRect { get; set; }
        public bool IsPlaced { get; set; }
        public bool IsSnapped { get; set; }
        public string SnapAnchorID { get; set; } = "";
        public string AnchorScreenID { get; set; } = "";
        public EdgeDirection AnchorEdge { get; set; } = EdgeDirection.None;
    }
}
