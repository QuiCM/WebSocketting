using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketting
{
	/// <summary>
	/// Represents a Websocket connection
	/// </summary>
	public class WSocket : IDisposable
	{
		private Uri _uri;
		private ClientWebSocket _sock = new ClientWebSocket();
		private CancellationToken _token;
		private bool _closing;
		private Queue<ArraySegment<byte>> _readQueue = new Queue<ArraySegment<byte>>();
		private Queue<byte[]> _sendQueue = new Queue<byte[]>();
		//These semaphores enforce 1 read and write op at a time, as required for a ClientWebSocket
		private SemaphoreSlim _readSemaphore = new SemaphoreSlim(1, 1);
		private SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);

		/// <summary>
		/// Invoked when a text message is received
		/// </summary>
		public event EventHandler<StringMessageEventArgs> ReceivedTextMessage;
		/// <summary>
		/// Invoked when a binary message is received
		/// </summary>
		public event EventHandler<BinaryMessageEventArgs> ReceivedBinaryMessage;
		/// <summary>
		/// Invoked when the websocket connects
		/// </summary>
		public event EventHandler Connected;
		/// <summary>
		/// Invoked when the websocket disconnects
		/// </summary>
		public event EventHandler<DisconnectEventArgs> Disconnected;

		/// <summary>
		/// Whether or not the websocket is open
		/// </summary>
		public bool IsClosed => _sock.State != WebSocketState.Open;

		/// <summary>
		/// Creates a new <see cref="WSocket"/> that will connect to the given URI, and uses the given <see cref="CancellationToken"/>
		/// </summary>
		/// <param name="url">URI to connect to</param>
		/// <param name="token">CancellationToken used when communicating with the websocket</param>
		public WSocket(Uri uri, CancellationToken token)
		{
			_uri = uri;
			_token = token;
		}

		/// <summary>
		/// Creates a new <see cref="WSocket"/> that will connect to the given URL, and uses the given <see cref="CancellationToken"/>
		/// </summary>
		/// <param name="url">URL to connect to</param>
		/// <param name="token">CancellationToken used when communicating with the websocket</param>
		public WSocket(string url, CancellationToken token) : this(new Uri(url), token)
		{
		}

		/// <summary>
		/// Asynchronously establishes a connection with a websocket
		/// </summary>
		/// <returns></returns>
		public async Task ConnectAsync()
		{
			//Allow re-use of this WSocket
			_closing = false;
			await _sock.ConnectAsync(_uri, _token);

			Connected?.Invoke(this, null);

			Thread read = new Thread(ReadThread);
			Thread send = new Thread(SendThread);

			read.Start();
			send.Start();
		}

		private async void SendThread()
		{
			while (!_token.IsCancellationRequested)
			{
				if (_closing)
				{
					break;
				}

				if (_sock?.State == WebSocketState.Open)
				{
					try
					{
						await _writeSemaphore.WaitAsync(_token);
						await ProcessSendQueueAsync();
						await Task.Delay(10);
					}
					catch (Exception ex)
					{
						//Temp
						Console.WriteLine(ex);
					}
					finally
					{
						_writeSemaphore.Release();
					}
				}
			}
		}

		private async void ReadThread()
		{
			while (!_token.IsCancellationRequested)
			{
				if (_closing)
				{
					break;
				}

				if (_sock?.State == WebSocketState.Open)
				{
					try
					{
						await _readSemaphore.WaitAsync(_token);
						await ProcessReadQueueAsync();
					}
					catch (Exception ex)
					{
						//Temp
						Console.WriteLine(ex);
					}
					finally
					{
						_readSemaphore.Release();
					}
				}
			}
		}

		/// <summary>
		/// Asynchronously closes a connection with a websocket
		/// </summary>
		/// <param name="reason"></param>
		/// <returns></returns>
		public async Task CloseAsync(WebSocketCloseStatus status, string reason)
		{
			if (status == WebSocketCloseStatus.Empty)
			{
				reason = null;
			}

			_closing = true;
			await _sock.CloseAsync(status, reason, _token).ConfigureAwait(false);
			Disconnected?.Invoke(this, new DisconnectEventArgs(status, reason));
		}

		/// <summary>
		/// Queues a message to be sent via the websocket
		/// </summary>
		/// <param name="msg">Message to queue</param>
		public void QueueMessage(string msg)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(msg);
			_sendQueue.Enqueue(bytes);
		}

		private async Task ProcessSendQueueAsync()
		{
			if (_sendQueue.Count == 0)
			{
				return;
			}

			byte[] data = _sendQueue.Dequeue();

			ArraySegment<byte> buf = new ArraySegment<byte>(data);
			await _sock.SendAsync(
				buf,
				WebSocketMessageType.Text,
				true,
				_token).ConfigureAwait(false);
		}

		private async Task ProcessReadQueueAsync()
		{
			ArraySegment<byte> buf = new ArraySegment<byte>(new byte[1024]);
			WebSocketReceiveResult res = await _sock.ReceiveAsync(buf, _token).ConfigureAwait(false);

			if (res.MessageType == WebSocketMessageType.Close)
			{
				if (!_closing)
				{
					await CloseAsync(res.CloseStatus.Value, res.CloseStatusDescription).ConfigureAwait(false);
				}
				return;
			}

			if (!res.EndOfMessage)
			{
				//if we have only read some of the data, store what we've got so far
				_readQueue.Enqueue(buf);
				return;
			}

			List<byte> send = new List<byte>();
			while (_readQueue.Count != 0)
			{
				//Push all the buffered data into a list
				send.AddRange(_readQueue.Dequeue().Array);
			}
			send.AddRange(buf.Array);

			if (res.MessageType == WebSocketMessageType.Binary)
			{
				//Send raw binary data
				ReceivedBinaryMessage?.Invoke(
					this,
					new BinaryMessageEventArgs(send.ToArray())
				);
			}
			else
			{
				//Send a string
				string s = Encoding.UTF8.GetString(send.ToArray()).Replace("\0", "");
				ReceivedTextMessage?.Invoke(
					this,
					new StringMessageEventArgs(s)
				);
			}
		}

		/// <summary>
		/// Closes and disposes an open websocket connection and raises the Disconnected event
		/// </summary>
		public async void Dispose()
		{
			await _sock.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client exiting", _token).ConfigureAwait(false);
			Disconnected?.Invoke(this, new DisconnectEventArgs(WebSocketCloseStatus.NormalClosure, "Client exiting"));
		}
	}
}
