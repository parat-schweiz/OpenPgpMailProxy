using System;

namespace OpenPgpMailProxy
{
    public class Context
    {
        public Config Config { get; private set; }

        public Context(Config config)
        {
            Config = config;
        }
    }
}
