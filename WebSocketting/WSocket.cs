using System;
using System.Collections.Generic;
using System.Linq;
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
		private ClientWebSocket _sock;
		private CancellationToken _token;
		private List<ArraySegment<byte>> _queue = new List<ArraySegment<byte>>();

		public event EventHandler<MessageEventArgs<string>> ReceivedTextMessage;
		public event EventHandler<MessageEventArgs<byte[]>> ReceivedBinaryMessage;
		public event EventHandler Connected;
		public event EventHandler Disconnected;

		public bool IsClosed => _sock.State != WebSocketState.Open;

		public WSocket(Uri uri, CancellationToken token)
		{
			_uri = uri;
			_token = token;
		}

		public WSocket(string url, CancellationToken token) : this(new Uri(url), token)
		{
		}

		/// <summary>
		/// Asynchronously establishes a connection with a websocket
		/// </summary>
		/// <returns></returns>
		public async Task ConnectAsync()
		{
			_sock = new ClientWebSocket();
			await _sock.ConnectAsync(_uri, _token);

			Connected?.Invoke(this, null);

			while (_sock.State == WebSocketState.Open)
			{
				await ProcessReadLoop();
			}
		}

		/// <summary>
		/// Asynchronously closes a connection with a websocket
		/// </summary>
		/// <param name="reason"></param>
		/// <returns></returns>
		public async Task CloseAsync(string reason)
		{
			await _sock.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, _token);
			_sock.Dispose();
		}

		/// <summary>
		/// Asynchronously sends a message to a websocket
		/// </summary>
		/// <param name="msg"></param>
		/// <returns></returns>
		public async Task SendAsync(string msg)
		{
			await SendBytesAsync(Encoding.UTF8.GetBytes(msg));
		}

		/// <summary>
		/// Asynchronously sends binary data to a websocket
		/// </summary>
		/// <param name="data"></param>
		/// <param name="finalMsg"></param>
		/// <param name="isText"></param>
		/// <returns></returns>
		public async Task SendBytesAsync(byte[] data, bool finalMsg = true, bool isText = true)
		{
			ArraySegment<byte> buf = new ArraySegment<byte>(data);
			await _sock.SendAsync(
				buf,
				isText ? WebSocketMessageType.Text
					: WebSocketMessageType.Binary,
				finalMsg,
				_token);
		}

		private async Task ProcessReadLoop()
		{
			ArraySegment<byte> buf = new ArraySegment<byte>(new byte[1024]);
			WebSocketReceiveResult res = await _sock.ReceiveAsync(buf, _token);

			if (res.MessageType == WebSocketMessageType.Close)
			{
				Disconnected?.Invoke(this, null);
				await _sock.CloseAsync(res.CloseStatus.Value, res.CloseStatusDescription, _token);
				return;
			}

			if (!res.EndOfMessage)
			{
				//if we have only read some of the data, store what we've got so far
				_queue.Add(buf);
				return;
			}

			byte[] send;
			if (_queue.Count != 0)
			{
				//Concat all the buffered data into one array
				send = _queue.SelectMany(q => q.Array).Concat(buf.Array).ToArray();
				_queue.Clear();
			}
			else
			{
				send = buf.Array;
			}

			if (res.MessageType == WebSocketMessageType.Binary)
			{
				//Send raw binary data
				ReceivedBinaryMessage?.Invoke(
					this,
					new MessageEventArgs<byte[]>(send)
				);
			}
			else
			{
				//Send a string
				string s = Encoding.UTF8.GetString(send).Replace("\0", "");
				ReceivedTextMessage?.Invoke(
					this,
					new MessageEventArgs<string>(s)
				);
			}
		}

		public void Dispose()
		{
			_sock.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client exiting", _token);
		}
	}
}
