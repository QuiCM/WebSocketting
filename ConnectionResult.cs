using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;

namespace WebSocketting
{
    public enum ConnectionResult
    {
        WebsocketException  = 0,
        ConnectionCancelled = 1,
        Disconnecting = 2
    }
}
