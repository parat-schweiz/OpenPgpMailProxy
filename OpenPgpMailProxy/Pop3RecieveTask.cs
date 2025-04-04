using System;
using System.Threading;
using MailKit.Net.Pop3;
using ThrowException.CSharpLibs.LogLib;

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
                        _context.Log.Notice("POP3 client: mail recieved");
                    }
                    client.DeleteMessages(0, count);
                    client.Disconnect(true);
                }
            }
        }

        private void Run(MailboxConfig config)
        {
            _context.Log.Verbose("POP3 client: querying mailbox {0}", config.Username);
            var mailbox = _context.Mailboxes.Get(config.Username, MailboxType.InboundInput);
            Run(config, mailbox);
        }

        private int BackoffMilliseconds(int counter)
        { 
            if (counter <= 1)
            {
                return 100;
            }
            else if (counter <= 2)
            {
                return 1000;
            }
            else if (counter <= 5)
            {
                return 2000;
            }
            else
            {
                return 3000;
            }
        }

        private void Retry(Action action)
        {
            int counter = 1;
            while (true)
            { 
                try
                {
                    action();
                    return;
                }
                catch (Exception exception)
                {
                    if (counter > 10)
                    {
                        _context.Log.Warning(exception.Message);
                        Thread.Sleep(BackoffMilliseconds(counter));
                        _context.Log.Warning("Retry {0}", counter);
                    }
                    else
                    {
                        _context.Log.Error("Counter exhausted. Error:");
                        _context.Log.Error(exception.ToString());
                        return;
                    }
                    counter++;
                }
            }
        }

        public void Run()
        {
            _context.Log.Info("POP3 client: running task");
            foreach (var user in _context.Config.Mailboxes)
            {
                Retry(() => Run(user));
            }
        }
    }
}
