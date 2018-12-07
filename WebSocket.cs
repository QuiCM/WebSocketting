using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

//Allow unit tests to access internals
[assembly: InternalsVisibleTo("Testing")]

namespace WebSocketting
{
    /// <summary>
    /// Provides the ability to connect to a WebSocket
    /// </summary>
    public class WebSocket
    {
        //ClientWebSocket supports exactly 1 send and 1 receive in parallel.
        //This means only 1 read and 1 write can happen at a time
        //A SemaphoreSlim is used to provide this restriction

        internal System.Net.WebSockets.WebSocket _ws;
        /// <summary>
        /// Connection options on the socket to be connected to
        /// </summary>
        public ClientWebSocketOptions ConnectionOptions => ((ClientWebSocket)_ws).Options;
        /// <summary>
        /// Event invoked when the Websocket is using <see cref="WebSocketMessageType.Text"/> and a message is received
        /// </summary>
        public event EventHandler<StringMessageEventArgs> OnTextMessage;

        /// <summary>
        /// Event invoked when the Websocket is using <see cref="WebSocketMessageType.Binary"/> and a message is received
        /// </summary>
        public event EventHandler<BinaryMessageEventArgs> OnBinaryMessage;

        /// <summary>
        /// Optional Action used to log messages if the LOG_WS preprocessor directive is defined.
        /// Logs to console by default.
        /// </summary>
        public Action<string, int> Logger { get; set; } = (s, i) => { Console.WriteLine("$[Severity {i}] [[Websocket]] {s}"); };

        private readonly WebSocketMessageType _msgType;
        private readonly Uri _uri;

        private ConcurrentQueue<IEnumerable<byte>> _rQ;
        private ConcurrentQueue<IEnumerable<byte>> _sQ;

        private SemaphoreSlim _sem;
        private bool _disconnecting;

        private void ConditionalLog(string msg, int level)
        {
            //0 - information
            //1 - system information
            //2 - system state change information
            //3 - cancellation
            //4 - TBA
            //5 - exception
#if LOG_WS
            Logger(msg, level);
#endif
        }

        /// <summary>
        /// Constructs a new WebSocket that will connect to the provided internet address and read messages in the provided format.
        /// Can be cancelled through the provided <see cref="CancellationToken"/>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="msgType"></param>
        /// <param name="tokenSource"></param>
        public WebSocket(string address, WebSocketMessageEncoding msgType, System.Net.IWebProxy proxy)
            : this(new Uri(address), msgType, proxy) { }

        /// <summary>
        /// Constructs a new WebSocket that will connect to the provided URI and read messages in the provided format.
        /// Can be cancelled through the provided <see cref="CancellationToken"/>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="msgType"></param>
        /// <param name="tokenSource"></param>
        public WebSocket(Uri address, WebSocketMessageEncoding msgType, System.Net.IWebProxy proxy)
        {
            _ws = new ClientWebSocket();
            (_ws as ClientWebSocket).Options.Proxy = proxy;
            _msgType = (WebSocketMessageType)msgType;
            _uri = address;

            _rQ = new ConcurrentQueue<IEnumerable<byte>>();
            _sQ = new ConcurrentQueue<IEnumerable<byte>>();

            //Maximum 2 requests can be made at once, see above
            _sem = new SemaphoreSlim(2, 2);
        }

        /// <summary>
        /// Queues a message to be sent through the WebSocket
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message)
        {
            ConditionalLog($"(Send) Message queued [ msg: {message} ]", 0);
            Send(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Queues bytes to be sent through the WebSocket
        /// </summary>
        /// <param name="bytes"></param>
        public void Send(byte[] bytes)
        {
            ConditionalLog($"(Send) Queued bytes [ len: {bytes.Length} ]", 0);
            _sQ.Enqueue(bytes);
        }

        /// <summary>
        /// Skips the message queue and directly sends over the Websocket.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<SendResult> SendAsync(string message, CancellationToken ct)
        {
            ConditionalLog($"(SendAsync) Message queued [ msg: {message} ]", 0);
            return await SendAsync(Encoding.UTF8.GetBytes(message), ct);
        }

        /// <summary>
        /// Skips the message queue and directly sends over the Websocket.
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<SendResult> SendAsync(byte[] bytes, CancellationToken ct)
        {
            if (_ws.State != WebSocketState.Open)
            {
                ConditionalLog($"(SendAsync) Invalid attempt to send while socket is not open", 5);
                throw new InvalidOperationException("Websocket is not open");
            }

            ConditionalLog($"(SendAsync) Queued bytes [ len: {bytes.Length} ]", 0);
            ArraySegment<byte> buf = new ArraySegment<byte>(bytes);

            await _sem.WaitAsync(ct);

            if (ct.IsCancellationRequested)
            {
                _sem.Release();
                ConditionalLog($"(SendAsync) Operation cancelled", 3);
                return SendResult.Cancellation;
            }

            await _ws.SendAsync(new ArraySegment<byte>(bytes, 0, bytes.Length), _msgType, true, ct);

            return SendResult.Sent;
        }

        /// <summary>
        /// Asynchronously connects to the WebSocket.
        /// </summary>
        /// <returns>The Task running the WebSocket's read and write loops. Awaiting this Task will block until the WebSocket connection
        /// is closed</returns>
        public async Task ConnectAsync(CancellationToken ct)
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting)
            {
                ConditionalLog($"(ConnectAsync) Connect attempt ignored as connection is already established", 1);
                return;
            }

            await ((ClientWebSocket)_ws).ConnectAsync(_uri, ct); //non-blocking
            ConditionalLog("(ConnectAsync) Socket connected", 2);
        }

