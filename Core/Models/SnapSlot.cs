using Avalonia;

namespace SharpKVM
{
    public class SnapSlot
    {
        public string ID;
        public Rect Rect;
        public string ParentID;
        public string Direction;

        public SnapSlot(string id, Rect r, string pid, string dir)
        {
            ID = id;
            Rect = r;
            ParentID = pid;
            Direction = dir;
        }
    }
}
