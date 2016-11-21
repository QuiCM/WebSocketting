using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketting
{
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

		public async Task CloseAsync(string reason)
		{
			await _sock.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, _token);
			_sock.Dispose();
		}

		public async Task SendAsync(string msg)
		{
			await SendBytesAsync(Encoding.UTF8.GetBytes(msg));
		}

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
				return;
			}

			if (!res.EndOfMessage)
			{
				_queue.Add(buf);
				return;
			}

			byte[] send;
			if (_queue.Count != 0)
			{
				send = _queue.SelectMany(q => q.Array).Concat(buf.Array).ToArray();
				_queue.Clear();
			}
			else
			{
				send = buf.Array;
			}

			if (res.MessageType == WebSocketMessageType.Binary)
			{
				ReceivedBinaryMessage?.Invoke(
					this,
					new MessageEventArgs<byte[]>(send)
				);
			}
			else
			{
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
