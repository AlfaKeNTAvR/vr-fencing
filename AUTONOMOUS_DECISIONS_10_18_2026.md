# Autonomous decisions, 2026-10-18 (PER-26, Unity skeleton + agent dev loop)

Decisions taken without Nikita, for review. Open questions at the end.

1. **Package source: Unity's registry, not Meta's npm registry.** Meta publishes Core/MRUK/Haptics 207.0.1 on packages.unity.com (also mirrored on npm.developer.oculus.com). Unity's registry needs no `scopedRegistries` block. Movement SDK is not on either registry; it comes from Meta's GitHub repo `oculus-samples/Unity-Movement` pinned to tag `v207.0.0`.
2. **URP, not the Built-in pipeline.** Meta's recommended pipeline for Quest; switching later costs more than starting with it. Settings: no HDR, 4x MSAA, no depth/opaque textures.
3. **OpenXR enabled for Windows too**, not only Android, so Play mode over Link and the Meta XR Simulator work (needed for Meta XR Operator in the Editor).
4. **Passthrough support = Required** in the Meta project config (the app is passthrough-only), target devices Quest 3 and 3S.
5. **Experimental features ON** in the Meta project config. Meta XR Operator needs the `com.oculus.experimental.enabled` manifest entry on Horizon OS v205+. Meta's tool writes it with `required="true"`. Must be switched off before any store submission.
6. **Android package id `com.alfakentavr.vrfencing`**, product name "VR Fencing", company "AlfaKeNTAvR". Rename is cheap now, expensive after a store listing.
7. **Min API 32, target API 34**, IL2CPP, ARM64 only, Vulkan only (Meta Project Setup Tool rules).
8. **Project Setup Tool "Fix All" runs headless with one exclusion.** The "install newest Meta XR Simulator" fix downloads a ~1 GB MSI into Downloads and opens Explorer; it is skipped. Meta only exposes an unfiltered Fix All, so the filtered internal method is called by reflection (throws, rather than running unfiltered, if Meta renames it).
9. **Unity MCP over HTTP** (`http://localhost:8080/mcp` in `.mcp.json`), the package default. The Unity package starts its own pinned server through `uvx`. Alternative was stdio, which needs the same one-time toggle in the Editor.
10. **`metavr` pinned to 1.8.1** in `.mcp.json`, run through `cmd /c npx` (Windows needs the `cmd /c` wrapper for npx).
11. **`DevAgentSettings.asset` gitignored.** Meta generates it with this PC's IP address and an access token; the repo is public.
12. **The one Edit-mode test checks that the passthrough scene is the first build scene**, rather than `Assert.True(true)`, so it also guards something the build relies on.

## Open questions for Nikita

- Keep the reference cube in the passthrough scene, or remove once the blade exists?
- Package id OK, or a different reverse-domain?
- The Project Setup Tool also applied "OVRManager requests the Scene permission at startup" (MRUK). Meta suggests managing the permission yourself later; fine for the prototype?
