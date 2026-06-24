using CommunityToolkit.Mvvm.Messaging.Messages;
using EspansoGo.Models;

public class AcGlobalsMessage : ValueChangedMessage<List<Var>>
{
    public AcGlobalsMessage(List<Var> message) : base(message)
    {
    }
}