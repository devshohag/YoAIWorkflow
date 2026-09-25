using OrderConfirmation;
using Xunit;
using YoAIWorkflow.Core;

namespace YoAIWorkflow.Tests;

public sealed class OrderConfirmationTests
{
    private readonly WorkflowEngine<OrderState, CustomerReply> _engine =
        new(new OrderConfirmationWorkflow());

    [Theory]
    [InlineData(CustomerReply.Confirm, OrderStatus.Confirmed, "order.confirmed")]
    [InlineData(CustomerReply.Decline, OrderStatus.Cancelled, "order.cancelled")]
    [InlineData(CustomerReply.Unclear, OrderStatus.NeedsHuman, "human.review-requested")]
    public void Reply_produces_expected_state_and_business_intent(
        CustomerReply reply, OrderStatus expectedStatus, string expectedAction)
    {
        var startingState = new OrderState("ORDER-1001", OrderStatus.Pending);

        var transition = _engine.Process(startingState, reply);

        Assert.Equal(OrderStatus.Pending, startingState.Status);
        Assert.Equal(expectedStatus, transition.State.Status);
        Assert.Equal(expectedAction, Assert.Single(transition.Actions).Name);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.NeedsHuman)]
    public void Closed_order_cannot_emit_a_second_action(OrderStatus status)
    {
        var state = new OrderState("ORDER-1001", status);

        var transition = _engine.Process(state, CustomerReply.Confirm);

        Assert.Same(state, transition.State);
        Assert.Empty(transition.Actions);
    }
}

