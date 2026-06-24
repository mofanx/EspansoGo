using CommunityToolkit.Mvvm.Messaging.Messages;
using EspansoGo.Models;

public class AcServiceMessage : ValueChangedMessage<(string, Match)>
{
    public AcServiceMessage((string, Match) message) : base(message)
    {
    }
}