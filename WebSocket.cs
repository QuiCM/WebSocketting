using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private readonly WebSocketMessageType _msgType;
        private readonly Uri _uri;

        private ConcurrentQueue<ArraySegment<byte>> _rQ;
        private ConcurrentQueue<ArraySegment<byte>> _sQ;

        private SemaphoreSlim _sem;

        /// <summary>
        /// Constructs a new WebSocket that will connect to the provided internet address and read messages in the provided format.
        /// Can be cancelled through the provided <see cref="CancellationToken"/>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="msgType"></param>
        /// <param name="tokenSource"></param>
        public WebSocket(string address, WebSocketMessageEncoding msgType)
            : this(new Uri(address), msgType) { }

        /// <summary>
        /// Constructs a new WebSocket that will connect to the provided URI and read messages in the provided format.
        /// Can be cancelled through the provided <see cref="CancellationToken"/>
        /// </summary>
        /// <param name="address"></param>
        /// <param name="msgType"></param>
        /// <param name="tokenSource"></param>
        public WebSocket(Uri address, WebSocketMessageEncoding msgType)
        {
            _ws = new ClientWebSocket();
            _msgType = (WebSocketMessageType)msgType;
            _uri = address;

            _rQ = new ConcurrentQueue<ArraySegment<byte>>();
            _sQ = new ConcurrentQueue<ArraySegment<byte>>();

            //Maximum 2 requests can be made at once, see above
            _sem = new SemaphoreSlim(2, 2);
        }

        /// <summary>
        /// Queues a message to be sent through the WebSocket
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message) => Send(Encoding.UTF8.GetBytes(message));

        /// <summary>
        /// Queues bytes to be sent through the WebSocket
        /// </summary>
        /// <param name="bytes"></param>
        public void Send(byte[] bytes)
        {
            ArraySegment<byte> buf = new ArraySegment<byte>(bytes);
            _sQ.Enqueue(buf);
        }

        /// <summary>
        /// Asynchronously connects to the WebSocket. This is a blocking method
        /// </summary>
        /// <returns></returns>
        public async Task ConnectAsync(CancellationToken ct)
        {
            await ((ClientWebSocket)_ws).ConnectAsync(_uri, ct); //non-blocking

            Task read = ReadLoopAsync(ct);
            Task write = WriteLoopAsync(ct);

            Task.WaitAny(new Task[] { read, write }, ct);

            if (read.Exception != null)
            {
                throw read.Exception;
            }

            if (write.Exception != null)
            {
                throw write.Exception;
            }
        }

        /// <summary>
        /// Awaitable task that reads incoming messages from the WebSocket.
        /// This is a blocking method
        /// </summary>
        /// <returns></returns>
        public async Task ReadLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();

                await _sem.WaitAsync(ct);

                WebSocketReceiveResult res;
                ArraySegment<byte> buf = new ArraySegment<byte>(new byte[1024]);

                res = await _ws.ReceiveAsync(buf, ct);

                _sem.Release();
                //We don't know how long the receive task has waited, so check for cancellation again and throw
                //if we need to cancel
                ct.ThrowIfCancellationRequested();

                if (res.MessageType == WebSocketMessageType.Close)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close request acknowledged", ct);
                    break;
                }

                if (!res.EndOfMessage)
                {
                    _rQ.Enqueue(buf);
                    continue;
                }

                List<byte> msg = new List<byte>();
                while (!_rQ.IsEmpty)
                {
                    _rQ.TryDequeue(out ArraySegment<byte> result);
                    msg.AddRange(result.Array);
                }

                msg.AddRange(buf.Array);

                if (res.MessageType == WebSocketMessageType.Binary)
                {
                    //event for binary
                }
                else
                {
                    string strMsg = Encoding.UTF8.GetString(msg.ToArray());
                    //event for string
                }
            }
        }

        /// <summary>
        /// Awaitable Task that sends pending messages over the WebSocket.
        /// This is a blocking method
        /// </summary>
        /// <returns></returns>
        public async Task WriteLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_sQ.Count < 1)
                {
                    //100ms delay to stop the loop from smashing CPU
                    await Task.Delay(100, ct);
                    continue;
                }

                if (_ws.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is not open");
                }

                await _sem.WaitAsync(ct);
                ct.ThrowIfCancellationRequested();

                _sQ.TryDequeue(out ArraySegment<byte> buf);
                await _ws.SendAsync(buf, _msgType, true, ct);
                ct.ThrowIfCancellationRequested();

                _sem.Release();
            }
        }
    }
}
