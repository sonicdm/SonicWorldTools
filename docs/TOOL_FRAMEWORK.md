# Tool runtime and ownership

SonicWorldTools tools run inside Valheim and Infinity Hammer's input loop. To keep one tool from leaking input into another, shared infrastructure owns polling, session ownership, and interception of vanilla actions. A tool module owns only its feature state and behavior.

## Responsibilities

| Area | Owns | Does not own |
| --- | --- | --- |
| `Plugin` | Composition root: config, Harmony install, integrations, and registering modules | Tool behavior or per-frame input rules |
| `ToolInput` | Once-per-frame cached keyboard, mouse, named ZInput buttons, and wheel; primes configured bindings before vanilla player update | Which tool consumes an input |
| `ToolRuntime` | Once-per-frame local-player, build-mode, UI-focus, and IH selection snapshot | Tool activation or selection policy |
| `IToolSession` / `ToolSessionCoordinator` | Exclusive ownership and stop/replacement lifecycle for modal world-input tools | Selection detection or feature state transitions |
| `ToolActionGate` | Routes vanilla player actions through the active session and narrowly scoped claims; allows owned batch placement through the placement patches | Calling tool behavior or deciding which prefabs to place |
| `ToolInputRouter` | Early callbacks that must run before `Player.Update` consumes an input | Polling, feature state, or regular per-frame ticking |
| `VanillaActionPatches` / `PlayerToolInputPatch` | Thin Harmony adapters from game callbacks into the shared runtime | Feature-specific conditions |
| Tool modules | Activation conditions, feature state, previews, placement, and tool-specific UI | Installing player Harmony patches or independently replacing shared input polling |
| `ToolIdentityTracker` / integration bridges | Discovering the selected IH tool and adapting IH/WEC APIs | Owning modal input once a module is active |

## Lifecycle

1. The composition root registers each modal tool session once and registers any action claims under stable owner IDs.
2. A tool checks its own activation conditions. When it becomes modal, it calls `ToolSessionCoordinator.Acquire(this)`; a different owner is stopped before ownership changes.
3. The owner handles its state machine from the module tick. Its `ShouldSuppressPlayerAction` policy states which vanilla actions must be blocked while the module owns input.
4. Deactivation clears the module's transient state and releases its session. Tool replacement, unregister, and plugin teardown use the same stop path.
5. Truly momentary actions that can run without a modal session register an action claim. Claims must be cheap, side-effect-aware, and return true only while they own that input.

## Adding a tool

- Keep feature state and rendering in the tool's module/controller.
- Read physical input through `ToolInput`; read shared player/build/UI/IH state through `ToolRuntime`.
- Use an `IToolSession` for modal world interactions. Acquire/release it at explicit activation boundaries and make `StopSession` safe to call more than once.
- Register narrow vanilla-action claims in `Plugin` only when the input is momentary and cannot be represented by the active session policy.
- Add an early-update handler only when a key must be observed before the game's normal update consumes it. The handler should delegate to the module and do no polling of its own.
- Route vanilla game callbacks through `ToolActionGate`; do not add another feature-specific patch for the same player action.
- Wrap plugin-owned batch placement in `using (ToolActionGate.BeginTrustedPlacement())` so the bypass always closes, including on exceptions.
- Keep IH/WEC reflection and compatibility handling in `Integration`; modules should use the bridge API rather than probing external types.
- On teardown, unregister handlers/claims and release the session. Make cleanup idempotent so hot reload and repeated tool selection are safe.

## Input ownership rules

- Only the local player is eligible for custom input handling.
- Text entry blocks tool commands. `PlayerToolInputPatch` primes configured bindings before vanilla player update, so later module ticks and Harmony actions see the same edge even if Valheim or IH read it first.
- The action gate fails safely around individual policies: exceptions are logged and do not escape into Valheim's Harmony callback.
- A trusted placement allowance is a disposable scope, not a persistent global mode.
- Session policy is exclusive. A module must not infer ownership solely from a selected tool name after it has entered an explicit modal state.

## Current boundaries

Line Tool and Precision Select use exclusive sessions. Scatter Props remains a selected-tool action with a narrowly registered remove-action claim for its sample button. Its placement/sample list logic remains inside the Scatter module. This is an intentional migration seam: any future modal Scatter workflow should become a session rather than expanding the shared gate with more special cases.
