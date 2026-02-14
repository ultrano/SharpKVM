using SharpHook.Native;

namespace SharpKVM;

public static class MacInputMapping
{
    public static KeyCode MapKeyCodeForMacRemote(KeyCode code)
    {
        return code switch
        {
            KeyCode.VcLeftMeta => KeyCode.VcLeftAlt,
            KeyCode.VcRightMeta => KeyCode.VcRightAlt,
            KeyCode.VcLeftAlt => KeyCode.VcLeftMeta,
            KeyCode.VcRightAlt => KeyCode.VcRightMeta,
            KeyCode.VcHangul => KeyCode.VcCapsLock,
            _ => code
        };
    }

    public static bool TryMapRawMouseClickType(int button, bool isDown, out uint type)
    {
        if (button == 0)
        {
            type = isDown ? 1u : 2u;
            return true;
        }

        if (button == 1)
        {
            type = isDown ? 3u : 4u;
            return true;
        }

        if (button >= 2)
        {
            type = isDown ? 25u : 26u;
            return true;
        }

        type = 0;
        return false;
    }

    public static bool TryMapRawMouseDragType(int button, out uint type)
    {
        if (button == 0)
        {
            type = 6u;
            return true;
        }

        if (button == 1)
        {
            type = 7u;
            return true;
        }

        if (button >= 2)
        {
            type = 27u;
            return true;
        }

        type = 0;
        return false;
    }
}
