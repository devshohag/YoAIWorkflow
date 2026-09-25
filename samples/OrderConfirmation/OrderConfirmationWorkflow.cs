using YoAIWorkflow.Abstractions;

namespace OrderConfirmation;

public enum OrderStatus { Pending, Confirmed, Cancelled, NeedsHuman }
public enum CustomerReply { Confirm, Decline, Unclear }
public sealed record OrderState(string OrderId, OrderStatus Status);

public sealed class OrderConfirmationWorkflow : IWorkflow<OrderState, CustomerReply>
{
    public WorkflowTransition<OrderState> Apply(OrderState state, CustomerReply input)
    {
        if (state.Status != OrderStatus.Pending)
        {
            return WorkflowTransition<OrderState>.WithoutActions(state);
        }

        return input switch
        {
            CustomerReply.Confirm => new(
                state with { Status = OrderStatus.Confirmed },
                [new WorkflowAction("order.confirmed")]),
            CustomerReply.Decline => new(
                state with { Status = OrderStatus.Cancelled },
                [new WorkflowAction("order.cancelled")]),
            _ => new(
                state with { Status = OrderStatus.NeedsHuman },
                [new WorkflowAction("human.review-requested")])
        };
    }
}

