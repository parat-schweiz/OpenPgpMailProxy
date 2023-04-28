using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace OpenPgpMailProxy
{
    public abstract class Server<TSession>
        where TSession : Session
    {
        private TcpListener _listener;
        private List<TSession> _sessions;

        public Server(IPEndPoint endPoint)
        {
            _sessions = new List<TSession>();
            _listener = new TcpListener(endPoint);
            _listener.Start();
        }

        protected abstract TSession Create(TcpClient client);

        public bool Process()
        {
            bool progress = false;

            if (_listener.Pending())
            {
                var client = _listener.AcceptTcpClient();
                var session = Create(client);
                session.Start();
                _sessions.Add(session);
                progress = true;
            }

            _sessions.RemoveAll(s => !s.Alive);

            return progress;
        }
    }
}
