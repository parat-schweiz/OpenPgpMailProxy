using System;
using System.Net;
using System.Net.Sockets;
using ThrowException.CSharpLibs.LogLib;

namespace OpenPgpMailProxy
{
    public class Pop3Server : Server<Pop3Session>
    {
        private Context _context;

        public Pop3Server(Context context, IPEndPoint endPoint)
            : base(endPoint)
        {
            _context = context;
            _context.Log.Notice("POP3 server started");
        }

        protected override Pop3Session Create(TcpClient client)
        {
            return new Pop3Session(_context, client);
        }
    }
}
