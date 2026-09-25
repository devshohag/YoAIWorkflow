# YoAIWorkflow

**A .NET foundation for AI-assisted business workflows.**

YoAIWorkflow helps applications turn an input and a current state into a new state and a set of requested business actions. Workflow rules live in typed C# code; the application decides how to collect input, save state, and execute actions.

> **Project status:** Early development. Phase 5 adds a provider-independent adapter for typed AI input proposals and business validation. A real model provider, production database adapter, and package release are still planned. Public APIs may change before the first package release.

## Why it exists

The same application may need different workflows: an appointment agent collects a booking request, while an order agent confirms or cancels an order. Those decisions have different rules even if the input arrives through the same voice agent, web application, or API.

YoAIWorkflow provides a shared place for the *decision boundary*. A workflow receives a typed input, evaluates business rules, and returns the next state plus action intents. The host application handles side effects. This approach also makes it possible to validate an AI-produced proposal before any business action is executed.

This project grew out of work on [YoVoiceAgent](https://github.com/devshohag/YoVoiceAgent), but its core does not reference YoVoiceAgent, Asterisk, an LLM provider, a database, or a particular communication channel.

## What works today

- Define a workflow using `IWorkflow<TState, TInput>`.
- Evaluate a transition with `WorkflowEngine<TState, TInput>`.
- Return a `WorkflowTransition<TState>` containing the next state and `WorkflowAction` intents.
- Register named `IWorkflowActionHandler<TState>` implementations and execute actions explicitly after a decision.
- Receive a result showing how many actions completed and where execution stopped if one failed.
- Run the order confirmation sample: confirm, cancel, or request human review.
- Run an appointment booking sample: choose a service, choose a date, then confirm or request human review.
- Run tests for transitions, repeated input, handler selection, failures, and cancellation.
- Persist a session and resume pending actions using `DurableWorkflowRunner` and `FileWorkflowSessionStore`.
- Give durable action handlers stable IDs to deduplicate external effects across retries.
- Accept a typed AI proposal through `YoAIWorkflow.AI`, check its confidence and business rules, then save a decision without dispatching actions automatically.

The workflow engine computes a decision only. Applications can use the existing in-process executor or opt into the durable runner. The sample handlers write to the console; they do not send messages or update orders. The durable runner saves state and action checkpoints between processes.

## Getting started

**Prerequisite:** .NET 10 SDK. Clone the repository and run the following commands from its root:

```bash
dotnet restore YoAIWorkflow.slnx
dotnet build YoAIWorkflow.slnx --no-restore
dotnet test YoAIWorkflow.slnx --no-build
dotnet run --project samples/OrderConfirmation/OrderConfirmation.csproj
dotnet run --project samples/AppointmentBooking/AppointmentBooking.csproj
dotnet run --project samples/PersistentOrderConfirmation/PersistentOrderConfirmation.csproj -- start ORDER-1001
dotnet run --project samples/PersistentOrderConfirmation/PersistentOrderConfirmation.csproj -- confirm ORDER-1001 reply-1
dotnet run --project samples/PersistentOrderConfirmation/PersistentOrderConfirmation.csproj -- show ORDER-1001
dotnet run --project samples/PersistentOrderConfirmation/PersistentOrderConfirmation.csproj -- dispatch ORDER-1001
dotnet run --project samples/AIOrderProposal/AIOrderProposal.csproj -- confirm
dotnet run --project samples/AIOrderProposal/AIOrderProposal.csproj -- maybe
```

In the order sample, enter `1` to confirm, `2` to cancel, or anything else to request human review. In the booking sample, enter a service code, a date in `yyyy-MM-dd` format, and then `1` to confirm or `2` to cancel. Both samples print their state and the action handled by a console-only handler.

The sample uses the SDK like this:

```csharp
using OrderConfirmation;
using YoAIWorkflow.Core;

var engine = new WorkflowEngine<OrderState, CustomerReply>(
    new OrderConfirmationWorkflow());

var result = engine.Process(
    new OrderState("ORDER-1001", OrderStatus.Pending),
    CustomerReply.Confirm);

Console.WriteLine(result.State.Status); // Confirmed
Console.WriteLine(result.Actions[0].Name); // order.confirmed

var executor = new WorkflowActionExecutor<OrderState>(
    [new ConsoleOrderActionHandler("order.confirmed")]);
var report = await executor.ExecuteAsync(result);
Console.WriteLine(report.Succeeded); // True
```

`OrderConfirmationWorkflow`, `OrderState`, `CustomerReply`, and `ConsoleOrderActionHandler` belong to the sample application. A consuming project supplies its own workflow rules and action handlers.

## Two workflows, one SDK

The order sample accepts one customer reply and decides whether an order is confirmed, cancelled, or needs a person. The booking sample collects a service and a date over multiple turns. It does not emit `booking.create-requested` until a valid date has been selected and the user explicitly confirms. A past date is rejected without advancing the booking state.

Both examples use `WorkflowEngine<TState, TInput>`, `WorkflowTransition<TState>`, and `WorkflowActionExecutor<TState>`. Each example owns its state, inputs, transition rules, and action handlers. The booking workflow receives the earliest acceptable date from its host, so its tests do not depend on the machine clock. A real host must choose that date using its business timezone and check actual slot availability before creating an appointment.

`BookingRequested` describes the workflow decision. The booking console example does not create a database booking, reserve a slot, send a confirmation, or save its state. A host can use the Phase 4 persistence interfaces for a workflow of its choice.

## Action execution and failures

Call `Process` to get a transition, then call `ExecuteAsync` only when the host is ready to perform its business actions. The executor checks that every requested action has exactly one named handler before it invokes any handler. Handlers run in order. On the first handler exception, execution stops and the report contains `CompletedCount`, the failed action's index, and the exception. A missing handler produces a failure report without starting any action. Cancellation is propagated to the caller.

The original `WorkflowActionExecutor` remains in-process and does not persist progress. For restart and retry behavior, use the separate runner below.

## Durable workflow sessions (Phase 4)

`DurableWorkflowRunner<TState, TInput>` loads a stored session, applies an input once per caller-supplied `inputId`, and saves the state and pending actions **before** dispatch. Call `DispatchAsync(workflowId)` to execute pending actions; each successful action is checkpointed. A restart can load the session and call `DispatchAsync` again. A new input waits until all actions from the previous input are complete. Each write uses an expected revision so concurrent writers cannot silently overwrite a decision. Input IDs must be stable across upstream retries and unique to each distinct input for a workflow.

The `IWorkflowSessionStore<TState>` contract accepts a custom persistence adapter. `FileWorkflowSessionStore<TState>` writes JSON snapshots to a local directory, keyed by a hash of the workflow ID. It uses a lock file and a temporary file rename to protect one filesystem shared by processes; it is a development and single-host example, not a distributed database or a guarantee of survival after power loss. State types must serialize and deserialize correctly with `System.Text.Json`; supply `JsonSerializerOptions` when needed (for example, custom polymorphic types). The store holds all processed input IDs for a session, so long-running workloads need a retention policy and a production storage design.

Durable delivery is **at least once**. If a process stops after an external effect succeeds but before its completion checkpoint is saved, dispatch will invoke the handler again with the **same action ID**. Implement `IIdempotentWorkflowActionHandler<TState>` so that recording the ID and applying its business effect happen atomically in the external system. For example, put a unique action ID and an order update in the same database transaction. A handler that only prints to the console, sends an SMS without provider-side deduplication, or writes an ID separately from its effect does not guarantee exactly-once results. Two dispatchers may also call a pending handler concurrently; the handler must enforce that same idempotency rule. There is no built-in timeout, lease, poison queue, or automatic retry scheduler.

The persistent order sample uses `workflow-data/` beneath the current working directory; the folder is ignored by Git. Run `start`, then `confirm` with an input ID, and `show` in a fresh process to see that the pending action survived. Run `dispatch` and `show` again to see the saved checkpoint. The console handler shows the stable action ID but performs no real business effect.

## AI proposals and the trust boundary (Phase 5)

`YoAIWorkflow.AI` has no provider SDK dependency. An application implements `IAIInputSource<TContext, TInput>` to parse a model response into `AIInputProposal<TInput>` with confidence from 0 to 1. The `AIWorkflowInputAdapter` rejects missing inputs, confidence outside that range, and proposals below a configurable minimum (default `0.8`). The application's `IAIInputValidator<TState, TInput>` then validates the typed input against the **current persisted state** before the runner computes and saves a decision. On a revision conflict, the runner loads state and validates again. A rejected proposal is not recorded as processed input, creates no actions, and does not call a handler. The host handles a rejection by asking for clarification or handing off according to its product rules.

The model never supplies a `WorkflowAction` or final business state through this adapter. Even a valid proposal only saves action intents; the host explicitly calls `DispatchAsync` afterward. The validator must check valid enum values, permissions, required evidence, applicable state, and any other business-specific rules. Confidence is supplied by the proposal source; it is not proof that the model is correct. Assign a stable `inputId` for each upstream event and reuse it on retries.

For example, an order host can implement `IAIInputSource<string, CustomerReply>` for its chosen model and `IAIInputValidator<OrderState, CustomerReply>` for its order rules. `samples/AIOrderProposal` uses a deterministic **simulated source**, so you can test `confirm`, `cancel`, `review`, `maybe` (low confidence), or an unknown value without an API key. It does not call a real LLM or interpret speech. The validator rejects proposals against a closed order. Existing direct `WorkflowEngine.Process` and `DurableWorkflowRunner.ProcessAsync` methods remain explicit host APIs; applications must route untrusted model output through validation instead of calling them directly.

## Repository structure

| Path | Purpose |
| --- | --- |
| `src/YoAIWorkflow.Abstractions` | Workflow contract, action intent, transition result, and action handler interface |
| `src/YoAIWorkflow.Core` | Decision engine, durable runner, file store, action executor, and validation hook |
| `src/YoAIWorkflow.AI` | Provider-independent typed input proposal adapter and validator interface |
| `samples/OrderConfirmation` | Example business rules, console input, and console handlers |
| `samples/AppointmentBooking` | Multi-turn booking example with independent rules and console handlers |
| `samples/PersistentOrderConfirmation` | File-backed restart demonstration with the order workflow |
| `samples/AIOrderProposal` | Simulated AI proposal with explicit business validation |
| `tests/YoAIWorkflow.Tests` | Tests for both workflows, handler execution, and failures |
| `.github/workflows/ci.yml` | Build and test on pushes to `main` |

Dependencies flow from `Core` to `Abstractions`. Product workflows reference these libraries; channel and infrastructure integrations sit outside the core.

## Roadmap

| Phase | Goal | Status |
| --- | --- | --- |
| 1 | Independent solution, typed contracts, order example, and tests | Complete; CI passed |
| 2 | Explicit action handlers and a failure contract | Complete; CI passed |
| 3 | A second workflow to verify reuse across different business rules | Complete; local build and 19 tests passed |
| 4 | Persist, resume, and supply stable action IDs for handler deduplication | Complete; local build and 24 tests passed |
| 5 | Optional AI input adapter with validation before actions | Code prepared; build verification pending |
| 6 | Optional voice and other channel integrations | Planned |
| 7 | Package release and integration documentation | Planned |

RAG and a broader LangChain-style .NET library are possible later projects. They are outside this SDK's current scope.

## Contributing

The project is in early development. Issues that describe a concrete workflow use case or a problem in the current sample are welcome. For code changes, run `dotnet build YoAIWorkflow.slnx` and `dotnet test YoAIWorkflow.slnx`; CI runs the same checks after a push to `main`.

The APIs are experimental until a package version is published. Keep workflow decisions separate from input collection and external business actions when proposing changes.
