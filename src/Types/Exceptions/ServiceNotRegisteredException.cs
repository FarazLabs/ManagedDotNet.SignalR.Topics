using System;

namespace ManagedDotNet.SignalR.Topics.Types.Exceptions
{
    public class ServiceNotRegisteredException(string type)
        : Exception($"Failed to acquire service {type} from the DI container.");
}