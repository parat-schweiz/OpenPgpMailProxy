using System;
using System.Net;
using System.Net.Sockets;

namespace OpenPgpMailProxy
{
    public class SmtpServer : Server<SmtpSession>
    {
        private Context _context;
        private IMailQueue _queue;

        public SmtpServer(Context context, IPEndPoint endPoint, IMailQueue queue)
            : base(endPoint)
        {
            _context = context;
            _queue = queue;
        }

        protected override SmtpSession Create(TcpClient client)
        {
            return new SmtpSession(_context, client, _queue);
        }
    }
}
