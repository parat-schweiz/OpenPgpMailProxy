using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace OpenPgpMailProxy
{
    public class Pop3Session : Session
    {
        private string _username = null;
        private bool _authorized = false;
        private Dictionary<Envelope, bool> _entries;
        private int _last = 0;
        private MailboxConfig _mailboxConfig;
        private IMailbox _mailbox;

        public Pop3Session(Context context, TcpClient client)
            : base(context, client)
        {
            Console.Error.WriteLine("New session");
        }

        protected override void WriteLine(string text)
        {
            Console.Error.WriteLine("Server: {0}", text);
            base.Write(text + "\r\n");
        }

        protected override string ReadLine()
        {
            var text = base.ReadLine();
            Console.Error.WriteLine("Client: {0}", text);
            return text;
        }

        protected override void Process()
        {
            WriteLine("+OK POP3 server ready");

            bool run = true;
            while (run)
            {
                var command = ReadLine();
                if (string.IsNullOrEmpty(command))
                    return;
                run = ProcessCommand(command);
            }
        }

        private bool Authenticate(string password)
        {
            var mailboxConfig = _context.Config.Mailboxes.SingleOrDefault(m => m.Username == _username);
            if ((mailboxConfig != null) && (mailboxConfig.LocalPassword == password))
            {
                _mailboxConfig = mailboxConfig;
                _mailbox = _context.Mailboxes.Get(_mailboxConfig.Username, MailboxType.InboundOutput);
                _mailbox.Lock();
                return true;
            }
            else
            {
                return false;
            }
        }

        private void ProcessStat()
        { 
            if (!_authorized)
            {
                WriteLine("-ERR unauthorized");
                return;
            }
            var envelopes = _entries.Where(e => e.Value).Select(e => e.Key);
            var count = envelopes.Count();
            var bytes = envelopes.Sum(e => e.Data.Length);
            WriteLine(string.Format("+OK {0} {1}", count, bytes));
        }

        private void ProcessList(Queue<string> arguments)
        {
            if (!_authorized)
            {
                WriteLine("-ERR unauthorized");
                return;
            }

            if (arguments.Any())
            {
                var idString = arguments.Dequeue();
                var message = GetMessage(idString);
                if (message != null)
                {
                    WriteLine(string.Format("+OK {0} {1}", message.Item1, message.Item2.Data.Length));
                }
            }
            else
            {
                WriteLine("+OK");
                int number = 1;
                foreach (var entry in _entries)
                {
                    if (entry.Value)
                    {
                        WriteLine(string.Format("{0} {1}", number, entry.Key.Data.Length));
                    }
                    number++;
                }
                WriteLine(".");
            }
        }

        private void ProcessRetr(Queue<string> arguments)
        {
            if (!_authorized)
            {
                WriteLine("-ERR unauthorized");
                return;
            }

            if (!arguments.Any())
            {
                WriteLine("+ERR missing argument");
            }

            var message = GetMessage(arguments.Dequeue());

            if (message != null)
            {
                _last = Math.Max(_last, message.Item1);
                WriteLine("+OK message follows");
                var lines = message.Item2.Data.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (line.StartsWith(".", StringComparison.CurrentCulture))
                    {
                        WriteLine("." + line);
                    }
                    else
                    {
                        WriteLine(line);
                    }
                }
                WriteLine(".");
            }
        }

        private Tuple<int, Envelope> GetMessage(string idString)
        {
            if (int.TryParse(idString, out int id))
            {
                if ((id < 1) || (id > _entries.Count))
                {
                    WriteLine("+ERR unknown message");
                }
                else
                {
                    var entry = _entries.ElementAt(id - 1);
                    if (entry.Value)
                    {
                        return new Tuple<int, Envelope>(id, entry.Key);
                    }
                    else
                    {
                        WriteLine("+ERR message deleted");
                    }
                }
            }
            return null;
        }

        private void ProcessDele(Queue<string> arguments)
        {
            if (!_authorized)
            {
                WriteLine("-ERR unauthorized");
                return;
            }

            if (!arguments.Any())
            {
                WriteLine("+ERR missing argument");
            }

            var message = GetMessage(arguments.Dequeue());
            _entries[message.Item2] = false;
            _last = Math.Max(_last, message.Item1);
            WriteLine("+OK deleted");
        }

        private bool ProcessCommand(string command)
        {
            var parts = new Queue<string>(command.Trim().Split(new string[] { " " }, StringSplitOptions.None));
            var commandWord = parts.Dequeue();
            switch (commandWord)
            {
                case "USER":
                    _username = parts.Dequeue();
                    WriteLine("+OK");
                    return true;
                case "PASS":
                    if (Authenticate(parts.Dequeue()))
                    {
                        Thread.Sleep(300);
                        WriteLine("+OK logged in");
                        _authorized = true;
                        _entries = new Dictionary<Envelope, bool>();
                        foreach (var envelope in _mailbox.List())
                        {
                            _entries.Add(envelope, true);
                        }
                    }
                    else
                    {
                        Thread.Sleep(1000);
                        WriteLine("-ERR invalid login");
                    }
                    return true;
                case "STAT":
                    ProcessStat();
                    return true;
                case "LIST":
                    ProcessList(parts);
                    return true;
                case "RETR":
                    ProcessRetr(parts);
                    return true;
                case "DELE":
                    ProcessDele(parts);
                    return true;
                case "LAST":
                    ProcessLast();
                    return true;
                case "RSET":
                    ProcessRset();
                    return true;
                case "NOOP":
                    WriteLine("+OK");
                    return true;
                case "QUIT":
                    ProcessQuit();
                    return false;
                default:
                    WriteLine("-ERR unknown command");
                    return true;
            }
        }

        private void ProcessLast()
        {
            WriteLine(string.Format("+OK {0}", _last));
        }

        private void ProcessRset()
        {
            _entries.All(e => _entries[e.Key] = true);
            WriteLine("+OK");
        }

        private void ProcessQuit()
        {
            if (_entries != null)
            {
                foreach (var envelope in _entries.Where(e => !e.Value).Select(e => e.Key))
                {
                    _mailbox.Delete(envelope);
                }
            }
            WriteLine("+OK bye");
        }

        protected override void Dispose(bool disposing)
        {
            Console.Error.WriteLine("Session closed");
            if (_mailbox != null)
            {
                _mailbox.Release();
                _mailbox = null;
            }
            base.Dispose(disposing);
        }
    }
}
