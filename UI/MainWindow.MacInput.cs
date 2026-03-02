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
                    var beforeSnapshot = MacInputSourceStateProbe.Capture();
                    if (!MacInputSourceSwitcher.ExecuteCapsLockToggle())
                    {
                        Log($"[MacInput][RX] CapsLock toggle execution failed: {MacInputSourceSwitcher.LastError}");
                        ReportMacInputSourceVerificationFailure(
                            triggerKey,
                            "capslock_direct_toggle",
                            $"toggle_failed:{MacInputSourceSwitcher.LastError}",
                            beforeSnapshot);
                        return false;
                    }

                    ConsumeRemotePressedKeysForInputSourceHotkey("capslock_direct_toggle");
                    Log("Input Source Hotkey Triggered (CapsLock)");
                    VerifyAndReportMacInputSourceSwitchAsync(triggerKey, "capslock_direct_toggle", beforeSnapshot);
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
                SendClientDiagnosticLogToServer("[MacInput][Verify] trigger=VcCapsLock route=capslock_direct_toggle result=skipped_caps_option_disabled");
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
                    SendClientDiagnosticLogToServer($"[MacInput][Verify] trigger={triggerKey} route=symbolic_{hotkey.SymbolicHotkeyId} result=blocked_caps_option_disabled");
                    continue;
                }
                var beforeSnapshot = MacInputSourceStateProbe.Capture();
                if (!MacInputSourceSwitcher.Execute(hotkey))
                {
                    Log($"[MacInput][RX] Hotkey execute failed: {DescribeMacHotkey(hotkey)}, error={MacInputSourceSwitcher.LastError}");
                    ReportMacInputSourceVerificationFailure(
                        triggerKey,
                        $"symbolic_{hotkey.SymbolicHotkeyId}",
                        $"execute_failed:{MacInputSourceSwitcher.LastError}",
                        beforeSnapshot);
                    return false;
                }
                ConsumeRemotePressedKeysForInputSourceHotkey($"symbolic_{hotkey.SymbolicHotkeyId}");

                Log($"Input Source Hotkey Triggered ({hotkey.SymbolicHotkeyId})");
                VerifyAndReportMacInputSourceSwitchAsync(triggerKey, $"symbolic_{hotkey.SymbolicHotkeyId}", beforeSnapshot);
                return true;
            }

            if (isDiagnosticKey)
            {
                Log($"[MacInput][RX] No input source hotkey matched for trigger={triggerKey}.");
            }

            return false;
        }

        private void VerifyAndReportMacInputSourceSwitchAsync(KeyCode triggerKey, string route, MacInputSourceSnapshot beforeSnapshot)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                await _macInputSourceVerifySemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    var verifyResult = await CaptureSwitchResultAsync(beforeSnapshot).ConfigureAwait(false);
                    string switchedText = verifyResult.Switched.HasValue ? (verifyResult.Switched.Value ? "true" : "false") : "unknown";
                    string message =
                        $"[MacInput][Verify] trigger={triggerKey} route={route} before={beforeSnapshot.ToLogValue()} after={verifyResult.AfterSnapshot.ToLogValue()} switched={switchedText}";

                    Dispatcher.UIThread.Post(() => Log(message));
                    SendClientDiagnosticLogToServer(message);

                    if (triggerKey == KeyCode.VcCapsLock &&
                        string.Equals(route, "capslock_direct_toggle", StringComparison.Ordinal) &&
                        verifyResult.Switched != true)
                    {
                        MacInputSourceSnapshot fallbackBaseline = verifyResult.AfterSnapshot.IsAvailable
                            ? verifyResult.AfterSnapshot
                            : beforeSnapshot;
                        await TryFallbackViaSymbolicHotkeyAsync(triggerKey, fallbackBaseline).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _macInputSourceVerifySemaphore.Release();
                }
            });
        }

        private async Task<(MacInputSourceSnapshot AfterSnapshot, bool? Switched)> CaptureSwitchResultAsync(MacInputSourceSnapshot beforeSnapshot)
        {
            MacInputSourceSnapshot afterSnapshot = MacInputSourceSnapshot.Unavailable("not_sampled");
            bool? switched = null;

            for (int attempt = 0; attempt < MAC_INPUT_SOURCE_VERIFY_MAX_ATTEMPTS; attempt++)
            {
                int delayMs = attempt == 0 ? MAC_INPUT_SOURCE_VERIFY_INITIAL_DELAY_MS : MAC_INPUT_SOURCE_VERIFY_RETRY_DELAY_MS;
                await Task.Delay(delayMs).ConfigureAwait(false);

                afterSnapshot = MacInputSourceStateProbe.Capture();
                if (beforeSnapshot.IsAvailable && afterSnapshot.IsAvailable)
                {
                    switched = !string.Equals(beforeSnapshot.Fingerprint, afterSnapshot.Fingerprint, StringComparison.Ordinal);
                    if (switched == true)
                    {
                        break;
                    }
                }
            }

            return (afterSnapshot, switched);
        }

        private async Task TryFallbackViaSymbolicHotkeyAsync(KeyCode triggerKey, MacInputSourceSnapshot baselineSnapshot)
        {
            var hotkeys = _macInputSourceHotkeys;
            if (hotkeys == null)
            {
                string unavailableMessage = $"[MacInput][Verify] trigger={triggerKey} route=capslock_fallback_symbolic result=skipped_no_hotkeys";
                Dispatcher.UIThread.Post(() => Log(unavailableMessage));
                SendClientDiagnosticLogToServer(unavailableMessage);
                return;
            }

            bool hasCandidate = false;
            foreach (var hotkey in hotkeys.Enumerate())
            {
                if (hotkey.TriggerKey == KeyCode.VcCapsLock)
                {
                    continue;
                }

                hasCandidate = true;
                string fallbackRoute = $"capslock_fallback_symbolic_{hotkey.SymbolicHotkeyId}";
                if (!MacInputSourceSwitcher.Execute(hotkey))
                {
                    string executeFailMessage =
                        $"[MacInput][Verify] trigger={triggerKey} route={fallbackRoute} before={baselineSnapshot.ToLogValue()} after=n/a switched=false reason=execute_failed:{MacInputSourceSwitcher.LastError}";
                    Dispatcher.UIThread.Post(() => Log(executeFailMessage));
                    SendClientDiagnosticLogToServer(executeFailMessage);
                    continue;
                }

                var fallbackResult = await CaptureSwitchResultAsync(baselineSnapshot).ConfigureAwait(false);
                string switchedText = fallbackResult.Switched.HasValue ? (fallbackResult.Switched.Value ? "true" : "false") : "unknown";
                string fallbackMessage =
                    $"[MacInput][Verify] trigger={triggerKey} route={fallbackRoute} before={baselineSnapshot.ToLogValue()} after={fallbackResult.AfterSnapshot.ToLogValue()} switched={switchedText}";
                Dispatcher.UIThread.Post(() => Log(fallbackMessage));
                SendClientDiagnosticLogToServer(fallbackMessage);

                if (fallbackResult.Switched == true)
                {
                    return;
                }
            }

            if (!hasCandidate)
            {
                string noCandidateMessage = $"[MacInput][Verify] trigger={triggerKey} route=capslock_fallback_symbolic result=skipped_no_candidate";
                Dispatcher.UIThread.Post(() => Log(noCandidateMessage));
                SendClientDiagnosticLogToServer(noCandidateMessage);
            }
        }

        private void ReportMacInputSourceVerificationFailure(KeyCode triggerKey, string route, string reason, MacInputSourceSnapshot beforeSnapshot)
        {
            string message =
                $"[MacInput][Verify] trigger={triggerKey} route={route} before={beforeSnapshot.ToLogValue()} after=n/a switched=false reason={reason}";
            Log(message);
            SendClientDiagnosticLogToServer(message);
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
