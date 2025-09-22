using System;
using System.Windows;
using Microsoft.Phone.Controls;
using Windows.Phone.Networking.Voip;
using Microsoft.Phone.Shell;

namespace PhoneApp8
{
    public partial class MainPage : PhoneApplicationPage
    {
        private SIPClient _sipClient;
        private VoipPhoneCall _call;
        private bool _isVoipUiUp;
        private bool _callAnswered;

        // Konstruktor
        public MainPage()
        {
            InitializeComponent();
        }

        // Obsługa kliknięcia przycisku "Start SIP Client"
        private void StartSIPClient_Click(object sender, RoutedEventArgs e)
        {
            if (_sipClient == null)
            {
                _sipClient = new SIPClient();
                _sipClient.IncomingCall += ShowIncomingCall;
                _sipClient.CallEnded += OnSipCallEnded;
                _sipClient.Start();
                MessageBox.Show("SIP Client uruchomiony.");
            }
            else
            {
                MessageBox.Show("SIP Client już działa.");
            }
        }

        public void ShowIncomingCall(string callerName)
        {
            Uri contactImage = new Uri("ms-appx:///Assets/Contact.png", UriKind.Absolute);
            Uri brandingImage = new Uri("ms-appx:///Assets/Branding.png", UriKind.Absolute);
            Uri ringtone = new Uri("ms-appx:///Assets/Ringtone.wav", UriKind.Absolute);

            try
            {
                _callAnswered = false;
                VoipCallCoordinator.GetDefault().RequestNewIncomingCall(
                    "MinimalSIP",
                    callerName,
                    callerName,
                    contactImage,
                    "SIPService",
                    brandingImage,
                    "Połączenie przychodzące",
                    ringtone,
                    VoipCallMedia.Audio,
                    TimeSpan.FromSeconds(30),
                    out _call
                );
                _isVoipUiUp = true;

                // 1) Użytkownik tapnął „Odbierz”
                _call.AnswerRequested += (s, args) =>
                {
                    _callAnswered = true;
                    _sipClient.IncomingAccepted();
                   // StartRtpSession();
                    SafeEndCallUI();
                    Deployment.Current.Dispatcher.BeginInvoke(() =>
                    {
                        PhoneApplicationService.Current.State["VoipCall"] = _call;
                        PhoneApplicationService.Current.State["SIP"] = _sipClient;

                        NavigationService.Navigate(
                          new Uri(
                            "/CallPage.xaml?caller=" + Uri.EscapeDataString(callerName),
                            UriKind.Relative
                          )
                        );
                    });
                };

                // 2) Użytkownik tapnął „Odrzuć”
                _call.RejectRequested += (s, args) =>
                {
                    // wyślij 486 Busy Here
                    _sipClient.IncomingRejected();
                    SafeEndCallUI();
                };

                // 3) Użytkownik zakończył trwające połączenie
                _call.EndRequested += (s, args) =>
                {
                    // możesz wysłać BYE
                    _sipClient.SendBye();
                    //StopRtpSession();
                    SafeEndCallUI();
                };



            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd VoIP: " + ex.Message);
            }

        }
        private void OnSipCallEnded()
        {
            // jeśli ekran nadal widoczny, to schowaj
                SafeNotifyEnded();

            //StopRtpSession();
        }
        private void SafeNotifyEnded()
        {
            if (!_isVoipUiUp) return;

            try
            {
                _call?.NotifyCallEnded();
            }
            catch
            {
                // swallow "Element not found" or any COM error
            }
            finally
            {
                _isVoipUiUp = false;
            }
        }
        private void SafeEndCallUI()
        {
            if (!_isVoipUiUp) return;

            try
            {
                _call.NotifyCallEnded();
            }
            catch
            {
                // ignorujemy ElementNotFound itp.
            }
            finally
            {
                _isVoipUiUp = false;
            }
        }
    }
}
