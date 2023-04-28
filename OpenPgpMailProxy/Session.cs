using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ThrowException.CSharpLibs.BytesUtilLib;

namespace OpenPgpMailProxy
{
    public abstract class Session : IDisposable
    {
        private TcpClient _client;
        private Stream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private Thread _thread;
        protected Context _context;

        public Session(Context context, TcpClient client)
        {
            _client = client;
            _stream = _client.GetStream();
            _reader = new StreamReader(_stream);
            _writer = new StreamWriter(_stream);
            _context = context;
        }

        public void Start()
        {
            _thread = new Thread(Run);
            _thread.Start();
        }

        public bool Alive
        {
            get { return _client != null && _client.Connected; }
        }

        protected abstract void Process();

        protected virtual void Write(string text)
        {
            _writer.Write(text);
            _writer.Flush();
        }

        protected virtual void WriteLine(string text)
        {
            _writer.WriteLine(text);
            _writer.Flush();
        }

        protected virtual void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
            _stream.Flush();
        }

        protected virtual void Read(byte[] buffer, int offset, int count)
        {
            _stream.Read(buffer, offset, count);
        }

        protected virtual string Read(int length)
        {
            var buffer = new char[length];
            int chars = _reader.Read(buffer, 0, buffer.Length);
            return new String(buffer, 0, chars);
        }

        protected virtual string ReadLine()
        {
            return _reader.ReadLine();
        }

        private void Run()
        {
            try
            {
                Process();
            }
            catch (SocketException) { }
            catch (IOException) { }
            Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_client != null)
            {
                _client.Close();
                _client = null;
            }
        }
    }
}
