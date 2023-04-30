using System;
using System.Collections.Generic;

namespace OpenPgpMailProxy
{
    public interface IMailbox
    {
        void Enqueue(Envelope envelope);

        IEnumerable<Envelope> List();

        void Delete(Envelope envelope);

        bool Exists(Envelope envelope);
    }
}
