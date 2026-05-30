# Debugging & Instrumentation Toolkit

All of Black Ice's protocol logic is managed C# on the Mono runtime
(`mono-2.0-bdwgc.dll`, Unity 2020.3.49, CLR 4.x, 64-bit). Native modules
(`steam_api64`, XInput, InControl) do not touch netcode. Tools are ranked by how much they
earn their keep on this target.

## Primary — managed
- **dnSpyEx 6.5.1** (`third-party/dnSpyEx/dnSpy.exe`): decompile + attach + breakpoint in C#.
  Attach: Debug > Attach to Process > select `Black Ice` (Unity/Mono). See
  [Mono soft debugger](#mono-soft-debugger) below for enabling the agent.
- **BepInEx 5.4.23.5** (installed in `<GAME>/BepInEx`): plugin host for the op-logger.
  Confirmed loading via doorstop (`winhttp.dll` + `doorstop_config.ini`); logs to
  `<GAME>/BepInEx/LogOutput.log`. HarmonyX (`0Harmony.dll`) ships in `BepInEx/core`.

## Secondary — native / dynamic
- **GDB 17.2 + gdbserver** (already on PATH): native attach for `steam_api64`, the Mono
  runtime, and crash triage; the remote-attach workflow is reused for the future C# server.
  See [GDB attach](#gdb-native-attach) below. Native frames only — not game logic.
- **Frida 17.9.11** (frida-tools 14.8.2, via pip): dynamic native+managed tracing.
  `frida-trace -n "Black Ice.exe"`.

## Capture / analysis
- **tshark/Wireshark**: UDP transport framing — see `tools/capture/capture.ps1`.
- **ilspycmd 8.2.0.7535** (global dotnet tool): static decompile to `<GAME>` DLLs ->
  `decompiled/` (analysis-only, gitignored).

## Environment notes
- `curl` downloads on this machine require `--ssl-no-revoke` (schannel revocation-check
  failure). NuGet/`dotnet` restore is unaffected.

## Mono soft debugger
*(Recipe verified in Task 3 — fill in the method that worked.)*

## GDB native attach
*(Recipe verified in Task 3 — fill in the exact pid command + observed output.)*

## Future server (Phase 1+)
- `dotnet-trace` / `dotnet-dump` + lldb-SOS for managed debugging; gdb/gdbserver for native
  crashes and remote attach on a Linux host.
