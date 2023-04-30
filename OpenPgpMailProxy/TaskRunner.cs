using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenPgpMailProxy
{
    public class TaskRunner : IDisposable
    {
        private readonly List<IMailTask> _tasks;
        private Thread _thread;
        private bool _run;

        public TaskRunner(params IMailTask[] tasks)
        {
            _tasks = new List<IMailTask>(tasks);
            _run = true;
            _thread = new Thread(Run);
            _thread.Start();
        }

        private void Run()
        { 
            while (_run)
            {
                foreach (var task in _tasks)
                {
                    task.Run();
                }
                Thread.Sleep(3000);
            }
        }

        public void Dispose()
        {
            if (_thread != null)
            {
                _run = false;
                _thread.Join();
                _thread = null;
            }
        }
    }
}
