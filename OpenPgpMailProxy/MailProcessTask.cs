using System;
using System.Linq;
using System.Threading;

namespace OpenPgpMailProxy
{
    public class MailProcessTask : IMailTask
    {
        private readonly Context _context;
        private readonly IMailProcessor _inboundProcessor;
        private readonly IMailProcessor _outboundProcessor;

        public MailProcessTask(Context context, IMailProcessor inboundProcessor, IMailProcessor outboundProcessor)
        {
            _context = context;
            _inboundProcessor = inboundProcessor;
            _outboundProcessor = outboundProcessor;
        }

        private void Process(IMailbox source, IMailbox sink, IMailbox errorBox, IMailProcessor processor)
        { 
            foreach (var envelope in source.List().ToList())
            {
                var processed = processor.Process(envelope, errorBox);
                if (processed != null)
                {
                    sink.Enqueue(processed);
                }
                source.Delete(envelope);
                Console.Error.WriteLine("Processed mail");
            }
        }

        private void Process(string username, MailboxType sourceType, MailboxType sinkType, IMailProcessor processor)
        {
            var source = _context.Mailboxes.Get(username, sourceType);
            source.Lock();
            try
            {
                var sink = _context.Mailboxes.Get(username, sinkType);
                if (sinkType == MailboxType.InboundOutput)
                {
                    Process(source, sink, sink, processor);
                }
                else
                {
                    var error = _context.Mailboxes.Get(username, MailboxType.InboundOutput);
                    Process(source, sink, error, processor);
                }
            }
            finally
            {
                source.Release();
            }
        }

        public void Run()
        {
            foreach (var user in _context.Config.Mailboxes)
            {
                Process(user.Username, MailboxType.OutboundInput, MailboxType.OutboundOutput, _outboundProcessor);
                Process(user.Username, MailboxType.InboundInput, MailboxType.InboundOutput, _inboundProcessor);
            }
        }
    }
}
