using System;
using System.Net.WebSockets;

namespace WebSocketting
{
	/// <summary>
	/// Used when a websocket disconnects
	/// </summary>
    public class DisconnectEventArgs : EventArgs
    {
		/// <summary>
		/// Status describing the disconnect event
		/// </summary>
		public WebSocketCloseStatus Status { get; }
		/// <summary>
		/// Reason string for the disconnect
		/// </summary>
		public string Message { get; }

		public DisconnectEventArgs(WebSocketCloseStatus status, string message)
		{
			Status = status;
			Message = message;
		}
    }
}
