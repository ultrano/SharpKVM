using Avalonia.Threading;
using SharpHook.Native;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SharpKVM
{
    public partial class MainWindow
    {
        private void TriggerMacMissionControl(KeyCode code)
        {
            if (!MacInputMapping.TryMapMissionControlArrowKeyCode(code, out int macCode))
            {
                return;
            }

            if ((DateTime.Now - _lastMacShortcutTime).TotalMilliseconds < MAC_SHORTCUT_COOLDOWN_MS)
            {
                return;
            }

            _lastMacShortcutTime = DateTime.Now;

            _ = Task.Run(() =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "osascript",
                        Arguments = $"-e \"tell application \\\"System Events\\\" to key code {macCode} using control down\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    Dispatcher.UIThread.Post(() => Log($"Mac Shortcut Triggered ({macCode})"));
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => Log($"Mac script error: {ex.Message}"));
                }
            });
        }

        private void RefreshMacInputSourceHotkeysIfNeeded()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            if ((DateTime.UtcNow - _lastMacInputSourceHotkeyRefresh).TotalSeconds < MAC_HOTKEY_REFRESH_INTERVAL_SEC) return;
            _lastMacInputSourceHotkeyRefresh = DateTime.UtcNow;

            if (!MacInputSourceHotkeyProvider.TryLoad(out var hotkeys))
            {
                _macInputSourceHotkeys = null;
                return;
            }

            _macInputSourceHotkeys = hotkeys;
        }

        private bool TryHandleMacInputSourceHotkey(KeyCode triggerKey)
        {
            bool capsLockEnabled = IsMacCapsLockInputSourceSwitchEnabled();
            bool isDiagnosticKey = IsMacInputDiagnosticKey(triggerKey);
            if (isDiagnosticKey)
            {
                Log($"[MacInput][RX] TryHandle trigger={triggerKey} pressed=[{FormatKeySet(_remotePressedKeys)}] consumed=[{FormatKeySet(_consumedInputSourceKeys)}] capsOption={capsLockEnabled} hotkeysLoaded={_macInputSourceHotkeys != null}");
            }

            if (triggerKey == KeyCode.VcCapsLock && capsLockEnabled)
            {
                var modifierMask = MacInputSourceHotkeyMapper.ToModifierMask(_remotePressedKeys, triggerKey);
                if (modifierMask == MacModifierMask.None)
                {
                    if (!MacInputSourceSwitcher.ExecuteCapsLockToggle())
                    {
                        Log($"[MacInput][RX] CapsLock toggle execution failed: {MacInputSourceSwitcher.LastError}");
                        return false;
                    }

                    ConsumeRemotePressedKeysForInputSourceHotkey("capslock_direct_toggle");
                    Log("Input Source Hotkey Triggered (CapsLock)");
                    return true;
                }

                if (isDiagnosticKey)
                {
                    Log($"[MacInput][RX] CapsLock direct toggle skipped due to modifiers={modifierMask}");
                }
            }
            else if (triggerKey == KeyCode.VcCapsLock && isDiagnosticKey)
            {
                Log("[MacInput][RX] CapsLock input source option is disabled; skipping direct toggle.");
            }

            if (_macInputSourceHotkeys == null)
            {
                if (isDiagnosticKey)
                {
                    Log("[MacInput][RX] Hotkeys not loaded; skipping symbolic hotkey matching.");
                }
                return false;
            }

            foreach (var hotkey in _macInputSourceHotkeys.Enumerate())
            {
                if (isDiagnosticKey && hotkey.TriggerKey == triggerKey)
                {
                    Log($"[MacInput][RX] Candidate {DescribeMacHotkey(hotkey)}");
                }
                if (!hotkey.Matches(_remotePressedKeys, triggerKey)) continue;
                if (hotkey.IsCapsLockPlainSwitch && !capsLockEnabled)
                {
                    if (isDiagnosticKey)
                    {
                        Log($"[MacInput][RX] Candidate matched but blocked by caps option: {DescribeMacHotkey(hotkey)}");
                    }
                    continue;
                }
                if (!MacInputSourceSwitcher.Execute(hotkey))
                {
                    Log($"[MacInput][RX] Hotkey execute failed: {DescribeMacHotkey(hotkey)}, error={MacInputSourceSwitcher.LastError}");
                    return false;
                }
                ConsumeRemotePressedKeysForInputSourceHotkey($"symbolic_{hotkey.SymbolicHotkeyId}");

                Log($"Input Source Hotkey Triggered ({hotkey.SymbolicHotkeyId})");
                return true;
            }

            if (isDiagnosticKey)
            {
                Log($"[MacInput][RX] No input source hotkey matched for trigger={triggerKey}.");
            }

            return false;
        }

        private static string DescribeMacHotkey(MacInputSourceHotkey hotkey)
        {
            return $"id={hotkey.SymbolicHotkeyId},name={hotkey.Name},trigger={hotkey.TriggerKey},required={hotkey.RequiredModifiers},vkey={hotkey.MacVirtualKeyCode},flags=0x{hotkey.MacModifierFlags:X}";
        }

        private void ConsumeRemotePressedKeysForInputSourceHotkey(string reason)
        {
            foreach (var key in _remotePressedKeys)
            {
                _consumedInputSourceKeys.Add(key);
                bool releasedForwardedKey = _forwardedRemoteKeys.Remove(key);
                if (releasedForwardedKey)
                {
                    _simulator?.SimulateKeyRelease(key);
                }

                if (IsMacInputDiagnosticKey(key))
                {
                    Log($"[MacInput][RX] consume key={key} reason={reason} releasedForwarded={releasedForwardedKey}");
                }
            }
        }

        private bool IsMacCapsLockInputSourceSwitchEnabled()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return false;
            if (_chkMacCapsLockInputSourceSwitch == null)
            {
                return _macInputSourceHotkeys?.IsCapsLockInputSourceSwitchEnabled ?? _macCapsLockInputSourceSwitchEnabled;
            }
            return _macCapsLockInputSourceSwitchEnabled;
        }

        private void LogMacAccessibilityStatusIfNeeded(bool force = false)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            if (!force && (DateTime.UtcNow - _lastMacAccessibilityStatusLogTime).TotalSeconds < MAC_ACCESSIBILITY_LOG_INTERVAL_SEC) return;
            _lastMacAccessibilityStatusLogTime = DateTime.UtcNow;

            bool trusted = MacAccessibilityDiagnostics.IsAccessibilityTrusted();
            Log($"[MacInput][Access] AXIsProcessTrusted={trusted}");
            if (!trusted)
            {
                Log("[MacInput][Access] macOS Accessibility permission is not granted. Keyboard/mouse injection may fail.");
            }
        }

        private void SimulateInput(InputPacket p)
        {
            try
            {
                bool isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
                switch (p.Type)
                {
                    case PacketType.MouseMove:
                        SimulateMouseMovePacket(p, isMacOS);
                        break;

                    case PacketType.MouseDown:
                        SimulateMouseButtonPacket(p, isButtonDown: true, isMacOS);
                        break;

                    case PacketType.MouseUp:
                        SimulateMouseButtonPacket(p, isButtonDown: false, isMacOS);
                        break;

                    case PacketType.MouseWheel:
                        _simulator?.SimulateMouseWheel((short)p.KeyCode);
                        break;

                    case PacketType.KeyDown:
                    case PacketType.KeyUp:
                        SimulateKeyboardPacket(p, isMacOS);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"[SimulateInput][Error] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void SimulateMouseMovePacket(InputPacket p, bool isMacOS)
        {
            if (isMacOS)
            {
                if (_isLeftDragging)
                {
                    CursorManager.SendMacRawDrag(p.X, p.Y, 0);
                }
                else
                {
                    CursorManager.SendMacRawMove(p.X, p.Y);
                }
            }
            else
            {
                _simulator?.SimulateMouseMovement((short)p.X, (short)p.Y);
            }

            _currentClientX = p.X;
            _currentClientY = p.Y;
        }

        private void SimulateMouseButtonPacket(InputPacket p, bool isButtonDown, bool isMacOS)
        {
            if (p.KeyCode == (int)MouseButton.Button1)
            {
                _isLeftDragging = isButtonDown;
            }

            bool hasAbsolutePosition = p.X >= 0 && p.Y >= 0;
            if (isMacOS && hasAbsolutePosition)
            {
                CursorManager.SendMacRawClick(p.X, p.Y, (int)p.KeyCode - 1, isButtonDown, p.ClickCount);
                return;
            }

            if (hasAbsolutePosition)
            {
                _simulator?.SimulateMouseMovement((short)p.X, (short)p.Y);
            }

            if (isButtonDown)
            {
                _simulator?.SimulateMousePress((MouseButton)p.KeyCode);
            }
            else
            {
                _simulator?.SimulateMouseRelease((MouseButton)p.KeyCode);
            }
        }

        private void SimulateKeyboardPacket(InputPacket p, bool isMacOS)
        {
            var code = (KeyCode)p.KeyCode;
            bool isKeyDown = p.Type == PacketType.KeyDown;
            bool isDiagnosticKey = IsMacInputDiagnosticKey(code);

            if (isMacOS && HandleMacSpecificKeyboardPacket(code, isKeyDown, isDiagnosticKey))
            {
                return;
            }

            if (isKeyDown)
            {
                _simulator?.SimulateKeyPress(code);
                if (isMacOS)
                {
                    _forwardedRemoteKeys.Add(code);
                    if (isDiagnosticKey)
                    {
                        Log($"[MacInput][RX] Forwarded KeyDown to simulator: {code}");
                    }
                }
                return;
            }

            bool shouldRelease = !isMacOS || _forwardedRemoteKeys.Remove(code);
            if (shouldRelease)
            {
                _simulator?.SimulateKeyRelease(code);
                if (isDiagnosticKey)
                {
                    Log($"[MacInput][RX] Forwarded KeyUp to simulator: {code}");
                }
            }
            else if (isDiagnosticKey)
            {
                Log($"[MacInput][RX] KeyUp skipped (not in forwarded set): {code}");
            }
        }

        private bool HandleMacSpecificKeyboardPacket(KeyCode code, bool isKeyDown, bool isDiagnosticKey)
        {
            if (isDiagnosticKey)
            {
                LogMacAccessibilityStatusIfNeeded();
            }

            if (isKeyDown)
            {
                _remotePressedKeys.Add(code);
            }
            else
            {
                _remotePressedKeys.Remove(code);
            }

            if (isDiagnosticKey)
            {
                Log($"[MacInput][RX] packet={(isKeyDown ? PacketType.KeyDown : PacketType.KeyUp)} code={code} remotePressed=[{FormatKeySet(_remotePressedKeys)}] forwarded=[{FormatKeySet(_forwardedRemoteKeys)}] consumed=[{FormatKeySet(_consumedInputSourceKeys)}]");
            }

            RefreshMacInputSourceHotkeysIfNeeded();

            if (!isKeyDown && _consumedInputSourceKeys.Remove(code))
            {
                if (isDiagnosticKey)
                {
                    Log($"[MacInput][RX] KeyUp consumed by input-source handler: {code}");
                }
                return true;
            }

            if (isKeyDown && TryHandleMacInputSourceHotkey(code))
            {
                if (isDiagnosticKey)
                {
                    Log($"[MacInput][RX] KeyDown handled by input-source handler: {code}");
                }
                return true;
            }

            if (code == KeyCode.VcLeftControl || code == KeyCode.VcRightControl)
            {
                _isRemoteCtrlDown = isKeyDown;
                if (isDiagnosticKey)
                {
                    Log($"[MacInput][RX] RemoteCtrl state={_isRemoteCtrlDown}");
                }
            }

            if (isKeyDown && _isRemoteCtrlDown && MacInputMapping.TryMapMissionControlArrowKeyCode(code, out _))
            {
                Log($"[MacInput][RX] Trigger mission control via Ctrl+Arrow ({code})");
                TriggerMacMissionControl(code);
                return true;
            }

            return false;
        }
    }
}
