using System;

namespace WebSocketting
{
	public class MessageEventArgs<T> : EventArgs
	{
		public T Data { get; }

		public MessageEventArgs(T data)
		{
			Data = data;
		}
	}
}
