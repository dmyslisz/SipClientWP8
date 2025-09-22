using System;
using Microsoft.Phone.Scheduler;
using Windows.Phone.Networking.Voip;

namespace PhoneApp8
{
    public class VoipBackgroundAgent : ScheduledTaskAgent
    {
        static VoipBackgroundAgent()
        {
            // pozwala przetestować agenta w emulatorze
            ScheduledActionService.LaunchForTest("VoipBackgroundAgent", TimeSpan.FromSeconds(5));
        }

        protected override void OnInvoke(ScheduledTask task)
        {
            // odblokowuje VoIP w systemie
            VoipCallCoordinator.GetDefault();
            NotifyComplete();
        }
    }
}
