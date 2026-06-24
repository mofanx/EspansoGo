using CommunityToolkit.Mvvm.Messaging.Messages;

namespace EspansoGo.Models
{
    public class AppResumedMessage : ValueChangedMessage<bool>
    {
        public AppResumedMessage() : base(true)
        {
        }
    }
}
