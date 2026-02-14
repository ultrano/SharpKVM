using SharpHook.Native;
using System.Collections.Generic;

namespace SharpKVM;

[System.Flags]
public enum MacModifierMask
{
    None = 0,
    Shift = 1,
    Control = 2,
    Option = 4,
    Command = 8,
    CapsLock = 16
}

public sealed class MacInputSourceHotkey
{
    public required string Name { get; init; }
    public required int SymbolicHotkeyId { get; init; }
    public required int MacVirtualKeyCode { get; init; }
    public required ulong MacModifierFlags { get; init; }
    public required KeyCode TriggerKey { get; init; }
    public required MacModifierMask RequiredModifiers { get; init; }

    public bool IsCapsLockPlainSwitch =>
        TriggerKey == KeyCode.VcCapsLock &&
        MacVirtualKeyCode == 57 &&
        RequiredModifiers == MacModifierMask.None;

    public bool Matches(IReadOnlyCollection<KeyCode> pressedKeys, KeyCode triggerKey)
    {
        if (triggerKey != TriggerKey)
        {
            return false;
        }

        return MacInputSourceHotkeyMapper.ToModifierMask(pressedKeys, triggerKey) == RequiredModifiers;
    }
}

public sealed class MacInputSourceHotkeys
{
    public MacInputSourceHotkey? Primary { get; init; }
    public MacInputSourceHotkey? Secondary { get; init; }
    public bool IsCapsLockInputSourceSwitchEnabled { get; init; }

    public static bool ComputeCapsLockOptionEnabled(MacInputSourceHotkey? primary, MacInputSourceHotkey? secondary) =>
        (primary?.IsCapsLockPlainSwitch ?? false) ||
        (secondary?.IsCapsLockPlainSwitch ?? false);

    public IEnumerable<MacInputSourceHotkey> Enumerate()
    {
        if (Primary != null) yield return Primary;
        if (Secondary != null) yield return Secondary;
    }
}

public enum MacInputSourceHotkeysLoadStatus
{
    Success,
    NotMacOS,
    PlistNotFound,
    PlutilFailed,
    JsonParseFailed,
    HotkeysRootMissing,
    NoInputSourceHotkeys
}

public sealed class MacInputSourceHotkeysDiagnostics
{
    public required MacInputSourceHotkeysLoadStatus Status { get; init; }
    public required bool IsCapsLockInputSourceSwitchEnabled { get; init; }
    public required string CapsLockOptionSource { get; init; }
    public required string RawOptionKey { get; init; }
    public required string RawOptionValue { get; init; }
    public required string PrimarySummary { get; init; }
    public required string SecondarySummary { get; init; }
    public required string Details { get; init; }
}

public static class MacInputSourceHotkeyMapper
{
    private const ulong CarbonShift = 0x00020000;
    private const ulong CarbonControl = 0x00040000;
    private const ulong CarbonOption = 0x00080000;
    private const ulong CarbonCommand = 0x00100000;
    private const ulong CarbonCapsLock = 0x00010000;

    public static bool TryMapMacVirtualKeyCode(int macVirtualKeyCode, out KeyCode keyCode)
    {
        keyCode = macVirtualKeyCode switch
        {
            49 => KeyCode.VcSpace,
            57 => KeyCode.VcCapsLock,
            _ => (KeyCode)0
        };

        return keyCode != 0;
    }

    public static MacModifierMask ToModifierMask(ulong carbonFlags)
    {
        MacModifierMask mask = MacModifierMask.None;
        if ((carbonFlags & CarbonShift) != 0) mask |= MacModifierMask.Shift;
        if ((carbonFlags & CarbonControl) != 0) mask |= MacModifierMask.Control;
        if ((carbonFlags & CarbonOption) != 0) mask |= MacModifierMask.Option;
        if ((carbonFlags & CarbonCommand) != 0) mask |= MacModifierMask.Command;
        if ((carbonFlags & CarbonCapsLock) != 0) mask |= MacModifierMask.CapsLock;
        return mask;
    }

    public static MacModifierMask ToModifierMask(IReadOnlyCollection<KeyCode> pressedKeys, KeyCode triggerKey)
    {
        MacModifierMask mask = MacModifierMask.None;
        foreach (var key in pressedKeys)
        {
            if (key == triggerKey) continue;

            switch (key)
            {
                case KeyCode.VcLeftShift:
                case KeyCode.VcRightShift:
                    mask |= MacModifierMask.Shift;
                    break;
                case KeyCode.VcLeftControl:
                case KeyCode.VcRightControl:
                    mask |= MacModifierMask.Control;
                    break;
                case KeyCode.VcLeftAlt:
                case KeyCode.VcRightAlt:
                    mask |= MacModifierMask.Option;
                    break;
                case KeyCode.VcLeftMeta:
                case KeyCode.VcRightMeta:
                    mask |= MacModifierMask.Command;
                    break;
                case KeyCode.VcCapsLock:
                    mask |= MacModifierMask.CapsLock;
                    break;
            }
        }
        return mask;
    }
}
