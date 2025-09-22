using System;
using System.Windows;
using Microsoft.Phone.Controls;
using Windows.Phone.Networking.Voip;
using System.Windows.Threading;
using Microsoft.Phone.Shell;
using System.Diagnostics;

namespace PhoneApp8
{
    public partial class CallPage : PhoneApplicationPage
    {
        private DispatcherTimer _timer;
        private TimeSpan _elapsed;
        private string _caller;
        private SIPClient _sip => PhoneApplicationService.Current.State["SIP"] as SIPClient;
        private VoipPhoneCall _call => PhoneApplicationService.Current.State["VoipCall"] as VoipPhoneCall;


        public CallPage()
        {
            InitializeComponent();

            _sip.OnIncomingByeAnswered += () =>
            {
                // akcje na wątku UI
                Deployment.Current.Dispatcher.BeginInvoke(() =>
                {
                    // zatrzymaj RTP/timer
                    //StopRtpSession();
                    _timer.Stop();

                    // schowaj natywne VoIP UI
                    try { _call.NotifyCallEnded(); }
                    catch { }
                });
            };


            // Timer połączenia
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                _elapsed = _elapsed.Add(TimeSpan.FromSeconds(1));
                TimerText.Text = _elapsed.ToString(@"mm\:ss");
            };
        }

        protected override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Odbierz parametr caller z URI
            if (NavigationContext.QueryString.TryGetValue("caller", out _caller))
                CallerText.Text = _caller;

            // Startujemy timera
            _elapsed = TimeSpan.Zero;
            _timer.Start();
        }

        private void Speaker_Click(object sender, RoutedEventArgs e)
        {
            // TODO: przełącz głośnik
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            // TODO: przełącz mikrofon
        }

        private void Bluetooth_Click(object sender, RoutedEventArgs e)
        {
            // TODO: przełącz Bluetooth
        }

        private void Hold_Click(object sender, RoutedEventArgs e)
        {
            // TODO: hold / resume
        }

        private void AddCall_Click(object sender, RoutedEventArgs e)
        {
            // TODO: add call
        }

        private void Skype_Click(object sender, RoutedEventArgs e)
        {
            // TODO: przełącz na Skype (jeśli zainstalowany)
        }

        private void EndCall_Click(object sender, RoutedEventArgs e)
        {
            // 1) Zanim schowasz UI, wyślij poprawny komunikat SIP:
            
            {
                // jeśli już odebrane, to BYE
                _sip.SendBye();
                Debug.WriteLine("<< BYE wysłano");
            }

            // 2) Zatrzymaj audio/timer
            _timer.Stop();
           // StopRtpSession();

            // 3) Zamknij natywne UI
            try { _call?.NotifyCallEnded(); } catch { }

            // 4) Wróć do MainPage
            NavigationService.GoBack();
        }
    }
}
