using System;
using System.IO;
using System.Linq;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;

namespace OpenPgpMailProxy
{
    public class SmtpSendTask : IMailTask
    {
        private readonly Context _context;

        public SmtpSendTask(Context context)
        {
            _context = context;
        }

        private void Send(MailboxConfig config, Envelope envelope)
        {
            var client = new SmtpClient();
            client.Connect(config.SmtpServerAddress, config.SmtpServerPort);
            client.Authenticate(config.Username, config.RemotePassword);
            client.Send(envelope.Message);
            Console.Error.WriteLine("Mail sent via SMTP");
        }

        private void Run(MailboxConfig config, IMailbox mailbox)
        {
            foreach (var envelope in mailbox.List().ToList())
            {
                Send(config, envelope);
                mailbox.Delete(envelope);
            }
        }

        private void Run(MailboxConfig config)
        {
            var mailbox = _context.Mailboxes.Get(config.Username, MailboxType.OutboundOutput);
            mailbox.Lock();
            try
            {
                Run(config, mailbox);
            }
            finally
            {
                mailbox.Release();
            }
        }

        public void Run()
        {
            foreach (var user in _context.Config.Mailboxes)
            {
                Run(user);
            }
        }
    }
}
