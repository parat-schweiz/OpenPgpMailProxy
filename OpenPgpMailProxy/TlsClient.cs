using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace OpenPgpMailProxy
{
    public class TlsClient : IDisposable
    {
        private TcpClient _client;
        private SslStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;

        public TlsClient(IPEndPoint endPoint, string targetHost)
        {
            _client = new TcpClient();
            _client.Connect(endPoint);
            _stream = new SslStream(_client.GetStream(), true);
            _stream.AuthenticateAsClient(targetHost);
            _reader = new StreamReader(_stream, Encoding.ASCII);
            _writer = new StreamWriter(_stream, Encoding.ASCII);
        }

        public void Write(string text)
        {
            _writer.Write(text);
            _writer.Flush();
        }

        public void WriteLine(string text)
        {
            _writer.WriteLine(text);
            _writer.Flush();
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
            _stream.Flush();
        }

        public void Read(byte[] buffer, int offset, int count)
        {
            _stream.Read(buffer, offset, count);
        }

        protected string Read(int length)
        {
            var buffer = new char[length];
            int chars = _reader.Read(buffer, 0, buffer.Length);
            return new String(buffer, 0, chars);
        }

        public string ReadLine()
        {
            return _reader.ReadLine();
        }

        public void Dispose()
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }

            if (_client != null)
            {
                _client.Close();
                _client = null;
            }
        }
    }
}
