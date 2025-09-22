// SIPClient.cs
using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Phone.Networking.Voip;
using System.Windows;                // Deployment.Current.Dispatcher
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using System.Diagnostics;

namespace PhoneApp8
{
    public class SIPClient
    {
        private DatagramSocket _udpSocket;
        private HostName _serverHost;
        private string _serverPort = "5060";

        private string _username = "1002";
        private string _password = "Haslo1002!";
        private string _domain = "192.168.0.16";
        private string _localIP = "192.168.0.14";

        // Nagłówki ostatniego INVITE
        private string _lastVia,
                       _lastFrom,
                       _lastTo,
                       _lastCallId,
                       _lastCSeq;
        private string _lastRemoteIp,
                       _lastRemotePort;

        // Zdarzenia dla UI
        public event Action<string> IncomingCall;
        public event Action CallEnded;

        public SIPClient()
        {
            // host serwera SIP
            _serverHost = new HostName(_domain);
        }

        public async void Start()
        {
            _udpSocket = new DatagramSocket();
            _udpSocket.MessageReceived += UdpSocket_MessageReceived;

            // Wiążemy lokalny port 5060
            await _udpSocket.BindServiceNameAsync("5060");

            // Pierwsze REGISTER bez autoryzacji
            await SendRegister(false, null, null);
        }

        private async Task SendRegister(bool withAuth, string realm, string nonce)
        {
            var branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var callId = Guid.NewGuid().ToString();
            var contact = $"sip:{_username}@{_localIP}:5060";

            // Digest auth
            string authHeader = "";
            if (withAuth)
            {
                var ha1 = ComputeMd5($"{_username}:{realm}:{_password}");
                var ha2 = ComputeMd5($"REGISTER:sip:{_domain}");
                var response = ComputeMd5($"{ha1}:{nonce}:{ha2}");
                authHeader =
                    $"Authorization: Digest username=\"{_username}\", realm=\"{realm}\"," +
                    $" nonce=\"{nonce}\", uri=\"sip:{_domain}\", response=\"{response}\", algorithm=MD5\r\n";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"REGISTER sip:{_domain} SIP/2.0");
            sb.AppendLine($"Via: SIP/2.0/UDP {_localIP}:5060;branch={branch}");
            sb.AppendLine("Max-Forwards: 70");
            sb.AppendLine($"To: <sip:{_username}@{_domain}>");
            sb.AppendLine($"From: <sip:{_username}@{_domain}>;tag=123456");
            sb.AppendLine($"Call-ID: {callId}");
            sb.AppendLine("CSeq: 1 REGISTER");
            sb.AppendLine($"Contact: <{contact}>");
            sb.AppendLine("Expires: 3600");
            sb.AppendLine("User-Agent: PhoneApp8/1.0");
            if (!string.IsNullOrEmpty(authHeader)) sb.Append(authHeader);
            sb.AppendLine("Content-Length: 0");
            sb.AppendLine();

            await SendMessage(sb.ToString());
        }

        private async void UdpSocket_MessageReceived(
            DatagramSocket sender,
            DatagramSocketMessageReceivedEventArgs args)
        {
            // Odczyt wiadomości
            string message;
            using (var reader = args.GetDataReader())
            {
                reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                message = reader.ReadString(reader.UnconsumedBufferLength);
            }

            var remoteIp = args.RemoteAddress.DisplayName;
            var remotePort = args.RemotePort;

            // 401 Unauthorized → powtórz REGISTER z Digest
            if (message.StartsWith("SIP/2.0 401"))
            {
                var realm = ExtractQuotedValue(message, "realm");
                var nonce = ExtractQuotedValue(message, "nonce");
                await SendRegister(true, realm, nonce);
            }
            // OPTIONS → 200 OK
            else if (message.StartsWith("OPTIONS"))
            {
                var resp = BuildSimpleResponse(message, "200 OK");
                await SendMessage(resp);
            }
            // INVITE → 180 Ringing + event do UI
            else if (message.Contains("INVITE sip:"))
            {
                // parsujemy i zapisujemy nagłówki
                _lastVia = GetHeader(message, "Via:");
                _lastFrom = GetHeader(message, "From:");
                _lastTo = GetHeader(message, "To:");
                _lastCallId = GetHeader(message, "Call-ID:");
                _lastCSeq = GetHeader(message, "CSeq:").Split(' ')[0];
                _lastRemoteIp = remoteIp;
                _lastRemotePort = remotePort;

                // wysyłamy 180 Ringing
                var sb = new StringBuilder();
                sb.AppendLine("SIP/2.0 180 Ringing");
                sb.AppendLine($"Via: {_lastVia}");
                sb.AppendLine($"To: {_lastTo}");
                sb.AppendLine($"From: {_lastFrom}");
                sb.AppendLine($"Call-ID: {_lastCallId}");
                sb.AppendLine($"CSeq: {_lastCSeq} INVITE");
                sb.AppendLine("Content-Length: 0");
                sb.AppendLine();

                await SendMessage(sb.ToString());

                // powiadamiamy UI
                var caller = ExtractDisplayName(_lastFrom);
                Deployment.Current.Dispatcher.BeginInvoke(() =>
                    IncomingCall?.Invoke(caller)
                );
            }
            // BYE lub CANCEL → 200 OK + event zakończenia
            else if (message.StartsWith("BYE") || message.StartsWith("CANCEL"))
            {
                var resp = BuildSimpleResponse(message, "200 OK");
                await SendMessage(resp);

                Deployment.Current.Dispatcher.BeginInvoke(() =>
                    CallEnded?.Invoke()
                );
            }
            else if (message.StartsWith("BYE"))
            {
                await HandleBye(message, remoteIp, remotePort);
                
            }

        }

