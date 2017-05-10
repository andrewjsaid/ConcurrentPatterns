using System;

namespace ConcurrentPatterns
{
    public class AsyncActionUnhandledExceptionEventArgs : EventArgs
    {

        public AsyncActionUnhandledExceptionEventArgs(Exception exception)
        {
            Exception = exception;
        }

        public Exception Exception { get; }
        public bool Handled { get; set; }

    }
}