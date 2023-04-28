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

        private void Process(IMailbox source, IMailbox sink, IMailProcessor processor)
        { 
            foreach (var envelope in source.List().ToList())
            {
                var processed = processor.Process(envelope);
                sink.Enqueue(processed);
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
                sink.Lock();
                try
                {
                    Process(source, sink, processor);
                }
                finally
                {
                    sink.Release();
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
                Process(user.Username, MailboxType.InboundInput, MailboxType.InboundOutput, _inboundProcessor);
                Process(user.Username, MailboxType.OutboundInput, MailboxType.OutboundOutput, _outboundProcessor);
            }
        }
    }
}
