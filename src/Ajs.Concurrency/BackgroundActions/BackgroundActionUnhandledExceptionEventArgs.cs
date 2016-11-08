using System;

namespace Ajs.Concurrency.BackgroundActions
{
    public class BackgroundActionUnhandledExceptionEventArgs : EventArgs
    {

        public Exception Exception { get; }
        public bool Handled { get; set; }

        public BackgroundActionUnhandledExceptionEventArgs(Exception exception)
        {
            Exception = exception;
        }

    }
}