using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using ThrowException.CSharpLibs.LogLib;

namespace OpenPgpMailProxy
{
    public class Pop3Session : Session
    {
        private string _username = null;
        private bool _authorized = false;
        private List<Envelope> _entries;
        private List<Envelope> _delete;
        private int _last = 0;
        private MailboxConfig _mailboxConfig;
        private IMailbox _mailbox;

        public Pop3Session(Context context, TcpClient client)
            : base(context, client)
        {
            _context.Log.Info("POP3 server: New session");
        }

        protected override void WriteLine(string text)
        {
            base.Write(text + "\r\n");
        }

        protected override string ReadLine()
        {
            var text = base.ReadLine();
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
                try
                {
                    run = ProcessCommand(command);
                }
                catch (Exception exception)
                {
                    _context.Log.Warning("POP3 server failure: {0}", exception.ToString());
                    return;
                }
            }
        }

        private bool Authenticate(string password)
        {
            var mailboxConfig = _context.Config.Mailboxes.SingleOrDefault(m => m.Username == _username);
            if ((mailboxConfig != null) && (mailboxConfig.LocalPassword == password))
            {
                _mailboxConfig = mailboxConfig;
                _mailbox = _context.Mailboxes.Get(_mailboxConfig.Username, MailboxType.InboundOutput);
                _context.Log.Notice("POP3 server: Authentication for {0} successful", _username);
                return true;
            }
            else
            {
                _context.Log.Notice("POP3 server: Authentication for {0} failed", _username);
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
            var envelopes = _entries
                .Where(e => !_delete.Contains(e))
                .Where(_mailbox.Exists)
                .ToList();
            var count = envelopes.Count();
            var bytes = envelopes.Sum(e => e.Data.Length);
            WriteLine(string.Format("+OK {0} {1}", count, bytes));
            _context.Log.Verbose("POP3 server: Stat completed");
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
                    if (!_delete.Contains(entry) && _mailbox.Exists(entry))
                    {
                        WriteLine(string.Format("{0} {1}", number, entry.Data.Length));
                    }
                    number++;
                }
                WriteLine(".");
            }
            _context.Log.Verbose("POP3 server: List completed");
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
                _context.Log.Info("POP3 server: Message retrieved");
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
                    if (!_delete.Contains(entry) && _mailbox.Exists(entry))
                    {
                        return new Tuple<int, Envelope>(id, entry);
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
            _delete.Add(message.Item2);
            _last = Math.Max(_last, message.Item1);
            WriteLine("+OK deleted");
            _context.Log.Verbose("POP3 server: Message deleted");
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
                        _entries = _mailbox.List().ToList();
                        _delete = new List<Envelope>();
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
                    _context.Log.Verbose("POP3 server: Unknown command");
                    return true;
            }
        }

        private void ProcessLast()
        {
            WriteLine(string.Format("+OK {0}", _last));
            _context.Log.Verbose("POP3 server: Last completed");
        }

        private void ProcessRset()
        {
            _delete.Clear();
            WriteLine("+OK");
            _context.Log.Verbose("POP3 server: Reset completed");
        }

        private void ProcessQuit()
        {
            _context.Log.Verbose("POP3 server: Quitting...");
            if (_entries != null)
            {
                foreach (var entry in _delete)
                {
                    _context.Log.Verbose("POP3 server: Deleting entry");
                    _mailbox.Delete(entry);
                }
            }
            WriteLine("+OK bye");
            _context.Log.Verbose("POP3 server: Quit completed");
        }

        protected override void Dispose(bool disposing)
        {
            _context.Log.Info("POP3 server: Session closed");
            _mailbox = null;
            base.Dispose(disposing);
        }
    }
}
