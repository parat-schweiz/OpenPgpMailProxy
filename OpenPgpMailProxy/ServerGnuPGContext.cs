using System;
using MimeKit.Cryptography;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace OpenPgpMailProxy
{
    public class ServerGnuPGContext : GnuPGContext
    {
        public ServerGnuPGContext()
        {
        }

        protected override string GetPasswordForKey(PgpSecretKey key)
        {
            return string.Empty;
        }
    }
}
