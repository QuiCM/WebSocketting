using System;

namespace WebSocketting
{
	/// <summary>
	/// Contains information received from a websocket.
	/// </summary>
	/// <typeparam name="T">Type of information. Should be <see cref="String"/> or <see cref="byte"/>[]</typeparam>
	public abstract class MessageEventArgs<T> : EventArgs
	{
		/// <summary>
		/// Data received from the websocket
		/// </summary>
		public T Data { get; }

		/// <summary>
		/// Creates a new instance of <see cref="MessageEventArgs{T}"/> with the provided data
		/// </summary>
		/// <param name="data"></param>
		public MessageEventArgs(T data)
		{
			Data = data;
		}
	}

	/// <summary>
	/// String implementation of <see cref="MessageEventArgs{T}"/>
	/// </summary>
	public class StringMessageEventArgs : MessageEventArgs<string>
	{
		/// <summary>
		/// Creates a new instance of <see cref="StringMessageEventArgs"/> with the provided data
		/// </summary>
		/// <param name="data"></param>
		public StringMessageEventArgs(string data) : base(data)
		{
		}
	}

	/// <summary>
	/// Binary implementation of <see cref="MessageEventArgs{T}"/>
	/// </summary>
	public class BinaryMessageEventArgs : MessageEventArgs<byte[]>
	{
		/// <summary>
		/// Creates a new instance of <see cref="BinaryMessageEventArgs"/> with the provided data
		/// </summary>
		/// <param name="data"></param>
		public BinaryMessageEventArgs(byte[] data) : base(data)
		{
		}
	}
}
