using Avalonia;

namespace SharpKVM
{
    public class SnapSlot
    {
        public string ID { get; }
        public Rect Rect { get; }
        public string ParentID { get; }
        public EdgeDirection Direction { get; }

        public SnapSlot(string id, Rect r, string pid, EdgeDirection dir)
        {
            ID = id;
            Rect = r;
            ParentID = pid;
            Direction = dir;
        }
    }
}
