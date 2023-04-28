using System;
using MailKit.Net.Pop3;

namespace OpenPgpMailProxy
{
    public class Pop3RecieveTask : IMailTask
    {
        private Context _context;

        public Pop3RecieveTask(Context context)
        {
            _context = context;
        }

        private void Run(MailboxConfig config, IMailbox mailbox)
        {
            using (var client = new Pop3Client())
            {
                client.Connect(config.Pop3ServerAddress, config.Pop3ServerPort);
                client.Authenticate(config.Username, config.RemotePassword);
                var count = client.GetMessageCount();
                if (count > 0)
                {
                    foreach (var message in client.GetMessages(0, count))
                    {
                        var envelope = new Envelope(Guid.NewGuid().ToString(), message);
                        mailbox.Enqueue(envelope);
                        Console.Error.WriteLine("Mail received from POP3");
                    }
                    client.DeleteMessages(0, count);
                    client.Disconnect(true);
                }
            }
        }

        private void Run(MailboxConfig config)
        {
            var mailbox = _context.Mailboxes.Get(config.Username, MailboxType.InboundInput);
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
