using System;
using System.Collections.Generic;
using System.Text;

namespace WebSocketting
{
    /// <summary>
    /// Constants providing integer identities to events
    /// </summary>
    public static class EventIdentifiers
    {
        /// <summary>
        /// A string message has been queued
        /// </summary>
        public const int WS_VER_QMSG = 0;
        /// <summary>
        /// A byte[] message has been queued
        /// </summary>
        public const int WS_VER_QBYTES = 1;
        /// <summary>
        /// A string message has been queued asynchronously
        /// </summary>
        public const int WS_VER_QMSG_ASYNC = 2;
        /// <summary>
        /// A byte[] message has been queued asynchronously
        /// </summary>
        public const int WS_VER_QBYTES_ASYNC = 3;
        /// <summary>
        /// A byte[] message has been received asynchronously
        /// </summary>
        public const int WS_VER_RBYTES_ASYNC = 4;
        /// <summary>
        /// A byte[] message has been sent asynchronously
        /// </summary>
        public const int WS_VER_SBYTES_ASYNC = 5;

        /// <summary>
        /// Send loop has been cancelled
        /// </summary>
        public const int WS_WAR_CANC_SEND = 10;
        /// <summary>
        /// Read loop has been cancelled
        /// </summary>
        public const int WS_WAR_CANC_READ = 11;
        /// <summary>
        /// Async Read loop has been cancelled
        /// </summary>
        public const int WS_WAR_CANC_READ_ASYNC = 12;
        /// <summary>
        /// Async Write loop has been cancelled
        /// </summary>
        public const int WS_WAR_CANC_SEND_ASYNC = 13;

        /// <summary>
        /// Erroneous attempt to send while socket is closed
        /// </summary>
        public const int WS_ERR_SEND_SOCK_CLOSE = 20;
        /// <summary>
        /// Erroneous attempt to read while socket is closed
        /// </summary>
        public const int WS_ERR_READ_SOCK_CLOSE = 21;
        /// <summary>
        /// Erroneous attempt to async send while socket is closed
        /// </summary>
        public const int WS_ERR_SEND_SOCK_CLOSE_ASYNC = 22;
        /// <summary>
        /// Erroneous attempt to async read while socket is closed
        /// </summary>
        public const int WS_ERR_READ_SOCK_CLOSE_ASYNC = 23;

        /// <summary>
        /// Attempted to connect while connection is already open
        /// </summary>
        public const int WS_VER_CONN_ALR_OPEN = 30;
        /// <summary>
        /// Connection has been opened
        /// </summary>
        public const int WS_INF_CONN_OPEN = 31;
        /// <summary>
        /// Connection has been closed
        /// </summary>
        public const int WS_INF_CONN_CLOSE = 32;
        /// <summary>
        /// Attempted to close while connection is already closed
        /// </summary>
        public const int WS_VER_CONN_ALR_CLOSE = 33;
        /// <summary>
        /// Server has requested that the connection be closed
        /// </summary>
        public const int WS_INF_CONN_CLOSE_REQ = 34;
    }
}
