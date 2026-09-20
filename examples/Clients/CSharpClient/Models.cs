namespace ManagedDotNet.SignalR.Topics.Examples.Clients.CSharpClient;


public record ConnectionAlert
{
    public string Message { get; set; } = string.Empty;
}


public class TerminateCommand
{
    public required string Reason { get; set; }
}
