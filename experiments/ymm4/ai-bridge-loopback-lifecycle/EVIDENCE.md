# Evidence — YMM4 AI Bridge Loopback Lifecycle

Status: **PASS**

## Provenance

- source HEAD: 9904077d7723cd51631901832394f8c10214698c
- PR synthetic checkout: c03a0c597d9824401e755d74f02295359199fd1b
- workflow run: 36026473617
- job: 107724170705
- artifact ID: 10820085389
- artifact digest: sha256:cd5f1003b925a29e24ac94f522a08d5030b8400e717e62170f16105b0aeff7a1
- probe DLL SHA256: d6d3fe284eebe9b20ef8b200b6206ad16901d01c46c2b8fcfac80d4938bf6cc1
- YMM4: 4.56.1.0 Lite
- YMM4 ZIP SHA256: 49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a

Run URL:
https://github.com/ziro-lab/chat-native-work-lab-001/actions/runs/36026473617

## Native automated observation

The real YMM4 process loaded the probe and produced the following machine-checked result:

    status=PASS
    startup_seen=True
    address=127.0.0.1
    listener_start_count=1
    ping_response=PONG
    graceful_process_exit=True
    shutdown_reason=application-exit
    shutdown_listener_start_count=1
    port_closed_after_exit=True

Additional startup evidence:

- SetCulture callback count at startup: 1
- WPF Application.Current present: True
- listener address: 127.0.0.1
- service start count: 1

Shutdown evidence:

- Application.Exit path was observed
- callback count remained 1 in this run
- listener start count remained 1
- old bound port rejected a connection after host exit

## PASS boundary

This proves, on YMM4 4.56.1.0 Lite in the tested GitHub-hosted Windows environment, that a normal YMM4 plugin can:

1. use an ILocalizePlugin callback as an idempotent bootstrap entry;
2. create a long-lived background service inside the YMM4 process;
3. bind that service explicitly to IPv4 loopback only;
4. receive and answer an external local-process request;
5. observe normal WPF Application.Exit during graceful YMM4 shutdown;
6. stop the listener so the endpoint no longer accepts connections after exit.

## NOT PROVEN

This result does not prove:

- MCP, JSON-RPC, HTTP or Streamable HTTP compatibility;
- behavior if SetCulture is invoked again after a language change;
- a fixed-port or port-discovery policy;
- firewall behavior on end-user systems;
- YMM4 project/timeline/selection read APIs;
- mutations, Undo/Redo, preview refresh or stale-state handling;
- Tool ViewModel lifecycle;
- product packaging or real MCP-client UX;
- compatibility with another YMM4 version.

## Downstream implication

For YMM4 AI Support, a separate Server.exe is not required merely to obtain a long-lived local service lifecycle. An embedded AI Bridge can reasonably use an idempotent plugin bootstrap plus WPF Application.Exit shutdown, while the actual MCP transport remains a separate verification step.