        private async Task SendMessage(string msg)
        {
            // otwieramy strumień wyjściowy
            using (var output = await _udpSocket.GetOutputStreamAsync(
                       _serverHost, _serverPort))
            {
                var writer = new DataWriter(output)
                {
                    UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8
                };
                writer.WriteString(msg);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
        }

        private string BuildSimpleResponse(string request, string status)
        {
            // generuje 200 OK / 486 Busy Here odcinając podstawowe nagłówki
            var via = GetHeader(request, "Via:");
            var to = GetHeader(request, "To:");
            var from = GetHeader(request, "From:");
            var callId = GetHeader(request, "Call-ID:");
            var cseq = GetHeader(request, "CSeq:");

            var sb = new StringBuilder();
            sb.AppendLine($"SIP/2.0 {status}");
            sb.AppendLine($"Via: {via}");
            sb.AppendLine($"To: {to}");
            sb.AppendLine($"From: {from}");
            sb.AppendLine($"Call-ID: {callId}");
            sb.AppendLine($"CSeq: {cseq}");
            sb.AppendLine("Content-Length: 0");
            sb.AppendLine();
            return sb.ToString();
        }

        #region Helper methods

        private string GetHeader(string msg, string name)
        {
            var idx = msg.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            var line = msg.Substring(idx)
                         .Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
            return line.Substring(name.Length).Trim();
        }

        private string ExtractQuotedValue(string msg, string key)
        {
            var tag = key + "=\"";
            var start = msg.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return "";
            start += tag.Length;
            var end = msg.IndexOf("\"", start, StringComparison.Ordinal);
            return msg.Substring(start, end - start);
        }

        private string ExtractDisplayName(string from)
        {
            // z "<sip:1002@...>" wyciąga "1002"
            var between = from.Split('<', '>')[1];
            return between.Substring(between.IndexOf(':') + 1)
                          .Split('@')[0];
        }

        private string ComputeMd5(string input)
        {
            // 1) Otwórz provider MD5
            var provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);

            // 2) Zamień tekst na binarny bufor UTF8
            IBuffer data = CryptographicBuffer.ConvertStringToBinary(
                input,
                BinaryStringEncoding.Utf8
            );

            // 3) Wykonaj hash
            IBuffer hash = provider.HashData(data);

            // 4) Zakoduj wynik w formie heksadecymalnej
            //    i zwróć małymi literami
            return CryptographicBuffer
                .EncodeToHexString(hash)
                .ToLowerInvariant();
        }



