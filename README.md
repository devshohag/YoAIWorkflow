# YoAIWorkflow

**A .NET foundation for AI-assisted business workflows.**

YoAIWorkflow helps applications turn an input and a current state into a new state and a set of requested business actions. Workflow rules live in typed C# code; the application decides how to collect input, save state, and execute actions.

> **Project status:** Early development. Phase 1 provides the workflow contract, a small execution core, an order confirmation example, and tests. It does not yet include an AI integration, action executor, or persistence. Public APIs may change before the first package release.

## Why it exists

The same application may need different workflows: an appointment agent books a slot, while an order agent confirms or cancels an order. Those decisions have different rules even if the input arrives through the same voice agent, web application, or API.

YoAIWorkflow provides a shared place for the *decision boundary*. A workflow receives a typed input, evaluates business rules, and returns the next state plus action intents. The host application handles side effects. This approach also makes it possible to validate an AI-produced proposal before any business action is executed.

This project grew out of work on [YoVoiceAgent](https://github.com/devshohag/YoVoiceAgent), but its core does not reference YoVoiceAgent, Asterisk, an LLM provider, a database, or a particular communication channel.

## What works today

- Define a workflow using `IWorkflow<TState, TInput>`.
- Evaluate a transition with `WorkflowEngine<TState, TInput>`.
- Return a `WorkflowTransition<TState>` containing the next state and `WorkflowAction` intents.
- Run the order confirmation sample: confirm, cancel, or request human review.
- Run tests covering these transitions and repeated input after an order leaves its pending state.

The engine computes a decision only. A `WorkflowAction` does not send a message or update an order, and Phase 1 does not save workflow state between processes.

## Getting started

**Prerequisite:** .NET 10 SDK. Clone the repository and run the following commands from its root:

```bash
dotnet restore YoAIWorkflow.slnx
dotnet build YoAIWorkflow.slnx --no-restore
dotnet test YoAIWorkflow.slnx --no-build
dotnet run --project samples/OrderConfirmation/OrderConfirmation.csproj
```

At the sample prompt, enter `1` to confirm, `2` to cancel, or anything else to request human review. The output shows the new order state and the action requested of a future host application.

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
```

`OrderConfirmationWorkflow`, `OrderState`, and `CustomerReply` belong to the sample application. A consuming project supplies its own types and workflow rules.

## Repository structure

| Path | Purpose |
| --- | --- |
| `src/YoAIWorkflow.Abstractions` | Workflow contract, action intent, and transition result |
| `src/YoAIWorkflow.Core` | Typed workflow execution boundary |
| `samples/OrderConfirmation` | Example business rules and console input |
| `tests/YoAIWorkflow.Tests` | Tests for order decisions and repeated inputs |
| `.github/workflows/ci.yml` | Build and test on pushes to `main` |

Dependencies flow from `Core` to `Abstractions`. Product workflows reference these libraries; channel and infrastructure integrations sit outside the core.

## Roadmap

| Phase | Goal | Status |
| --- | --- | --- |
| 1 | Independent solution, typed contracts, order example, and tests | Code available; build verification pending |
| 2 | Explicit action handlers and a failure contract | Planned |
| 3 | A second workflow to verify reuse across different business rules | Planned |
| 4 | Persist, resume, and deduplicate work safely | Planned |
| 5 | Optional AI input adapter with validation before actions | Planned |
| 6 | Optional voice and other channel integrations | Planned |
| 7 | Package release and integration documentation | Planned |

RAG and a broader LangChain-style .NET library are possible later projects. They are outside this SDK's current scope.

## Contributing

The project is in its first development phase. Issues that describe a concrete workflow use case or a problem in the current sample are welcome. For code changes, run `dotnet build YoAIWorkflow.slnx` and `dotnet test YoAIWorkflow.slnx`; CI runs the same checks after a push to `main`.

The APIs are experimental until a package version is published. Keep workflow decisions separate from input collection and external business actions when proposing changes.
"# YoAIWorkflow" 
