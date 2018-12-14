#define TRACE

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

        private readonly WebSocketMessageType _msgType;
        private readonly Uri _uri;

        private ConcurrentQueue<IEnumerable<byte>> _rQ;
        private ConcurrentQueue<IEnumerable<byte>> _sQ;

        private SemaphoreSlim _sem;
        private bool _disconnecting;

#if NETFRAMEWORK
        //DotNet Core & Standard do not currently support configuring a TraceSource from a configuration file
        //This means that TraceSource will use a DefaultTraceListener and output to the default location.
        //Until this is fixed we'll just use Trace.WriteLine and people can add their own listeners to Trace programmatically.
        //Unfortunately ConsoleTraceListener doesn't exist in NetCore 2.0 so we can't even use that
        static TraceSource _ts = new TraceSource("WebSocket");
#endif

        private void TraceEvent(string msg, int identifier, TraceEventType level)
        {
#if NETSTANDARD || NETCORE
            Trace.WriteLine(msg, "WebSocket");
#else
            _ts.TraceEvent(level, identifier, msg);
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
            TraceEvent($"(Send) Message queued [ msg: {message} ]", EventIdentifiers.WS_VER_QMSG, TraceEventType.Verbose);
            Send(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Queues bytes to be sent through the WebSocket
        /// </summary>
        /// <param name="bytes"></param>
        public void Send(byte[] bytes)
        {
            TraceEvent($"(Send) Queued bytes [ len: {bytes.Length} ]", EventIdentifiers.WS_VER_QBYTES, TraceEventType.Verbose);
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
            TraceEvent($"(SendAsync) Message queued [ msg: {message} ]", EventIdentifiers.WS_VER_QMSG_ASYNC, TraceEventType.Verbose);
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
                TraceEvent(
                    $"(SendAsync) Invalid attempt to send while socket is not open",
                    EventIdentifiers.WS_ERR_SEND_SOCK_CLOSE,
                    TraceEventType.Error
                );
                throw new InvalidOperationException("Websocket is not open");
            }

            TraceEvent($"(SendAsync) Queued bytes [ len: {bytes.Length} ]", EventIdentifiers.WS_VER_QMSG_ASYNC, TraceEventType.Verbose);
            ArraySegment<byte> buf = new ArraySegment<byte>(bytes);

            await _sem.WaitAsync(ct);

            if (ct.IsCancellationRequested)
            {
                _sem.Release();
                TraceEvent($"(SendAsync) Operation cancelled", EventIdentifiers.WS_WAR_CANC_SEND, TraceEventType.Warning);
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
                TraceEvent($"(ConnectAsync) Connect attempt ignored as connection is already established",
                    EventIdentifiers.WS_VER_CONN_ALR_OPEN,
                    TraceEventType.Information
                );
                return;
            }

            await ((ClientWebSocket)_ws).ConnectAsync(_uri, ct); //non-blocking
            TraceEvent("(ConnectAsync) Socket connected", EventIdentifiers.WS_INF_CONN_OPEN, TraceEventType.Information);
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
                TraceEvent(
                    $"(DisconnectAsync) Discconnect attempt ignored as connection is already closed",
                    EventIdentifiers.WS_VER_CONN_ALR_CLOSE,
                    TraceEventType.Verbose
                );
                return;
            }

            _disconnecting = true;
            await ((ClientWebSocket)_ws).CloseAsync(status, description, token);
            TraceEvent(
                "(DisconnectAsync) Socket disconnected",
                EventIdentifiers.WS_INF_CONN_CLOSE,
                TraceEventType.Information
           );
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
                    TraceEvent(
                        "(ReadLoopAsync) Invalid attempt to read a closed socket",
                        EventIdentifiers.WS_ERR_READ_SOCK_CLOSE_ASYNC,
                        TraceEventType.Error
                    );
                    throw new InvalidOperationException("Websocket is not open");
                }

                if (ct.IsCancellationRequested)
                {
                    TraceEvent(
                        "(ReadLoopAsync) Operation cancelled",
                        EventIdentifiers.WS_WAR_CANC_READ,
                        TraceEventType.Warning
                    );
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
                    TraceEvent(
                        "(ReadLoopAsync) Operation cancelled",
                        EventIdentifiers.WS_WAR_CANC_READ_ASYNC,
                        TraceEventType.Warning
                    );
                    return ConnectionResult.ConnectionCancelled;
                }

                if (res.MessageType == WebSocketMessageType.Close)
                {
                    _disconnecting = true;
                    TraceEvent(
                        $"(ReadLoopAsync) Close request: [{res.CloseStatus}] {res.CloseStatusDescription}",
                        EventIdentifiers.WS_INF_CONN_CLOSE_REQ,
                        TraceEventType.Information
                    );
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close request acknowledged", ct);
                    TraceEvent(
                        "(ReadLoopAsync) Socket disconnected",
                        EventIdentifiers.WS_INF_CONN_CLOSE,
                        TraceEventType.Information
                    );
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

                TraceEvent(
                    $"(ReadLoopAsync) Received bytes [ len: {res.Count} ]",
                    EventIdentifiers.WS_VER_RBYTES_ASYNC,
                    TraceEventType.Verbose
                );

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
                TraceEvent(
                    "(ReadLoopAsync) Operation cancelled",
                    EventIdentifiers.WS_WAR_CANC_READ_ASYNC,
                    TraceEventType.Warning
                );
                return ConnectionResult.ConnectionCancelled;
            }

            TraceEvent(
                "(ReadLoopAsync) Socket disconnecting",
                EventIdentifiers.WS_INF_CONN_CLOSE,
                TraceEventType.Information
            );
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
                    TraceEvent(
                        "(WriteLoopAsync) Invalid attempt to write to a closed socket",
                        EventIdentifiers.WS_ERR_SEND_SOCK_CLOSE_ASYNC,
                        TraceEventType.Error
                    );
                    throw new InvalidOperationException("Websocket is not open");
                }

                await _sem.WaitAsync(ct);

                if (ct.IsCancellationRequested)
                {
                    _sem.Release();
                    TraceEvent(
                        "(WriteLoopAsync) Operation cancelled",
                        EventIdentifiers.WS_WAR_CANC_SEND_ASYNC,
                        TraceEventType.Warning
                    );
                    return ConnectionResult.ConnectionCancelled;
                }

                _sQ.TryDequeue(out IEnumerable<byte> buf);
                byte[] bufArray = buf.ToArray();
                await _ws.SendAsync(new ArraySegment<byte>(bufArray, 0, bufArray.Length), _msgType, true, ct);
                TraceEvent(
                    $"(WriteLoopAsync) Sent bytes [ len: {bufArray.Length} ]",
                    EventIdentifiers.WS_VER_SBYTES_ASYNC,
                    TraceEventType.Verbose
                );

                _sem.Release();

                if (ct.IsCancellationRequested)
                {
                    TraceEvent(
                        "(WriteLoopAsync) Operation cancelled",
                        EventIdentifiers.WS_WAR_CANC_SEND_ASYNC,
                        TraceEventType.Warning
                    );
                    return ConnectionResult.ConnectionCancelled;
                }
            }

            if (ct.IsCancellationRequested)
            {
                TraceEvent(
                       "(WriteLoopAsync) Operation cancelled",
                       EventIdentifiers.WS_WAR_CANC_SEND_ASYNC,
                       TraceEventType.Warning
                );
                return ConnectionResult.ConnectionCancelled;
            }

            TraceEvent(
                "(WriteLoopAsync) Socket disconnecting",
                EventIdentifiers.WS_INF_CONN_CLOSE,
                TraceEventType.Information
            );
            return ConnectionResult.Disconnecting;
        }
    }
}
