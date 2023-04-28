using System;
using System.Net;
using System.Net.Sockets;

namespace OpenPgpMailProxy
{
    public class Pop3Server : Server<Pop3Session>
    {
        private Context _context;
        private readonly IMailQueue _queue;

        public Pop3Server(Context context, IPEndPoint endPoint, IMailQueue queue)
            : base(endPoint)
        {
            _context = context;
            _queue = queue;
        }

        protected override Pop3Session Create(TcpClient client)
        {
            return new Pop3Session(_context, client, _queue);
        }
    }
}
