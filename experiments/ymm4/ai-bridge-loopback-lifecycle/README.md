# YMM4 AI Bridge Loopback Lifecycle

## Goal

Answer one narrow host question needed by downstream YMM4 AI Support:

> Can a normal YMM4 plugin bootstrap one long-lived loopback service inside the YMM4 process, answer an external request, and stop the service during a graceful YMM4 shutdown?

This experiment tests the service/lifecycle substrate only. It does not implement MCP.

## Environment

- GitHub-hosted Windows runner
- YMM4 4.56.1.0 Lite
- .NET 10
- exact YMM4 archive SHA256: 49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a

YMM4 is downloaded from the official GitHub release during the workflow and is not redistributed.

## Probe shape

The probe is a minimal ILocalizePlugin.

On the first SetCulture() callback it:

1. starts a TcpListener bound explicitly to IPAddress.Loopback with an ephemeral port;
2. records the actual bound endpoint and callback/start counters;
3. answers a single-line PING request with PONG;
4. registers graceful shutdown handlers;
5. makes repeated SetCulture() calls idempotent for listener startup.

The runner then connects from outside the YMM4 process, requires PONG, closes YMM4 through its real top-level window, and requires a shutdown marker.

## PASS assertions

PASS requires:

1. the exact YMM4 archive hash matches;
2. the probe builds with warnings as errors;
3. real YMM4 loads the probe;
4. the service starts exactly once;
5. the bound endpoint is 127.0.0.1, not a wildcard/LAN address;
6. an external process can connect and receive PONG for PING;
7. YMM4 exits through the normal window-close route;
8. the plugin observes graceful shutdown and stops the listener;
9. the old port no longer accepts a connection after shutdown.

## PASS means

A PASS is evidence that an embedded AI-bridge-style local service can live inside the tested YMM4 process without a separate server executable, at least for a minimal loopback TCP listener.

It also establishes a tested lifecycle pattern for downstream product design:

    ILocalizePlugin callback
      -> idempotent service bootstrap
      -> background loopback service
      -> Application.Exit / process shutdown
      -> listener disposal

## NOT PROVEN

This experiment does not prove:

- MCP protocol compatibility;
- HTTP / Streamable HTTP behavior;
- a fixed-port policy;
- firewall behavior on user machines;
- YMM4 timeline/project read access;
- mutation, Undo/Redo or preview refresh;
- Tool ViewModel lifecycle;
- language-change behavior beyond idempotent repeated callback handling;
- compatibility with YMM4 versions other than 4.56.1.0 Lite;
- product packaging or install UX.

## Downstream

Consumer: ziro-lab/ymm4-plugin-garage PR #11, YMM4 AI Support.

The product should consume only the verified lifecycle facts, not copy this probe implementation wholesale.
