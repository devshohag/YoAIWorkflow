using OrderConfirmation;
using YoAIWorkflow.Core;

var engine = new WorkflowEngine<OrderState, CustomerReply>(new OrderConfirmationWorkflow());
var state = new OrderState("ORDER-1001", OrderStatus.Pending);

Console.WriteLine("Enter 1 to confirm, 2 to cancel, or anything else for human review:");
var input = Console.ReadLine() switch
{
    "1" => CustomerReply.Confirm,
    "2" => CustomerReply.Decline,
    _ => CustomerReply.Unclear
};

var transition = engine.Process(state, input);
Console.WriteLine($"Order {transition.State.OrderId}: {transition.State.Status}");

// The host chooses handlers explicitly; the engine itself never calls them.
var executor = new WorkflowActionExecutor<OrderState>(
[
    new ConsoleOrderActionHandler("order.confirmed"),
    new ConsoleOrderActionHandler("order.cancelled"),
    new ConsoleOrderActionHandler("human.review-requested")
]);

var report = await executor.ExecuteAsync(transition);
if (!report.Succeeded)
{
    Console.Error.WriteLine(
        $"Action {report.Failure!.ActionIndex} failed: {report.Failure.Kind}");
    Environment.ExitCode = 1;
}
