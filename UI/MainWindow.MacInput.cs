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
        private static readonly bool _traceMacInputSource = string.Equals(
            Environment.GetEnvironmentVariable("SHARP_KVM_MAC_INPUT_TRACE")?.Trim(),
            "1",
            StringComparison.OrdinalIgnoreCase);

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
            bool isDiagnosticKey = IsMacInputDiagnosticKey(triggerKey);
            bool isCapsLikeTrigger = IsCapsInputSourceToggleKey(triggerKey);
            KeyCode effectiveTriggerKey = GetEffectiveCapsLikeTriggerKey(triggerKey);
            TraceMacInputSource($"TryHandle start trigger={triggerKey} effective={effectiveTriggerKey} pressed=[{FormatKeySet(_remotePressedKeys)}] capsLike={isCapsLikeTrigger} hotkeysLoaded={_macInputSourceHotkeys != null}");
            if (isDiagnosticKey)
            {
                Log($"[MacInput][RX] TryHandle trigger={triggerKey} effective={effectiveTriggerKey} pressed=[{FormatKeySet(_remotePressedKeys)}] consumed=[{FormatKeySet(_consumedInputSourceKeys)}] hotkeysLoaded={_macInputSourceHotkeys != null}");
            }

            if (isCapsLikeTrigger)
            {
                var modifierMask = MacInputSourceHotkeyMapper.ToModifierMask(_remotePressedKeys, effectiveTriggerKey);
                if (modifierMask == MacModifierMask.None)
                {
                    var beforeSnapshot = MacInputSourceStateProbe.Capture();
                    if (!MacInputSourceSwitcher.ExecuteCapsLockToggle())
                    {
                        Log($"[MacInput][RX] CapsLock toggle execution failed ({triggerKey}): {MacInputSourceSwitcher.LastError}");
                        TraceMacInputSource($"Direct toggle failed trigger={triggerKey} error={MacInputSourceSwitcher.LastError}");
                        ReportMacInputSourceVerificationFailure(
                            triggerKey,
                            "capslock_direct_toggle",
                            $"toggle_failed:{MacInputSourceSwitcher.LastError}",
                            beforeSnapshot);
                        ConsumeRemotePressedKeysForInputSourceHotkey("capslock_direct_toggle_failed");
                        VerifyAndReportMacInputSourceSwitchAsync(triggerKey, "capslock_direct_toggle", beforeSnapshot);
                        return true;
                    }

                    ConsumeRemotePressedKeysForInputSourceHotkey($"capslock_direct_toggle_{triggerKey}");
                    Log("Input Source Hotkey Triggered (CapsLock)");
                    TraceMacInputSource($"Direct toggle executed trigger={triggerKey} error={MacInputSourceSwitcher.LastError}");
                    VerifyAndReportMacInputSourceSwitchAsync(triggerKey, "capslock_direct_toggle", beforeSnapshot);
                    return true;
                }

                TraceMacInputSource($"Direct toggle skipped; trigger={triggerKey} modifierMask={modifierMask}");
                if (isDiagnosticKey)
                {
                    Log($"[MacInput][RX] CapsLock direct toggle skipped due to modifiers={modifierMask}");
                }
            }

            if (_macInputSourceHotkeys == null)
            {
                if (isDiagnosticKey)
                {
                    Log("[MacInput][RX] Hotkeys not loaded; skipping symbolic hotkey matching.");
                }
                TraceMacInputSource($"Symbolic matching skipped; no hotkeys loaded for trigger={triggerKey}");
                return false;
            }

            foreach (var hotkey in _macInputSourceHotkeys.Enumerate())
            {
                if (isDiagnosticKey && hotkey.TriggerKey == effectiveTriggerKey)
                {
                    Log($"[MacInput][RX] Candidate {DescribeMacHotkey(hotkey)}");
                }
                if (!hotkey.Matches(_remotePressedKeys, effectiveTriggerKey)) continue;
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

            TraceMacInputSource($"No symbolic hotkey matched for trigger={triggerKey} effective={effectiveTriggerKey}");
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

                    if (IsCapsInputSourceToggleKey(triggerKey) &&
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
                bool loaded = MacInputSourceHotkeyProvider.TryLoadWithDiagnostics(out var reloadedHotkeys, out var diagnostics);
                if (loaded && reloadedHotkeys != null)
                {
                    hotkeys = reloadedHotkeys;
                    _macInputSourceHotkeys = reloadedHotkeys;
                    _lastMacInputSourceHotkeyRefresh = DateTime.UtcNow;
                }

                if (hotkeys == null)
                {
                    string unavailableMessage =
                        $"[MacInput][Verify] trigger={triggerKey} route=capslock_fallback_symbolic result=skipped_no_hotkeys status={diagnostics.Status} option_source={diagnostics.CapsLockOptionSource} details={diagnostics.Details}";
                    Dispatcher.UIThread.Post(() => Log(unavailableMessage));
                    SendClientDiagnosticLogToServer(unavailableMessage);
                    await TryFallbackViaDefaultCtrlSpaceAsync(triggerKey, baselineSnapshot, $"no_hotkeys:{diagnostics.Status}").ConfigureAwait(false);
                    return;
                }
            }

            if (hotkeys == null)
            {
                // Defensive guard (unreachable by design).
                string unavailableMessage = $"[MacInput][Verify] trigger={triggerKey} route=capslock_fallback_symbolic result=skipped_no_hotkeys_after_reload";
                Dispatcher.UIThread.Post(() => Log(unavailableMessage));
                SendClientDiagnosticLogToServer(unavailableMessage);
                await TryFallbackViaDefaultCtrlSpaceAsync(triggerKey, baselineSnapshot, "no_hotkeys_after_reload").ConfigureAwait(false);
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
                await TryFallbackViaDefaultCtrlSpaceAsync(triggerKey, baselineSnapshot, "no_candidate").ConfigureAwait(false);
                return;
            }

            string noSwitchMessage = $"[MacInput][Verify] trigger={triggerKey} route=capslock_fallback_symbolic result=no_switch";
            Dispatcher.UIThread.Post(() => Log(noSwitchMessage));
            SendClientDiagnosticLogToServer(noSwitchMessage);
            await TryFallbackViaDefaultCtrlSpaceAsync(triggerKey, baselineSnapshot, "symbolic_no_switch").ConfigureAwait(false);
        }

        private async Task TryFallbackViaDefaultCtrlSpaceAsync(KeyCode triggerKey, MacInputSourceSnapshot baselineSnapshot, string reasonContext)
        {
            const string fallbackRoute = "capslock_fallback_default_ctrl_space";
            if (!MacInputSourceSwitcher.ExecuteControlSpaceFallback())
            {
                string executeFailMessage =
                    $"[MacInput][Verify] trigger={triggerKey} route={fallbackRoute} before={baselineSnapshot.ToLogValue()} after=n/a switched=false reason=execute_failed:{MacInputSourceSwitcher.LastError};context={reasonContext}";
                Dispatcher.UIThread.Post(() => Log(executeFailMessage));
                SendClientDiagnosticLogToServer(executeFailMessage);
                return;
            }

            var fallbackResult = await CaptureSwitchResultAsync(baselineSnapshot).ConfigureAwait(false);
            string switchedText = fallbackResult.Switched.HasValue ? (fallbackResult.Switched.Value ? "true" : "false") : "unknown";
            string fallbackMessage =
                $"[MacInput][Verify] trigger={triggerKey} route={fallbackRoute} before={baselineSnapshot.ToLogValue()} after={fallbackResult.AfterSnapshot.ToLogValue()} switched={switchedText};context={reasonContext}";
            Dispatcher.UIThread.Post(() => Log(fallbackMessage));
            SendClientDiagnosticLogToServer(fallbackMessage);
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

        internal static bool IsCapsInputSourceToggleKey(KeyCode code)
        {
            return code == KeyCode.VcCapsLock || code == KeyCode.VcHangul;
        }

        internal static KeyCode GetEffectiveCapsLikeTriggerKey(KeyCode code)
        {
            return code == KeyCode.VcHangul
                ? KeyCode.VcCapsLock
                : code;
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

        private void TraceMacInputSource(string message)
        {
            if (!_traceMacInputSource)
            {
                return;
            }

            Log($"[MacInput][RX][TRACE] {message}");
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
            TraceMacInputSource($"Handle start code={code} isKeyDown={isKeyDown} isDiagnostic={isDiagnosticKey} remotePressed(before)=[{FormatKeySet(_remotePressedKeys)}] consumed=[{FormatKeySet(_consumedInputSourceKeys)}] forwarded=[{FormatKeySet(_forwardedRemoteKeys)}]");
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
                TraceMacInputSource($"KeyUp consumed by input-source handler: code={code}");
                if (isDiagnosticKey)
                {
                    Log($"[MacInput][RX] KeyUp consumed by input-source handler: {code}");
                }
                return true;
            }

            if (isKeyDown && TryHandleMacInputSourceHotkey(code))
            {
                TraceMacInputSource($"KeyDown handled by input-source handler: code={code}");
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

            TraceMacInputSource($"No input-source handling: code={code} isKeyDown={isKeyDown}");
            return false;
        }
    }
}