        public async void IncomingAccepted()
        {
            // Wygeneruj nasz To-tag i rozbuduj nagłówek
            string localTag = GenerateTag();
            string toWithTag = $"{_lastTo};tag={localTag}";
            _lastTo = toWithTag;

            // Przygotuj SDP
            string sdp =
              "v=0\r\n" +
              $"o=- 0 0 IN IP4 {_localIP}\r\n" +
              "s=VoIP\r\n" +
              $"c=IN IP4 {_localIP}\r\n" +
              "t=0 0\r\n" +
              "m=audio 7078 RTP/AVP 0\r\n" +
              "a=rtpmap:0 PCMU/8000\r\n";

            // Zbuduj 200 OK z rozszerzonym To
            var ok200 =
              $"SIP/2.0 200 OK\r\n" +
              $"Via: {_lastVia}\r\n" +
              $"To: {toWithTag}\r\n" +
              $"From: {_lastFrom}\r\n" +
              $"Call-ID: {_lastCallId}\r\n" +
              $"CSeq: {_lastCSeq} INVITE\r\n" +
              $"Contact: <sip:{_username}@{_localIP}:5060>\r\n" +
              $"Content-Type: application/sdp\r\n" +
              $"Content-Length: {sdp.Length}\r\n\r\n" +
              sdp;

            Debug.WriteLine(">> wysyłam 200 OK:\n" + ok200);
            await SendMessage(ok200);
        }


        /// <summary>
        /// Wywołaj to w MainPage.EndRequested, aby wysłać 486 Busy Here.
        /// </summary>
        public async void IncomingRejected()
        {
            var busy =
              $"SIP/2.0 486 Busy Here\r\n" +
              $"Via: {_lastVia}\r\n" +
              $"To: {_lastTo}\r\n" +
              $"From: {_lastFrom}\r\n" +
              $"Call-ID: {_lastCallId}\r\n" +
              $"CSeq: {_lastCSeq} INVITE\r\n" +
              $"Content-Length: 0\r\n\r\n";

            await SendMessage(busy);
        }

        #endregion
        public async void SendBye()
        {
            // 1) Oblicz nowy numer CSeq: INVITE był z _lastCSeq
            int prev;                             // <-- deklarujemy zmienną prev tutaj
            if (!int.TryParse(_lastCSeq, out prev))
                prev = 1;

            int byeCseq = prev + 1;

            // 2) Zbuduj nowy branch i prefix z oryginalnej Via
            var prefix = _lastVia.Split(';')[0];
            string branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // 3) Zbuduj komunikat BYE
            var sb = new StringBuilder();
            sb.AppendLine("BYE sip:" + _domain + " SIP/2.0");
            sb.AppendLine("Via: " + prefix + ";rport;branch=" + branch);
            sb.AppendLine("From: " + _lastFrom);
            sb.AppendLine("To:   " + _lastTo);
            sb.AppendLine("Call-ID: " + _lastCallId);
            sb.AppendLine("CSeq: " + byeCseq + " BYE");
            sb.AppendLine("Content-Length: 0");
            sb.AppendLine();

            // 4) Wyślij pod ostatni znany adres/port SIP (Asterisk)
            await SendMessage(sb.ToString(), _lastRemoteIp, _lastRemotePort);
        }  // <- upewnij się, że ta klamra zamyka całą metodę


        // Przeciążka SendMessage wysyłająca pod dowolny host/port
        private async Task SendMessage(string msg, string remoteIp, string remotePort)
        {
            var host = new HostName(remoteIp);
            using (var output = await _udpSocket.GetOutputStreamAsync(host, remotePort))
            {
                var writer = new DataWriter(output)
                {
                    UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8
                };
                writer.WriteString(msg);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
        }

        private string GenerateTag()
        {
            // Możesz tu użyć GUID lub timestampu
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }
        private async Task HandleBye(string message, string remoteIp, string remotePort)
        {
            // 1) Wyciągnij nagłówki z oryginalnego BYE
            string via = GetHeader(message, "Via:");
            string from = GetHeader(message, "From:");
            string to = GetHeader(message, "To:");
            string callId = GetHeader(message, "Call-ID:");
            string cseq = GetHeader(message, "CSeq:");  // np. "2 BYE"

            // 2) Zbuduj 200 OK
            var sb = new StringBuilder();
            sb.AppendLine("SIP/2.0 200 OK");
            sb.AppendLine("Via: " + via);
            sb.AppendLine("From: " + from);
            sb.AppendLine("To: " + to);
            sb.AppendLine("Call-ID: " + callId);
            sb.AppendLine("CSeq: " + cseq);
            sb.AppendLine("Content-Length: 0");
            sb.AppendLine();

            Debug.WriteLine(">> Odpowiadam 200 OK na BYE:\n" + sb);
            await SendMessage(sb.ToString(), remoteIp, remotePort);

            // 3) Zamknij RTP i powiadom UI (jeśli jeszcze otwarte)
            //StopRtpSession();
            OnIncomingByeAnswered?.Invoke();
        }
        public event Action OnIncomingByeAnswered;
    }
}
