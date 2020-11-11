using SharpDX.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace SpeechToTextWithAmiVoice
{
    class VoiceRecognizerWithAmiVoiceCloud
    {
        protected ClientWebSocket wsAmiVoice;
        protected bool isConnected = false;
        private Uri connectionUri;
        public Uri ConnectionUri
        {
            get { return this.connectionUri; }
            set
            {
                if (!isConnected)
                {
                    this.connectionUri = value;
                }
            }
        }

        private string appKey;
        public string AppKey
        {
            get { return this.appKey; }
            set
            {
                if (!isConnected)
                {
                    this.appKey = value;
                }
            }
        }

        private string engine;
        public string Engine
        {
            get { return this.engine; }
            set
            {
                if (!isConnected)
                {
                    this.engine = value;
                }
            }
        }

        private string? profileId;
        public string? ProfileId
        {
            get { return this.profileId; }
            set
            {
                if (!isConnected)
                {
                    this.profileId = value;
                }
            }
        }

        public enum ProvidingStateType
        {
            Initialized,
            Starting,
            Started,
            Providing,
            Ending,
            Error
        }

        public enum DetectingStateType
        {
            NotDetecting,
            Detecting
        }

        public enum RecognizingStateType
        {
            NotRecognizing,
            Recognizing
        }

        public ProvidingStateType ProvidingState { get; protected set; }
        public DetectingStateType DetectingState { get; protected set; }
        public RecognizingStateType RecognizingState { get; protected set; }

        protected enum CommandType : byte
        {
            Auth = 0x73,  // 's'
            Data = 0x70,  // 'p'
            End = 0x65,   // 'e'
        }

        public const string WaveFormatString = "lsb16k";
        public const uint MaxReconnectCount = 5;

        private VoiceRecognizerWithAmiVoiceCloud()
        {
            wsAmiVoice = new ClientWebSocket();
        }

        public VoiceRecognizerWithAmiVoiceCloud(string wsUri, string appKey)
        {
            ConnectionUri = new Uri(wsUri);
            if (ConnectionUri.Scheme != "ws" && ConnectionUri.Scheme != "wss")
            {
                throw new ArgumentException("Invalid scheme");
            }
            AppKey = appKey;
            Engine = "-a-genaral";

            wsAmiVoice = new ClientWebSocket();

            ProvidingState = ProvidingStateType.Initialized;
            DetectingState = DetectingStateType.NotDetecting;
            RecognizingState = RecognizingStateType.NotRecognizing;
        }

        /// <summary>
        /// データの終わりまで読み込み、UTF-8でデコードして返します。
        /// </summary>
        /// <remarks>
        /// AmiVoice Cloudのサーバーは常にASCIIコードの範囲で返答することを考慮しています。
        /// </remarks>
        /// <param name="ct">キャンセルする際に必要なCancellationToken</param>
        /// <returns>受信した文字列(キャンセルした場合は空文字列)</returns>
        protected async Task<string> ReceiveTillEnd(CancellationToken ct)
        {
            ArraySegment<Byte> buffer = new ArraySegment<byte>(new Byte[8192]);

            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await wsAmiVoice.ReceiveAsync(buffer, ct);
                    ms.Write(buffer.ToArray(), 0, result.Count);
                } while (result.EndOfMessage != true);

                if (ct.IsCancellationRequested)
                {
                    return "";
                }

                var payload = ms.ToArray();
                Debug.WriteLine("Recieved bytes: {0}", payload.Length);
                var encodedData = Encoding.UTF8.GetString(payload);
                return encodedData;
            }
        }

        public class ConnectionResult
        {
            public bool isSuccess;
            public string message;
        }

        private string HexToString(byte b)
        {
            return Encoding.UTF8.GetString(new byte[] { b });
        }

        /// <summary>
        /// 指定されているURIのAmiVoiceCloudに接続を試みる
        /// </summary>
        /// <param name="ct">外部からキャンセルするためのCancellation Token</param>
        /// <param name="grammerEngine">接続する音声認識エンジンの文字列</param>
        /// <param name="parameter">キー:値型の別途送信するパラメーター</param>
        /// <returns>自身を表わしたTask</returns>
        private async Task<ConnectionResult> Connect(CancellationToken ct, string grammerEngine = "-a-general", Dictionary<string, string> parameter = null)
        {
            // AppKeyに対応する辞書エントリを作る
            if (parameter == null)
            {
                parameter = new Dictionary<string, string>();
            }
            parameter["authorization"] = this.AppKey;

            // 接続して認証する
            using (var ms = new MemoryStream())
            {
                // 送信する文字列を作成する
                string sendString = String.Format(" {0} {1} ", WaveFormatString, grammerEngine);
                foreach (var item in parameter)
                {
                    string key = item.Key;
                    string value = item.Value.Contains(" ") ? String.Format("\"{0}\"", item.Value) : item.Value;
                    sendString += String.Format("{0}={1} ", key, value);
                }
                sendString.TrimEnd();
                sendString = sendString.Insert(0, HexToString((byte)CommandType.Auth));
                Debug.WriteLine(String.Format("Connection String: {0}", sendString));

                // 認証文字列を実際に送信するバイト列に直す
                ms.Write(Encoding.UTF8.GetBytes(sendString));

                // 実際に接続する
                await wsAmiVoice.ConnectAsync(ConnectionUri, ct);
                // 接続中にキャンセルされたらソケットを閉じて失敗を返す
                if (ct.IsCancellationRequested)
                {
                    wsAmiVoice.Abort();
                    return new ConnectionResult { isSuccess = false, message = "Connection cancelled" };
                }

                // 実際に接続できているかどうかを確認して接続できていなかったらエラーとする
                if (wsAmiVoice.State != WebSocketState.Open)
                {
                    wsAmiVoice.Abort();
                    return new ConnectionResult { isSuccess = false, message = "Connection error (websocket client)" };
                }

                // バイト列を連続するメモリにコピーして送信する
                var data = new ReadOnlyMemory<byte>(ms.ToArray());
                await wsAmiVoice.SendAsync(data, WebSocketMessageType.Text, true, ct);
                ProvidingState = ProvidingStateType.Starting;

                // 認証した結果を受信する
                string result = await ReceiveTillEnd(ct);
                // キャンセルされていたらエラーにする
                if (ct.IsCancellationRequested)
                {
                    ProvidingState = ProvidingStateType.Error;
                    wsAmiVoice.Abort();
                    return new ConnectionResult { isSuccess = false, message = "Connection cancelled" };
                }
                Debug.WriteLine(String.Format("Connection Result: '{0}' Length: {1}", result, result.Length));

                // 認証の結果を確認する
                string expectedString = "s";
                if (result.StartsWith(expectedString))
                {
                    // 's'だけの場合は認証に成功している
                    if (result.Length == 1)
                    {
                        ProvidingState = ProvidingStateType.Started;
                        return new ConnectionResult { isSuccess = true, message = "Connection is success" };
                    }

                    // 's'の後に文字列が続いている場合は認証に失敗しているので
                    // その理由を返す
                    var errMessage = result.Substring(1).Trim();
                    ProvidingState = ProvidingStateType.Error;
                    wsAmiVoice.Abort();
                    return new ConnectionResult { isSuccess = false, message = errMessage };
                }

                // もし認証後に's'以外から始まる文字列が初めに来たのならそれはエラーとする
                wsAmiVoice.Abort();
                ProvidingState = ProvidingStateType.Error;
                return new ConnectionResult { isSuccess = false, message = "Unknown error" };
            }
        }

        private async Task<ConnectionResult> Disconnect(CancellationToken ct)
        {
            byte[] command = { (byte)CommandType.End };
            var sendString = Encoding.UTF8.GetString(command);
            var data = new ArraySegment<Byte>(Encoding.UTF8.GetBytes(sendString));

            if (wsAmiVoice.State != WebSocketState.Open)
            {
                await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                ProvidingState = ProvidingStateType.Initialized;
            }

            await wsAmiVoice.SendAsync(data, WebSocketMessageType.Text, true, ct);

            string result;
            do
            {
                result = await ReceiveTillEnd(ct);
            } while (!result.StartsWith("e") && !ct.IsCancellationRequested);
            if (ct.IsCancellationRequested)
            {
                ProvidingState = ProvidingStateType.Error;
                await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                return new ConnectionResult { isSuccess = false, message = "Disconection is cancelled" };
            }
            Debug.WriteLine(String.Format("Disconnecting: {0}", result));

            if (result.StartsWith("e") && result.Length == 1)
            {
                try
                {
                    await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
                finally
                {
                    ProvidingState = ProvidingStateType.Initialized;
                }
                return new ConnectionResult { isSuccess = true, message = "Disconnection is success" };
            }
            var errMessage = result.Substring(1).Trim();
            ProvidingState = ProvidingStateType.Error;
            await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            return new ConnectionResult { isSuccess = false, message = errMessage };
        }

        public class SpeechRecognizeToken
        {
            public string Written = "";
            public double? Confidence;
            public uint? StartTime;
            public uint? EndTime;
            public string? Spoken;
        }

        public class SpeechRecogntionResult
        {
            public double? Confidence;
            public uint? StartTime;
            public uint? EndTime;
            public string Text = "";
            public IList<SpeechRecognizeToken> Tokens;
        }

        public class SpeechRecognitionEventArgs
        {
            public SpeechRecogntionResult Results;
            public string? UtteranceId;
            public string text = "";
            public string? code;
            public string? message;
        }

        public event EventHandler<SpeechRecognitionEventArgs> Recognized;
        public event EventHandler<SpeechRecognitionEventArgs> Recognizing;
        public event EventHandler<uint> VoiceStart;
        public event EventHandler<uint> VoiceEnd;
        public event EventHandler RecognizeStarting;
        public event EventHandler<string> ErrorOccured;

        public string LastErrorString { get; protected set; }

        protected async Task ReceivingLoop(CancellationToken ct)
        {
            bool isError = false;
            uint reconnectionCount = 0;
            ProvidingState = ProvidingStateType.Initialized;
            DetectingState = DetectingStateType.NotDetecting;
            RecognizingState = RecognizingStateType.NotRecognizing;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                IgnoreNullValues = true,
            };

            while (!isError && !ct.IsCancellationRequested && reconnectionCount <= MaxReconnectCount)
            {
                if (wsAmiVoice.State != WebSocketState.Open)
                {
                    isConnected = true;
                    var conn = await Connect(ct, Engine);
                    if (conn.isSuccess != true)
                    {
                        isError = true;
                        ProvidingState = ProvidingStateType.Error;
                        continue;
                    }
                }

                var receivedData = await ReceiveTillEnd(ct);
                if (receivedData.Length == 0)
                {
                    isError = true;
                    continue;
                }

                switch (receivedData.Substring(0, 1))
                {
                    case "p":  // p応答パケット: タイムアウト(セッション/無音声)
                        LastErrorString = receivedData.Substring(2);
                        ErrorOccured?.Invoke(this, LastErrorString);
                        reconnectionCount += 1;
                        ProvidingState = ProvidingStateType.Error;
                        continue;
                    case "e":  // 無通信タイムアウト
                        LastErrorString = receivedData.Substring(2);
                        ErrorOccured?.Invoke(this, LastErrorString);
                        reconnectionCount += 1;
                        ProvidingState = ProvidingStateType.Error;
                        continue;
                    case "S":  // 発話区間の先頭を検出
                        var startTimeStr = receivedData.Substring(2).Trim();
                        uint startTime = 0;
                        uint.TryParse(startTimeStr, out startTime);
                        VoiceStart?.Invoke(this, startTime);
                        ProvidingState = ProvidingStateType.Providing;
                        DetectingState = DetectingStateType.Detecting;
                        break;
                    case "E":  // 発話区間の終端を検出
                        var endTimeStr = receivedData.Substring(2).Trim();
                        uint endTime = 0;
                        uint.TryParse(endTimeStr, out endTime);
                        VoiceEnd?.Invoke(this, endTime);
                        ProvidingState = ProvidingStateType.Providing;
                        DetectingState = DetectingStateType.NotDetecting;
                        break;
                    case "C":  // 認識処理を開始
                        RecognizeStarting?.Invoke(this, null);
                        ProvidingState = ProvidingStateType.Providing;
                        RecognizingState = RecognizingStateType.Recognizing;
                        break;
                    case "U":  // 認識途中結果(utterance)
                        try
                        {
                            var result = JsonSerializer.Deserialize<SpeechRecognitionEventArgs>(receivedData.Substring(1).Trim());
                            Recognizing?.Invoke(this, result);
                        }
                        catch (Exception ex) when (ex is ArgumentNullException || ex is JsonException)
                        {
                            Debug.WriteLine(ex.Message);
                            ErrorOccured?.Invoke(this, ex.Message);
                        }
                        break;
                    case "A":  // 認識最終結果
                        try
                        {
                            RecognizingState = RecognizingStateType.NotRecognizing;
                            var result = JsonSerializer.Deserialize<SpeechRecognitionEventArgs>(receivedData.Substring(1).Trim());
                            if (result.code != "" && result.message != "")
                            {
                                LastErrorString = String.Format("Recognize Error: {0} - {1}", result.code, result.message);
                                Debug.WriteLine(LastErrorString);
                                ErrorOccured?.Invoke(this, LastErrorString);
                            }
                            Recognized?.Invoke(this, result);
                        }
                        catch (Exception ex) when (ex is ArgumentNullException || ex is JsonException)
                        {
                            ErrorOccured?.Invoke(this, ex.Message);
                            Debug.WriteLine(ex.Message);
                        }
                        break;
                    case "G":  // 何かしらのイベント(使用しないことが求められている)
                        break;
                    default:
                        break;
                }
            };

            if (ct.IsCancellationRequested && wsAmiVoice.State == WebSocketState.Open)
            {
                var result = await Disconnect(CancellationToken.None);
                if (!result.isSuccess)
                {
                    ProvidingState = ProvidingStateType.Error;
                    Debug.WriteLine(result.message);
                    LastErrorString = result.message;
                }
                else
                {
                    ProvidingState = ProvidingStateType.Initialized;
                }
            }

            if (wsAmiVoice.State == WebSocketState.Open)
            {
                await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }

            isConnected = false;
        }

        private Task receivingTask;

        /// <summary>
        /// AmiVoice Cloudに接続をしてメッセージの受信ループに入ります。
        /// 受信ループに入ったらすぐに処理を返します。
        /// </summary>
        /// <param name="ct">受信ループを終わらせるためのCancellation Token</param>
        public void Start(CancellationToken ct)
        {
            try
            {
                receivingTask = Task.Run(async () => { await ReceivingLoop(ct); });
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// RAW PCMデータをAmiVoice Cloudに送信します。
        /// 送信が成功したかどうかはこの関数からはわかりません。
        /// </summary>
        /// <remarks>
        /// 送信するバイト列はサンプリングレート16000Hzの量子化ビット数16bit、
        /// リトルエンディアンのRAW PCMデータである必要があります。
        /// ヘッダを含めてはいけません。
        /// </remarks>
        /// <param name="rawWave">送信するRAW PCMデータ</param>
        /// <returns>自身を表わすTask</returns>
        public async Task FeedRawWave(byte[] rawWave)
        {
            if (ProvidingState != ProvidingStateType.Started && ProvidingState != ProvidingStateType.Providing)
            {
                return;
            }
            if (wsAmiVoice.State != WebSocketState.Open)
            {
                return;
            }
            List<byte> byteList = new List<byte>(rawWave);
            var header = Encoding.UTF8.GetBytes("p");
            byteList.InsertRange(0, header);
            try
            {
                await wsAmiVoice.SendAsync(byteList.ToArray(), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                LastErrorString = ex.Message;
                ErrorOccured?.Invoke(this, LastErrorString);
                ProvidingState = ProvidingStateType.Error;
            }
            ProvidingState = ProvidingStateType.Providing;
        }
    }
}
