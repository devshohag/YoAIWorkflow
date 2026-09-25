namespace YoAIWorkflow.AI;

/// <summary>Adapts a model or a test source to a proposed typed workflow input.</summary>
public interface IAIInputSource<TContext, TInput>
{
    Task<AIInputProposal<TInput>?> ProposeAsync(
        TContext context, CancellationToken cancellationToken = default);
}

public sealed record AIInputProposal<TInput>(TInput? Input, decimal Confidence);
