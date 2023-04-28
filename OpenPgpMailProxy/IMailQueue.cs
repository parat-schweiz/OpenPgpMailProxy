using System;
using System.Collections.Generic;

namespace OpenPgpMailProxy
{
    public interface IMailQueue
    {
        void Enqueue(Envelope envelope);

        IEnumerable<Envelope> List();

        void Delete(Envelope envelope);
    }
}
