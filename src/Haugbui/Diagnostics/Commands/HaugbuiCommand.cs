using System;
using Jotunn.Entities;

namespace Haugbui.Diagnostics.Commands
{
    internal abstract class HaugbuiCommand : ConsoleCommand
    {
        private Terminal _context;

        public sealed override void Run(string[] args, Terminal context)
        {
            _context = context;
            try
            {
                Execute(args);
            }
            catch (Exception e)
            {
                Print($"{Name} failed: {e.Message}");
                Log.Error($"{Name} failed: {e}");
            }
            finally
            {
                _context = null;
            }
        }

        protected abstract void Execute(string[] args);

        protected void Print(string line)
        {
            _context?.AddString(line);
            Log.Info(line);
        }
    }
}
