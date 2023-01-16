#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechToTextWithAmiVoice
{
    internal class VoiceRecognizerWithAmiVoiceCloud
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

        private Dictionary<string, string> connectionParameter;

        public Dictionary<string, string> ConnectionParameter
        {
            get { return this.connectionParameter; }
            set
            {
                if (!isConnected)
                {
                    this.connectionParameter = value;
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

        private readonly JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IgnoreNullValues = true };

        public VoiceRecognizerWithAmiVoiceCloud(string wsUri, string appKey)
        {
            var uri = new Uri(wsUri);
            if (uri.Scheme != "ws" && uri.Scheme != "wss")
            {
                throw new ArgumentException("Invalid scheme");
            }
            connectionUri = uri;
            this.appKey = appKey;
            this.engine = "-a-general";

            wsAmiVoice = new ClientWebSocket();
            sendQueue = new ConcurrentQueue<byte[]>();
            receiveQueue = new ConcurrentQueue<string>();

            connectionParameter = new Dictionary<string, string>();

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
                WebSocketReceiveResult? result = null;
                do
                {
                    try
                    {
                        result = await wsAmiVoice.ReceiveAsync(buffer, CancellationToken.None);
                        ms.Write(buffer.ToArray(), 0, result.Count);
                        if (result.CloseStatus != null)
                        {
                            Debug.WriteLine(result.CloseStatusDescription);
                        }
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Debug.WriteLine("WebSocket Close Recieved");
                            break;
                        }
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                    catch (WebSocketException ex)
                    {
                        var errStr = String.Format("WebSocketError: {0} - {1}", ex.WebSocketErrorCode, ex.Message);
                        ErrorOccured?.Invoke(this, errStr);
                        Debug.WriteLine(ex.Message);
                        Debug.WriteLine(ex.StackTrace);
                        if (wsAmiVoice.CloseStatus != null)
                        {
                            Debug.WriteLine(wsAmiVoice.CloseStatusDescription);
                        }
                        Debug.WriteLine(ex.WebSocketErrorCode);

                        string temp;
                        try
                        {
                            temp = Encoding.UTF8.GetString(ms.ToArray());
                            Debug.WriteLine(temp);
                        }
                        catch (Exception encex) when (encex is ArgumentException || encex is ArgumentNullException)
                        {
                        }

                        return "";
                    }
                } while ((result == null || result.EndOfMessage != true) && (wsAmiVoice.State == WebSocketState.Open || wsAmiVoice.State == WebSocketState.CloseSent));

                var payload = ms.ToArray();
                try
                {
                    var encodedData = System.Text.Encoding.UTF8.GetString(payload);
                    Debug.WriteLine(encodedData);
                    Debug.Write("Exit ReceiveTillEnd");
                    return encodedData;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is ArgumentNullException || ex is DecoderFallbackException)
                {
                    Debug.Write("Exit ReceiveTillEnd");
                    return "";
                }
            }
        }

        public class ConnectionResult
        {
            public bool isSuccess;
            public string message = "";
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
        private async Task<ConnectionResult> Connect(CancellationToken ct)
        {
            wsAmiVoice?.Dispose();
            wsAmiVoice = new ClientWebSocket();
            // AmiVoice CloudはおそらくPingパケットを投げられると切断する仕様なので
            // Keep-Aliveを無効にする
            Debug.WriteLine(String.Format("Default Keep-Alive: {0}", wsAmiVoice.Options.KeepAliveInterval));
            wsAmiVoice.Options.KeepAliveInterval = TimeSpan.Zero;

            // AppKeyに対応する辞書エントリを作る
            var parameter = new Dictionary<string, string>(connectionParameter);
            parameter["authorization"] = this.AppKey;

            // 接続して認証する
            using (var ms = new MemoryStream())
            {
                // 送信する文字列を作成する
                string sendString = String.Format(" {0} {1} ", WaveFormatString, engine);
                foreach (var item in parameter)
                {
                    string key = item.Key;
                    string value = item.Value.Contains(" ") ? String.Format("\"{0}\"", item.Value) : item.Value;
                    sendString += String.Format("{0}={1} ", key, value);
                }
                sendString.TrimEnd();
                sendString = sendString.Insert(0, HexToString((byte)CommandType.Auth));
                //Debug.WriteLine(String.Format("Connection String: {0}", sendString));

                // 認証文字列を実際に送信するバイト列に直す
                ms.Write(Encoding.UTF8.GetBytes(sendString));

                // 実際に接続する
                try
                {
                    await wsAmiVoice.ConnectAsync(ConnectionUri, ct);
                }
                catch (WebSocketException ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                // 実際に接続できているかどうかを確認して接続できていなかったらエラーとする
                if (wsAmiVoice.State != WebSocketState.Open)
                {
                    await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    return new ConnectionResult { isSuccess = false, message = "Connection error (websocket client)" };
                }

                // バイト列を連続するメモリにコピーして送信する
                var data = new ReadOnlyMemory<byte>(ms.ToArray());
                await wsAmiVoice.SendAsync(data, WebSocketMessageType.Text, true, ct);
                ProvidingState = ProvidingStateType.Starting;

                while (true)
                {
                    // 認証した結果を受信する
                    string result = await ReceiveTillEnd(ct);
                    // キャンセルされていたらエラーにする
                    if (ct.IsCancellationRequested)
                    {
                        ProvidingState = ProvidingStateType.Error;
                        await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        return new ConnectionResult { isSuccess = false, message = "Connection cancelled" };
                    }
                    Debug.WriteLine(String.Format("Connection Result: '{0}' Length: {1}", result, result.Length));

                    // 認証の結果を確認する
                    const string expectedString = "s";
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
                        await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        return new ConnectionResult { isSuccess = false, message = errMessage };
                    }

                    const string traceString = "G";
                    if (!result.StartsWith(traceString))
                    {
                        break;
                    }
                }

                // もし認証後に's'以外から始まる文字列が初めに来たのならそれはエラーとする
                await wsAmiVoice.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                ProvidingState = ProvidingStateType.Error;
                return new ConnectionResult { isSuccess = false, message = "Unknown error" };
            }
        }

        public class SpeechRecognizeToken
        {
            public string Written { get; set; } = "";
            public double? Confidence { get; set; }
            public int? StartTime { get; set; }
            public int? EndTime { get; set; }
            public string? Spoken { get; set; }
        }

        public class SpeechRecogntionResult
        {
            public double? Confidence { get; set; }
            public int? StartTime { get; set; }
            public int? EndTime { get; set; }
            public string Text { get; set; } = "";
            public List<string>? Tags { get; set; }
            public string? Rulename { get; set; }
            public IList<SpeechRecognizeToken>? Tokens { get; set; }
        }

        public class SpeechRecognitionEventArgs
        {
            public IList<SpeechRecogntionResult>? Results { get; set; }
            public string? UtteranceId { get; set; }
            public string Text { get; set; } = "";
            public string? code { get; set; }
            public string? message { get; set; }

            [JsonExtensionData]
            public Dictionary<string, object>? ExtensionData { get; set; }
        }

        public event EventHandler<SpeechRecognitionEventArgs>? Recognized;

        public event EventHandler<SpeechRecognitionEventArgs>? Recognizing;

        public event EventHandler<uint>? VoiceStart;

        public event EventHandler<uint>? VoiceEnd;

        public event EventHandler<bool>? RecognizeStarting;

        public event EventHandler<string>? ErrorOccured;

        public event EventHandler<bool>? RecognizeStopped;

        public event EventHandler<string>? Trace;

        public string LastErrorString { get; protected set; } = "";

        private ConcurrentQueue<byte[]> sendQueue;
        private ConcurrentQueue<string> receiveQueue;

        private async Task ReceiveLoop(CancellationToken ct)
        {
            List<byte> response = new List<byte>();
            // var buffer = new byte[4096];
            var buffer = new ArraySegment<byte>(new byte[8192]);
            while (!ct.IsCancellationRequested && wsAmiVoice.State == WebSocketState.Open)
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        while (!ct.IsCancellationRequested && wsAmiVoice.State == WebSocketState.Open)
                        {
                            var result = await wsAmiVoice.ReceiveAsync(buffer, ct);
                            if (result.Count == 0)
                            {
                                continue;
                            }
                            ms.Write(buffer.ToArray(), 0, result.Count);
                            if (result.EndOfMessage)
                            {
                                break;
                            }
                        }
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        var textArray = ms.ToArray();
                        var resultText = System.Text.Encoding.UTF8.GetString(textArray);
                        receiveQueue.Enqueue(resultText);
                    }
                }
                catch (WebSocketException ex)
                {
                    Debug.WriteLine(String.Format("Recieve:WebSocketException: {0}", ex.Message));
                    break;
                }
            }

            if (wsAmiVoice.State == WebSocketState.CloseReceived || wsAmiVoice.State == WebSocketState.Open)
            {
                Debug.WriteLine("Receive will be closed.");
                try
                {
                    await wsAmiVoice.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                catch (WebSocketException ex)
                {
                    Debug.WriteLine(String.Format("ReceiveClose:WebSocketException: {0}", ex.Message));
                }
            }
        }

        public static readonly byte[] prefixC = new byte[] { (byte)CommandType.Data };

        protected async Task MessageLoop(CancellationToken ct)
        {
            ProvidingState = ProvidingStateType.Initialized;

            try
            {
                Func<Task<bool>> connectAction = async () =>
                {
                    var connectionResult = await Connect(ct);
                    if (connectionResult.isSuccess == false)
                    {
                        LastErrorString = connectionResult.message;
                        ErrorOccured?.Invoke(this, LastErrorString);
                        return false;
                    }
                    Trace?.Invoke(this, "Connection complete.");
                    return true;
                };

                sendQueue.Clear();

                CancellationTokenSource receiveTokenSource = new CancellationTokenSource();
                CancellationToken receiveToken = receiveTokenSource.Token;
                Task? receiveTask = null;

                char[] charsToTrim = { ' ', '\x00' };
                while (!ct.IsCancellationRequested)
                {
                    if (ProvidingState == ProvidingStateType.Initialized || (receiveTask != null && receiveTask.Status == TaskStatus.RanToCompletion))
                    {
                        if (receiveTask != null && receiveTask.Status != TaskStatus.RanToCompletion)
                        {
                            if (wsAmiVoice.State == WebSocketState.Open)
                            {
                                wsAmiVoice.Abort();
                            }
                            receiveTask.Dispose();
                        }

                        Trace?.Invoke(this, "Try to connect.");
                        if (receiveTask != null && receiveTask.Status == TaskStatus.Running)
                        {
                            receiveTokenSource?.Cancel();
                            await receiveTask;
                        }

                        var connectResult = await connectAction();
                        if (!connectResult)
                        {
                            break;
                        }

                        receiveTask?.Dispose();
                        receiveTokenSource = new CancellationTokenSource();
                        receiveToken = receiveTokenSource.Token;
                        receiveTask = Task.Run(() => ReceiveLoop(receiveToken), receiveToken);
                    }

                    string receiveData;
                    if (receiveQueue.TryDequeue(out receiveData))
                    {
                        receiveData = receiveData.Trim(charsToTrim);
                        //Debug.WriteLine(String.Format("Recieve: {0}", receiveData));

                        // セッションタイムアウト等による切断
                        // 実際は強制的に切断されていたりするのでここを通らなかったりする
                        if (receiveData.StartsWith("p") && receiveData.Length > 3)
                        {
                            Debug.WriteLine("Timeout occured.");
                            Trace?.Invoke(this, receiveData.Substring(1).Trim());
                            RecognizingState = RecognizingStateType.NotRecognizing;
                            DetectingState = DetectingStateType.NotDetecting;
                            ProvidingState = ProvidingStateType.Initialized;
                            continue;
                        }

                        // エラー処理
                        if ((receiveData.StartsWith("e") && receiveData.Length > 3))
                        {
                            LastErrorString = receiveData;
                            ErrorOccured?.Invoke(this, receiveData.Substring(1));
                            ProvidingState = ProvidingStateType.Error;
                            RecognizingState = RecognizingStateType.NotRecognizing;
                            DetectingState = DetectingStateType.NotDetecting;
                            break;
                        }

                        // 発話区間開始
                        if (receiveData.StartsWith("S"))
                        {
                            uint startMiliSec;
                            if (uint.TryParse(receiveData.Substring(2), out startMiliSec))
                            {
                                //Debug.WriteLine(String.Format("S: {0}", receiveData));
                                VoiceStart?.Invoke(this, startMiliSec);
                            }
                            DetectingState = DetectingStateType.Detecting;
                        }

                        // 発話区間終了
                        if (receiveData.StartsWith("E"))
                        {
                            uint endMiliSec;
                            if (uint.TryParse(receiveData.Substring(2), out endMiliSec))
                            {
                                //Debug.WriteLine(String.Format("E: {0}", receiveData));
                                VoiceEnd?.Invoke(this, endMiliSec);
                            }
                            DetectingState = DetectingStateType.NotDetecting;
                        }

                        // 認識処理開始
                        if (receiveData.StartsWith("C"))
                        {
                            RecognizingState = RecognizingStateType.Recognizing;
                            RecognizeStarting?.Invoke(this, true);
                        }

                        // 認識処理返却
                        if (receiveData.StartsWith("U") || receiveData.StartsWith("A"))
                        {
                            try
                            {
                                //Debug.WriteLine(receiveData.Substring(2).Trim());
                                var result = JsonSerializer.Deserialize<SpeechRecognitionEventArgs>(receiveData.Substring(2), jsonSerializerOptions);
                                if (receiveData.StartsWith("U"))
                                {
                                    // 認識途中
                                    Recognizing?.Invoke(this, result);
                                }
                                else if (receiveData.StartsWith("A"))
                                {
                                    // 認識終了
                                    Recognized?.Invoke(this, result);
                                    RecognizingState = RecognizingStateType.NotRecognizing;
                                }
                            }
                            catch (JsonException ex)
                            {
                                Debug.WriteLine(ex.Message);
                            }
                        }
                    }

                    // 受信が異常終了していないか確認
                    if (receiveTask != null && receiveTask.Status == TaskStatus.Faulted)
                    {
                        Debug.WriteLine(String.Format("Loop:ReceiveWSException: {0}", receiveTask.Exception.InnerException.Message));
                        ErrorOccured?.Invoke(this, String.Format("ReceiveTaskException"));
                        break;
                    }

                    // 音声データを送る
                    byte[] sendData;
                    while (wsAmiVoice.State == WebSocketState.Open && sendQueue.TryDequeue(out sendData))
                    {
                        sendData = prefixC.Concat(sendData).ToArray();
                        if (sendData.Length == 0)
                        {
                            continue;
                        }
                        try
                        {
                            await wsAmiVoice.SendAsync(sendData, WebSocketMessageType.Binary, true, CancellationToken.None);
                        }
                        catch (Exception ex) when (ex is WebSocketException || ex is IOException)
                        {
                            var sendErrString = String.Format("Send:WebSocketException: {0}", ex.Message);
                            Trace?.Invoke(this, sendErrString);
                            break;
                        }
                    }

                    await Task.Delay(50);
                }

                // 終了処理
                byte[] endArray = new byte[] { (byte)CommandType.End };
                await wsAmiVoice.SendAsync(endArray, WebSocketMessageType.Text, true, CancellationToken.None);
                string disconnectionStr = "";
                while (wsAmiVoice.State == WebSocketState.Open && receiveTask != null && receiveTask.Status == TaskStatus.Running)
                {
                    if (!receiveQueue.TryDequeue(out disconnectionStr))
                    {
                        continue;
                    }

                    if (disconnectionStr.StartsWith("e") && disconnectionStr.Length == 1)
                    {
                        receiveTokenSource?.Cancel();
                        receiveTask.Wait(1000);
                        ProvidingState = ProvidingStateType.Initialized;
                        RecognizingState = RecognizingStateType.NotRecognizing;
                        DetectingState = DetectingStateType.NotDetecting;
                        RecognizeStopped?.Invoke(this, true);
                    }
                    else if (disconnectionStr.StartsWith("e"))
                    {
                        RecognizingState = RecognizingStateType.NotRecognizing;
                        DetectingState = DetectingStateType.NotDetecting;
                        ProvidingState = ProvidingStateType.Error;
                        LastErrorString = disconnectionStr.Substring(2);
                        ErrorOccured?.Invoke(this, LastErrorString);
                    }
                }

                if (receiveTokenSource != null && !receiveTokenSource.IsCancellationRequested)
                {
                    receiveTokenSource.Cancel();
                    if (receiveTask != null && receiveTask.Status == TaskStatus.Running)
                    {
                        receiveTask.Wait(3000);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                ErrorOccured?.Invoke(this, String.Format("Loop:WebSocketException: {0}", ex.Message));
            }
            finally
            {
                if (wsAmiVoice.State == WebSocketState.Open)
                {
                    try
                    {
                        await wsAmiVoice.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    catch (WebSocketException ex)
                    {
                        Debug.WriteLine(String.Format("Close:WebSocketException: {0} - {1}", ex.Message, wsAmiVoice.State.ToString()));
                    }
                }
            }

            Trace?.Invoke(this, "Disconnected.");
        }

        public Task? messageLoopTask { get; protected set; }

        /// <summary>
        /// AmiVoice Cloudに接続をしてメッセージの受信ループに入ります。
        /// 受信ループに入ったらすぐに処理を返します。
        /// </summary>
        /// <param name="ct">受信ループを終わらせるためのCancellation Token</param>
        public void Start(CancellationToken ct)
        {
            try
            {
                messageLoopTask = Task.Run(async () => { await MessageLoop(ct); }, ct);
                messageLoopTask.ConfigureAwait(false);
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
        public void FeedRawWave(byte[] rawWave)
        {
            if (ProvidingState != ProvidingStateType.Started && ProvidingState != ProvidingStateType.Providing)
            {
                return;
            }
            if (wsAmiVoice.State != WebSocketState.Open)
            {
                return;
            }
            if (rawWave.Length == 0)
            {
                return;
            }

            sendQueue.Enqueue(rawWave);
        }
    }
}