using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WebSocketting
{
	public static class Extensions
	{
		public static async Task<byte[]> ReadBytesAsync(this NetworkStream stream, int length)
		{
			byte[] buf = new byte[length];
			if (length == 0)
			{
				return buf;
			}

			int read = await stream.ReadAsync(buf, 0, length);
			if (read == 0)
			{
				throw new EndOfStreamException();
			}

			return buf;
		}

		public static async Task<ushort> ReadUShortAsync(this NetworkStream stream, bool littleEndian = false)
		{
			byte[] buf = await ReadBytesAsync(stream, 2);

			if (!littleEndian)
			{
				buf.ConvertEndianness();
			}

			return BitConverter.ToUInt16(buf, 0);
		}

		public static async Task<ulong> ReadULongAsync(this NetworkStream stream, bool littleEndian = false)
		{
			byte[] buf = await ReadBytesAsync(stream, 8);

			if (!littleEndian)
			{
				buf.ConvertEndianness();
			}

			return BitConverter.ToUInt64(buf, 0);

		}

		public static void ConvertEndianness(this byte[] buf)
		{
			Array.Reverse(buf);
		}

		public static async Task<string> ReadHttpHeaderAsync(this NetworkStream stream)
		{
			byte[] buffer = new byte[1024 * 16];

			int read = await stream.ReadAsync(buffer, 0, buffer.Length);

			string header = Encoding.UTF8.GetString(buffer, 0, read);

			if (!header.EndsWith("\r\n\r\n"))
			{
				throw new InvalidDataException("Header message was not in the correct format.");
			}

			return header;
		}

		public static string ReadHttpHeader(this Stream stream)
		{
			int length = 1024 * 16; // 16KB buffer more than enough for http header
			byte[] buffer = new byte[length];
			int offset = 0;
			int bytesRead = 0;
			do
			{
				if (offset >= length)
				{
					throw new Exception();
				}

				bytesRead = stream.Read(buffer, offset, length - offset);
				offset += bytesRead;
				string header = Encoding.UTF8.GetString(buffer, 0, offset);

				// as per http specification, all headers should end this this
				if (header.Contains("\r\n\r\n"))
				{
					return header;
				}

			} while (bytesRead > 0);

			return string.Empty;
		}
	}
}
