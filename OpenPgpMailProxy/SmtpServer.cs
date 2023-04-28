using System;
using System.Net;
using System.Net.Sockets;

namespace OpenPgpMailProxy
{
    public class SmtpServer : Server<SmtpSession>
    {
        private Context _context;

        public SmtpServer(Context context, IPEndPoint endPoint)
            : base(endPoint)
        {
            _context = context;
        }

        protected override SmtpSession Create(TcpClient client)
        {
            return new SmtpSession(_context, client);
        }
    }
}
