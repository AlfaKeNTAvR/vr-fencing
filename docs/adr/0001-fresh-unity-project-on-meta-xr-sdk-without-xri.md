# Fresh Unity project on Meta XR SDK, without Unity XR Interaction Toolkit

No open-source fencing project was worth forking (all unlicensed or abandoned), so the app is a fresh Unity 6 LTS (6000.0.85f1) project in `unity/`, built on Unity OpenXR plus Meta's XR SDKs (Core, MR Utility Kit, Movement, Haptics) and deliberately **without** Unity's XR Interaction Toolkit (XRI), even though Unity's MR template and most fencing prototypes use XRI. The blade only needs the controller pose, not a grab/interaction framework, and Meta's stack is what the agent tooling (Meta XR Operator needs Core SDK v207+), body tracking (IOBT) and Meta's Quest skills target; carrying XRI too would mean two overlapping input stacks. The cost is lock-in to Quest, which is acceptable for a Quest-only app.

## Consequences

- Unlicensed fencing repos (e.g. FencingVR-Redo's touch rule, Shannju's blade-bend visual) are read for ideas only and rewritten; MIT/Apache code such as Meta samples may be copied with attribution.
- Agent tooling from day one: Meta's `agentic-tools` plugin (`metavr`), CoplayDev unity-mcp pinned to a release, and batchmode build/test scripts as a fallback that does not depend on any MCP server.