        public async Task<Task<ConnectionResult[]>> ReadWriteAsync(CancellationToken ct)
        {
            //Start a new task that will run the Read and Write loops.
            //This task is non-blocking and will complete when the Read and Write loops completes.
            //Due to the continuations above, if either loop completes it should cancel the other, causing this task to complete
            Task<ConnectionResult[]> connectionTask = await Task.Factory.StartNew(
                () => Task.WhenAll(ReadLoopAsync(ct), WriteLoopAsync(ct)),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            ); //non-blocking

            return connectionTask;
        }

        public async Task DisconnectAsync(WebSocketCloseStatus status, string description, CancellationToken token)
        {
            if (_ws.State != WebSocketState.Open)
            {
                ConditionalLog($"(DisconnectAsync) Discconnect attempt ignored as connection is already closed", 1);
                return;
            }

            _disconnecting = true;
            await ((ClientWebSocket)_ws).CloseAsync(status, description, token);
            ConditionalLog("(DisconnectAsync) Socket disconnected", 2);
        }

        /// <summary>
        /// Awaitable task that reads incoming messages from the WebSocket.
        /// This is a blocking method
        /// </summary>
        /// <returns></returns>
        public async Task<ConnectionResult> ReadLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_disconnecting)
            {
                if (_ws.State != WebSocketState.Open)
                {
                    ConditionalLog("(ReadLoopAsync) Invalid attempt to read a closed socket", 5);
                    throw new InvalidOperationException("Websocket is not open");
                }

                if (ct.IsCancellationRequested)
                {
                    ConditionalLog("(ReadLoopAsync) Operation cancelled", 3);
                    return ConnectionResult.ConnectionCancelled;
                }

                await _sem.WaitAsync(ct);

                WebSocketReceiveResult res;
                ArraySegment<byte> buf = new ArraySegment<byte>(new byte[1024]);

                res = await _ws.ReceiveAsync(buf, ct);

                _sem.Release();
                //We don't know how long the receive task has waited, so check for cancellation again
                if (ct.IsCancellationRequested)
                {
                    ConditionalLog("(ReadLoopAsync) Operation cancelled", 3);
                    return ConnectionResult.ConnectionCancelled;
                }

                if (res.MessageType == WebSocketMessageType.Close)
                {
                    _disconnecting = true;
                    ConditionalLog($"(ReadLoopAsync) Close request: [{res.CloseStatus}] {res.CloseStatusDescription}", 2);
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close request acknowledged", ct);
                    ConditionalLog("(ReadLoopAsync) Socket disconnected", 2);
                    return ConnectionResult.Disconnecting;
                }

                if (!res.EndOfMessage)
                {
                    _rQ.Enqueue(buf.Take(res.Count));
                    continue;
                }

                List<byte> msg = new List<byte>();
                while (!_rQ.IsEmpty)
                {
                    _rQ.TryDequeue(out IEnumerable<byte> result);
                    msg.AddRange(result);
                }

                msg.AddRange(buf.Take(res.Count));

                ConditionalLog($"(ReadLoopAsync) Received bytes [ len: {res.Count} ]", 0);

                if (res.MessageType == WebSocketMessageType.Binary)
                {
                    OnBinaryMessage?.Invoke(this, new BinaryMessageEventArgs(msg.ToArray()));
                }
                else
                {
                    string strMsg = Encoding.UTF8.GetString(msg.ToArray());
                    OnTextMessage?.Invoke(this, new StringMessageEventArgs(strMsg));
                }
            }

            if (ct.IsCancellationRequested)
            {
                ConditionalLog("(ReadLoopAsync) Operation cancelled", 3);
                return ConnectionResult.ConnectionCancelled;
            }

            ConditionalLog("(ReadLoopAsync) Socket disconnecting", 2);
            return ConnectionResult.Disconnecting;
        }

        /// <summary>
        /// Awaitable Task that sends pending messages over the WebSocket.
        /// This is a blocking method
        /// </summary>
        /// <returns></returns>
        public async Task<ConnectionResult> WriteLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_disconnecting)
            {
                if (_sQ.Count < 1)
                {
                    //100ms delay to stop the loop from smashing CPU
                    await Task.Delay(100, ct);
                    continue;
                }

                if (_ws.State != WebSocketState.Open)
                {
                    ConditionalLog("(WriteLoopAsync) Invalid attempt to write to a closed socket", 5);
                    throw new InvalidOperationException("Websocket is not open");
                }

                await _sem.WaitAsync(ct);

                if (ct.IsCancellationRequested)
                {
                    _sem.Release();
                    ConditionalLog("(WriteLoopAsync) Operation cancelled", 3);
                    return ConnectionResult.ConnectionCancelled;
                }

                _sQ.TryDequeue(out IEnumerable<byte> buf);
                byte[] bufArray = buf.ToArray();
                await _ws.SendAsync(new ArraySegment<byte>(bufArray, 0, bufArray.Length), _msgType, true, ct);
                ConditionalLog($"(WriteLoopAsync) Sent bytes [ len: {bufArray.Length} ]", 0);

                _sem.Release();

                if (ct.IsCancellationRequested)
                {
                    ConditionalLog("(WriteLoopAsync) Operation cancelled", 3);
                    return ConnectionResult.ConnectionCancelled;
                }
            }

            if (ct.IsCancellationRequested)
            {
                ConditionalLog("(WriteLoopAsync) Operation cancelled", 3);
                return ConnectionResult.ConnectionCancelled;
            }

            ConditionalLog("(WriteLoopAsync) Socket disconnecting", 2);
            return ConnectionResult.Disconnecting;
        }
    }
}
