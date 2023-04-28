using System;
using System.Collections.Generic;

namespace OpenPgpMailProxy
{
    public class MemoryMailQueue : IMailbox
    {
        private readonly List<Envelope> _envelopes;

        public MemoryMailQueue()
        {
            _envelopes = new List<Envelope>();
        }

        public void Delete(Envelope envelope)
        {
            _envelopes.Remove(envelope);
        }

        public void Enqueue(Envelope envelope)
        {
            _envelopes.Add(envelope);
        }

        public IEnumerable<Envelope> List()
        {
            return _envelopes;
        }

        public void Lock()
        {
            System.Threading.Monitor.Enter(_envelopes);
        }

        public void Release()
        {
            System.Threading.Monitor.Exit(_envelopes);
        }
    }
}
