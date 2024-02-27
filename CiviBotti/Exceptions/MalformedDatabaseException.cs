namespace CiviBotti.Exceptions;

using System;

public class MalformedDatabaseException : Exception
{
    
    public MalformedDatabaseException()
    {
    }
    
    public MalformedDatabaseException(string message)
        : base(message)
    {
    }

    protected MalformedDatabaseException(string message, Exception inner)
        : base(message, inner)
    {
    }
}