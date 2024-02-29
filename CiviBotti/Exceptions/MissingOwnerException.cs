namespace CiviBotti.Exceptions;

using System;

public class MissingOwnerException : Exception
{
    
    public MissingOwnerException()
    {
    }
    
    public MissingOwnerException(string message)
        : base(message)
    {
    }

    protected MissingOwnerException(string message, Exception inner)
        : base(message, inner)
    {
    }
}